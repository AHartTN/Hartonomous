using Milvus.Client;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Milvus;

/// <summary>
/// Service for managing Milvus vector database operations
/// Implements semantic search as read-replica in the Hartonomous data fabric
/// </summary>
public class MilvusService : IDisposable
{
    private readonly MilvusClient _client;
    private readonly ILogger<MilvusService> _logger;
    private readonly string _collectionName = "component_embeddings";

    public MilvusService(IConfiguration configuration, ILogger<MilvusService> logger)
    {
        _logger = logger;

        try
        {
            var host = configuration["Milvus:Host"] ?? throw new ArgumentException("Milvus:Host configuration required");
            var port = int.Parse(configuration["Milvus:Port"] ?? "19530");
            var username = configuration["Milvus:Username"];
            var password = configuration["Milvus:Password"];

            _client = new MilvusClient(host, port, username: username, password: password);
            _logger.LogInformation("Milvus client initialized for {Host}:{Port}", host, port);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Milvus client");
            throw;
        }
    }

    /// <summary>
    /// Initialize the component embeddings collection
    /// Called during system startup
    /// </summary>
    public async Task InitializeCollectionAsync()
    {
        try
        {
            // Check if collection already exists
            var hasCollection = await _client.HasCollectionAsync(_collectionName);
            if (hasCollection)
            {
                _logger.LogDebug("Collection {CollectionName} already exists", _collectionName);
                return;
            }

            // Create collection schema for component embeddings
            var schema = new CollectionSchema
            {
                CollectionName = _collectionName,
                Description = "Vector embeddings for model components",
                Fields = new List<FieldSchema>
                {
                    new() { Name = "id", DataType = DataType.VarChar, MaxLength = 36, IsPrimaryKey = true },
                    new() { Name = "component_id", DataType = DataType.VarChar, MaxLength = 36 },
                    new() { Name = "model_id", DataType = DataType.VarChar, MaxLength = 36 },
                    new() { Name = "user_id", DataType = DataType.VarChar, MaxLength = 128 },
                    new() { Name = "component_name", DataType = DataType.VarChar, MaxLength = 512 },
                    new() { Name = "component_type", DataType = DataType.VarChar, MaxLength = 128 },
                    new() { Name = "embedding", DataType = DataType.FloatVector, Dimension = 768 }, // Standard BERT embedding size
                    new() { Name = "created_at", DataType = DataType.Int64 }
                }
            };

            await _client.CreateCollectionAsync(schema);

            // Create index for vector search
            var indexParams = new IndexParams
            {
                FieldName = "embedding",
                IndexType = IndexType.IvfFlat,
                MetricType = MetricType.L2,
                ExtraParams = new Dictionary<string, object> { { "nlist", 1024 } }
            };

            await _client.CreateIndexAsync(_collectionName, indexParams);
            await _client.LoadCollectionAsync(_collectionName);

            _logger.LogInformation("Created and loaded collection: {CollectionName}", _collectionName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Milvus collection: {CollectionName}", _collectionName);
            throw;
        }
    }

    /// <summary>
    /// Insert component embedding vector
    /// Called from CDC pipeline when embeddings are calculated
    /// </summary>
    public async Task InsertEmbeddingAsync(Guid componentId, Guid modelId, string userId,
        string componentName, string componentType, float[] embedding)
    {
        try
        {
            _logger.LogDebug("Inserting embedding for component {ComponentId} (dimension: {Dimension})", componentId, embedding.Length);

            var entity = new Dictionary<string, object>
            {
                { "id", Guid.NewGuid().ToString() },
                { "component_id", componentId.ToString() },
                { "model_id", modelId.ToString() },
                { "user_id", userId },
                { "component_name", componentName },
                { "component_type", componentType },
                { "embedding", embedding },
                { "created_at", DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            };

            await _client.InsertAsync(_collectionName, new[] { entity });
            await _client.FlushAsync(_collectionName);

            _logger.LogDebug("Inserted embedding for component: {ComponentId}", componentId);
        }
        catch (MilvusException ex)
        {
            _logger.LogError(ex, "Milvus error inserting embedding for {ComponentId}: {Message}", componentId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error inserting embedding for component: {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Perform semantic similarity search
    /// Core capability for model component discovery
    /// </summary>
    public async Task<IEnumerable<SimilarComponent>> SearchSimilarAsync(float[] queryEmbedding, string userId,
        int topK = 10, string? componentType = null)
    {
        try
        {
            _logger.LogDebug("Searching for similar components (topK: {TopK}, type: {ComponentType})", topK, componentType ?? "any");

            var searchParams = new SearchParams
            {
                CollectionName = _collectionName,
                VectorFieldName = "embedding",
                Vectors = new[] { queryEmbedding },
                TopK = topK,
                MetricType = MetricType.L2,
                OutputFields = new[] { "component_id", "model_id", "component_name", "component_type", "created_at" }
            };

            // Add user filter for security
            var filter = $"user_id == \"{userId}\"";

            // Add component type filter if specified
            if (!string.IsNullOrEmpty(componentType))
            {
                filter += $" && component_type == \"{componentType}\"";
            }

            searchParams.Expression = filter;

            var results = await _client.SearchAsync(searchParams);

            var similarComponents = new List<SimilarComponent>();

            foreach (var result in results.Results)
            {
                foreach (var hit in result.Hits)
                {
                    similarComponents.Add(new SimilarComponent
                    {
                        ComponentId = Guid.Parse(hit.Entity["component_id"].ToString()!),
                        ModelId = Guid.Parse(hit.Entity["model_id"].ToString()!),
                        ComponentName = hit.Entity["component_name"].ToString()!,
                        ComponentType = hit.Entity["component_type"].ToString()!,
                        SimilarityScore = hit.Score,
                        CreatedAt = DateTimeOffset.FromUnixTimeMilliseconds((long)hit.Entity["created_at"]).DateTime
                    });
                }
            }

            _logger.LogDebug("Found {Count} similar components for user {UserId}", similarComponents.Count, userId);
            return similarComponents.OrderBy(c => c.SimilarityScore); // L2 distance: lower is more similar
        }
        catch (MilvusException ex)
        {
            _logger.LogError(ex, "Milvus error searching similar components for {UserId}: {Message}", userId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error searching similar components for user: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Search for components by text query
    /// Requires text-to-vector conversion in calling code
    /// </summary>
    public async Task<IEnumerable<SimilarComponent>> SearchByTextAsync(string queryText, string userId,
        int topK = 10, string? componentType = null)
    {
        // NOTE: This would require a text embedding service (like OpenAI, BERT, etc.)
        // For now, this is a placeholder that would need integration with an embedding API

        _logger.LogWarning("SearchByTextAsync requires text embedding service integration - not implemented");
        throw new NotImplementedException("Text embedding service integration required");
    }

    /// <summary>
    /// Delete component embeddings
    /// Called from CDC pipeline when components are deleted
    /// </summary>
    public async Task DeleteEmbeddingsAsync(Guid componentId, string userId)
    {
        try
        {
            _logger.LogDebug("Deleting embeddings for component {ComponentId}", componentId);

            var deleteExpression = $"component_id == \"{componentId}\" && user_id == \"{userId}\"";
            await _client.DeleteAsync(_collectionName, deleteExpression);
            await _client.FlushAsync(_collectionName);

            _logger.LogDebug("Deleted embeddings for component: {ComponentId}", componentId);
        }
        catch (MilvusException ex)
        {
            _logger.LogError(ex, "Milvus error deleting embeddings for {ComponentId}: {Message}", componentId, ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error deleting embeddings for component: {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Get collection statistics
    /// For monitoring and administration
    /// </summary>
    public async Task<MilvusCollectionStats> GetCollectionStatsAsync()
    {
        try
        {
            _logger.LogDebug("Getting collection statistics for {CollectionName}", _collectionName);

            var stats = await _client.GetCollectionStatisticsAsync(_collectionName);
            return new MilvusCollectionStats
            {
                CollectionName = _collectionName,
                RowCount = stats.RowCount,
                DataSize = stats.DataSize
            };
        }
        catch (MilvusException ex)
        {
            _logger.LogError(ex, "Milvus error getting collection statistics: {Message}", ex.Message);
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error getting collection statistics");
            throw;
        }
    }

    public void Dispose()
    {
        _client?.Dispose();
    }
}

/// <summary>
/// Represents a similar component from vector search
/// </summary>
public class SimilarComponent
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public float SimilarityScore { get; set; }
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Collection statistics for monitoring
/// </summary>
public class MilvusCollectionStats
{
    public string CollectionName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public long DataSize { get; set; }
}