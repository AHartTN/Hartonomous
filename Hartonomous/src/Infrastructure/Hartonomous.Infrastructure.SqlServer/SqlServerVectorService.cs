using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.Infrastructure.SqlServer;

/// <summary>
/// SQL Server 2025 native vector service
/// Implements NinaDB vector capabilities using SQL Server's native VECTOR data type
/// Replaces external vector databases with integrated SQL Server solution
/// </summary>
public class SqlServerVectorService : IVectorService, IDisposable
{
    private readonly string _connectionString;
    private readonly ILogger<SqlServerVectorService> _logger;
    private readonly object _lockObject = new object();
    private bool _tablesInitialized = false;

    public SqlServerVectorService(IConfiguration configuration, ILogger<SqlServerVectorService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? throw new ArgumentException("DefaultConnection string required for SQL Server vector operations");

        _logger.LogInformation("SQL Server Vector Service initialized with connection");
    }

    /// <summary>
    /// Initialize the component embeddings collection
    /// Called during system startup
    /// </summary>
    public async Task InitializeAsync()
    {
        await EnsureVectorTablesExistAsync();
    }

    /// <summary>
    /// Initialize the component embeddings collection
    /// Called during system startup  
    /// </summary>
    public async Task InitializeCollectionAsync()
    {
        await EnsureVectorTablesExistAsync();
    }

    /// <summary>
    /// Ensures the vector storage tables exist
    /// Creates ComponentEmbeddings table with VECTOR column if not exists
    /// </summary>
    public Task EnsureVectorTablesExistAsync()
    {
        if (_tablesInitialized)
            return Task.CompletedTask;

        lock (_lockObject)
        {
            if (_tablesInitialized)
                return Task.CompletedTask;

            try
            {
                using var connection = new SqlConnection(_connectionString);
                connection.Open();
                var createTableSql = @"
                IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'ComponentEmbeddings')
                BEGIN
                    CREATE TABLE dbo.ComponentEmbeddings (
                        EmbeddingId UNIQUEIDENTIFIER PRIMARY KEY DEFAULT NEWID(),
                        ComponentId UNIQUEIDENTIFIER NOT NULL,
                        ModelId UNIQUEIDENTIFIER NOT NULL,
                        UserId NVARCHAR(128) NOT NULL,
                        ComponentType NVARCHAR(100) NOT NULL,
                        Description NVARCHAR(MAX),
                        EmbeddingVector VECTOR(1536) NOT NULL, -- OpenAI embedding dimensions
                        CreatedAt DATETIME2 DEFAULT GETUTCDATE(),

                        INDEX IX_ComponentEmbeddings_ComponentId (ComponentId),
                        INDEX IX_ComponentEmbeddings_ModelId_UserId (ModelId, UserId),
                        INDEX IX_ComponentEmbeddings_ComponentType (ComponentType)
                    );

                    -- Create vector index for similarity search
                    CREATE INDEX IX_ComponentEmbeddings_Vector
                    ON dbo.ComponentEmbeddings(EmbeddingVector)
                    USING VECTOR;
                END";

                using var command = new SqlCommand(createTableSql, connection);
                command.CommandTimeout = 120; // Allow time for index creation
                command.ExecuteNonQuery();

                _tablesInitialized = true;
                _logger.LogDebug("Vector tables initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to ensure vector tables exist");
                throw;
            }
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// Insert component embedding vector using SQL Server 2025 native VECTOR type
    /// </summary>
    public async Task InsertEmbeddingAsync(Guid componentId, Guid modelId, float[] embeddingVector,
        string componentType, string description, string userId)
    {
        try
        {
            await EnsureVectorTablesExistAsync();
            
            _logger.LogDebug("Inserting native VECTOR embedding for component {ComponentId} with {Dimensions} dimensions",
                componentId, embeddingVector.Length);

            var insertSql = @"
                MERGE dbo.ComponentEmbeddings AS target
                USING (SELECT @ComponentId AS ComponentId) AS source
                ON target.ComponentId = source.ComponentId AND target.UserId = @UserId
                WHEN MATCHED THEN
                    UPDATE SET
                        EmbeddingVector = @EmbeddingVector,
                        ComponentType = @ComponentType,
                        Description = @Description,
                        CreatedAt = GETUTCDATE()
                WHEN NOT MATCHED THEN
                    INSERT (ComponentId, ModelId, UserId, ComponentType, Description, EmbeddingVector)
                    VALUES (@ComponentId, @ModelId, @UserId, @ComponentType, @Description, @EmbeddingVector);";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(insertSql, connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);
            command.Parameters.AddWithValue("@ModelId", modelId);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@ComponentType", componentType);
            command.Parameters.AddWithValue("@Description", description ?? (object)DBNull.Value);
            
            // Use native VECTOR parameter - SQL Server 2025 handles float[] directly
            var vectorParam = command.Parameters.Add("@EmbeddingVector", SqlDbType.Variant);
            vectorParam.Value = embeddingVector;

            var rowsAffected = await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Inserted native VECTOR embedding for component {ComponentId}: {RowsAffected} rows affected", 
                componentId, rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to insert native VECTOR embedding for component {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Search for similar component embeddings using SQL Server native vector similarity
    /// Uses VECTOR_DISTANCE function for cosine similarity
    /// </summary>
    public async Task<IEnumerable<SimilarComponent>> SearchSimilarAsync(float[] queryEmbedding, string userId,
        int topK = 10, string? componentType = null)
    {
        try
        {
            _logger.LogDebug("Performing vector similarity search for user {UserId} with topK={TopK}", userId, topK);

            var queryVectorJson = JsonSerializer.Serialize(queryEmbedding);

            var searchSql = @"
                SELECT TOP (@TopK)
                    ce.ComponentId,
                    ce.ModelId,
                    ce.ComponentType,
                    ce.Description,
                    VECTOR_DISTANCE('cosine', ce.EmbeddingVector, CAST(@QueryVector AS VECTOR(1536))) AS Distance,
                    (1 - VECTOR_DISTANCE('cosine', ce.EmbeddingVector, CAST(@QueryVector AS VECTOR(1536)))) AS SimilarityScore
                FROM dbo.ComponentEmbeddings ce
                WHERE ce.UserId = @UserId
                " + (componentType != null ? "AND ce.ComponentType = @ComponentType" : "") + @"
                ORDER BY VECTOR_DISTANCE('cosine', ce.EmbeddingVector, CAST(@QueryVector AS VECTOR(1536))) ASC";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(searchSql, connection);
            command.Parameters.AddWithValue("@TopK", topK);
            command.Parameters.AddWithValue("@QueryVector", queryVectorJson);
            command.Parameters.AddWithValue("@UserId", userId);

            if (componentType != null)
                command.Parameters.AddWithValue("@ComponentType", componentType);

            var results = new List<SimilarComponent>();

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new SimilarComponent
                {
                    ComponentId = reader.GetGuid("ComponentId"),
                    ModelId = reader.GetGuid("ModelId"),
                    ComponentType = reader.GetString("ComponentType"),
                    Description = reader.IsDBNull("Description") ? string.Empty : reader.GetString("Description"),
                    SimilarityScore = reader.GetDouble("SimilarityScore"),
                    Metadata = new Dictionary<string, object>
                    {
                        ["distance"] = reader.GetDouble("Distance"),
                        ["search_method"] = "sql_server_vector"
                    }
                });
            }

            _logger.LogDebug("Vector search returned {ResultCount} similar components", results.Count);
            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform vector similarity search");
            throw;
        }
    }

    /// <summary>
    /// Delete embeddings for a specific component
    /// Called when components are removed
    /// </summary>
    public async Task DeleteEmbeddingsAsync(Guid componentId, string userId)
    {
        try
        {
            var deleteSql = @"
                DELETE FROM dbo.ComponentEmbeddings
                WHERE ComponentId = @ComponentId AND UserId = @UserId";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(deleteSql, connection);
            command.Parameters.AddWithValue("@ComponentId", componentId);
            command.Parameters.AddWithValue("@UserId", userId);

            var rowsAffected = await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Deleted embedding for component {ComponentId}: {RowsAffected} rows affected",
                componentId, rowsAffected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete embedding for component {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Get collection statistics
    /// For monitoring and administration
    /// </summary>
    public async Task<VectorCollectionStats> GetCollectionStatsAsync()
    {
        try
        {
            var statsSql = @"
                SELECT
                    COUNT(*) as RowCount,
                    COUNT(DISTINCT UserId) as UniqueUsers,
                    COUNT(DISTINCT ModelId) as UniqueModels,
                    COUNT(DISTINCT ComponentId) as UniqueComponents,
                    MIN(CreatedAt) as OldestEntry,
                    MAX(CreatedAt) as NewestEntry
                FROM dbo.ComponentEmbeddings";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(statsSql, connection);
            // No parameters needed for global stats

            using var reader = await command.ExecuteReaderAsync();
            if (await reader.ReadAsync())
            {
                var rowCount = reader.GetInt32("RowCount");
                return new VectorCollectionStats
                {
                    CollectionName = "ComponentEmbeddings",
                    RowCount = rowCount,
                    DataSize = (long)(rowCount * 1536 * 4) // Estimate: 1536 floats * 4 bytes
                };
            }

            return new VectorCollectionStats
            {
                CollectionName = "ComponentEmbeddings",
                RowCount = 0,
                DataSize = 0
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get collection stats");
            throw;
        }
    }

    /// <summary>
    /// Batch insert embeddings for better performance
    /// </summary>
    public async Task BatchInsertEmbeddingsAsync(IEnumerable<ComponentEmbedding> embeddings, string userId)
    {
        try
        {
            var embeddingsList = embeddings.ToList();
            _logger.LogDebug("Batch inserting {Count} embeddings for user {UserId}", embeddingsList.Count, userId);

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                var insertSql = @"
                    MERGE dbo.ComponentEmbeddings AS target
                    USING (SELECT @ComponentId AS ComponentId) AS source
                    ON target.ComponentId = source.ComponentId AND target.UserId = @UserId
                    WHEN MATCHED THEN
                        UPDATE SET
                            EmbeddingVector = CAST(@VectorJson AS VECTOR(1536)),
                            ComponentType = @ComponentType,
                            Description = @Description,
                            CreatedAt = GETUTCDATE()
                    WHEN NOT MATCHED THEN
                        INSERT (ComponentId, ModelId, UserId, ComponentType, Description, EmbeddingVector)
                        VALUES (@ComponentId, @ModelId, @UserId, @ComponentType, @Description,
                                CAST(@VectorJson AS VECTOR(1536)));";

                foreach (var embeddingItem in embeddingsList)
                {
                    var vectorJson = JsonSerializer.Serialize(embeddingItem.Vector);

                    using var command = new SqlCommand(insertSql, connection, transaction);
                    command.Parameters.AddWithValue("@ComponentId", embeddingItem.ComponentId);
                    command.Parameters.AddWithValue("@ModelId", embeddingItem.ModelId);
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@ComponentType", embeddingItem.ComponentType);
                    command.Parameters.AddWithValue("@Description", embeddingItem.Description ?? (object)DBNull.Value);
                    command.Parameters.AddWithValue("@VectorJson", vectorJson);

                    await command.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                _logger.LogDebug("Successfully batch inserted {Count} embeddings", embeddingsList.Count);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch insert embeddings");
            throw;
        }
    }

    /// <summary>
    /// Find similar components using SQL Server native COSINE_DISTANCE
    /// </summary>
    public async Task<IEnumerable<ComponentSimilarityDto>> FindSimilarComponentsAsync(float[] queryVector,
        double threshold, int maxResults, string userId)
    {
        try
        {
            _logger.LogDebug("Finding similar components with native VECTOR search for user {UserId}", userId);

            var searchSql = @"
                SELECT TOP (@MaxResults)
                    ce.ComponentId,
                    ce.ModelId,
                    ce.ComponentType,
                    ce.Description,
                    VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @QueryVector) AS Distance
                FROM dbo.ComponentEmbeddings ce
                WHERE ce.UserId = @UserId
                    AND (1 - VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @QueryVector)) >= @Threshold
                ORDER BY VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @QueryVector) ASC";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(searchSql, connection);
            command.Parameters.AddWithValue("@MaxResults", maxResults);
            command.Parameters.AddWithValue("@UserId", userId);
            command.Parameters.AddWithValue("@Threshold", threshold);
            
            // Use native VECTOR parameter
            var vectorParam = command.Parameters.Add("@QueryVector", SqlDbType.Variant);
            vectorParam.Value = queryVector;

            var results = new List<ComponentSimilarityDto>();
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var distance = reader.GetDouble("Distance");
                results.Add(new ComponentSimilarityDto(
                    reader.GetGuid("ComponentId"),
                    reader.GetGuid("ModelId"),
                    reader.GetString("ComponentType"),
                    reader.GetString("ComponentType"), // ComponentName - using type as name for now
                    reader.IsDBNull("Description") ? string.Empty : reader.GetString("Description"),
                    distance));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find similar components");
            throw;
        }
    }

    /// <summary>
    /// Batch insert multiple component embeddings
    /// </summary>
    public async Task BatchInsertEmbeddingsAsync(IEnumerable<ComponentEmbeddingDto> embeddings, string userId)
    {
        try
        {
            await EnsureVectorTablesExistAsync();
            
            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var embedding in embeddings)
                {
                    var insertSql = @"
                        INSERT INTO dbo.ComponentEmbeddings 
                        (ComponentId, ModelId, UserId, ComponentType, Description, EmbeddingVector)
                        VALUES (@ComponentId, @ModelId, @UserId, @ComponentType, @Description, @EmbeddingVector)";

                    using var command = new SqlCommand(insertSql, connection, transaction);
                    command.Parameters.AddWithValue("@ComponentId", embedding.ComponentId);
                    command.Parameters.AddWithValue("@ModelId", embedding.ModelId);
                    command.Parameters.AddWithValue("@UserId", userId);
                    command.Parameters.AddWithValue("@ComponentType", embedding.ComponentType);
                    command.Parameters.AddWithValue("@Description", embedding.Description ?? (object)DBNull.Value);
                    
                    var vectorParam = command.Parameters.Add("@EmbeddingVector", SqlDbType.Variant);
                    vectorParam.Value = embedding.EmbeddingVector;

                    await command.ExecuteNonQueryAsync();
                }

                transaction.Commit();
                _logger.LogDebug("Batch inserted {Count} embeddings for user {UserId}", 
                    embeddings.Count(), userId);
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch insert embeddings");
            throw;
        }
    }

    /// <summary>
    /// Delete all embeddings for a specific model
    /// </summary>
    public async Task DeleteModelEmbeddingsAsync(Guid modelId, string userId)
    {
        try
        {
            var deleteSql = @"
                DELETE FROM dbo.ComponentEmbeddings 
                WHERE ModelId = @ModelId AND UserId = @UserId";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(deleteSql, connection);
            command.Parameters.AddWithValue("@ModelId", modelId);
            command.Parameters.AddWithValue("@UserId", userId);

            var deletedRows = await command.ExecuteNonQueryAsync();
            _logger.LogDebug("Deleted {DeletedRows} embeddings for model {ModelId}", deletedRows, modelId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete embeddings for model {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Get embedding statistics for a model
    /// </summary>
    public async Task<ModelEmbeddingStatsDto> GetModelEmbeddingStatsAsync(Guid modelId, string userId)
    {
        try
        {
            var statsSql = @"
                SELECT 
                    COUNT(*) as TotalComponents,
                    ComponentType,
                    COUNT(*) as TypeCount
                FROM dbo.ComponentEmbeddings 
                WHERE ModelId = @ModelId AND UserId = @UserId
                GROUP BY ComponentType";

            using var connection = new SqlConnection(_connectionString);
            await connection.OpenAsync();
            using var command = new SqlCommand(statsSql, connection);
            command.Parameters.AddWithValue("@ModelId", modelId);
            command.Parameters.AddWithValue("@UserId", userId);

            var typeCounts = new Dictionary<string, int>();
            var totalComponents = 0;

            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var componentType = reader.GetString("ComponentType");
                var count = reader.GetInt32("TypeCount");
                typeCounts[componentType] = count;
                totalComponents += count;
            }

            return new ModelEmbeddingStatsDto(
                modelId,
                totalComponents,
                typeCounts,
                1536, // OpenAI embedding dimensions
                DateTime.UtcNow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get embedding stats for model {ModelId}", modelId);
            throw;
        }
    }

    public void Dispose()
    {
        try
        {
            _logger.LogDebug("SQL Server Vector Service disposed");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error during SQL Server Vector Service disposal");
        }
    }
}

/// <summary>
/// Component embedding for batch operations
/// </summary>
public class ComponentEmbedding
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public float[] Vector { get; set; } = Array.Empty<float>();
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
}

/// <summary>
/// Collection statistics from SQL Server vector database
/// Provides backward compatibility for legacy vector operations
/// </summary>
public class VectorCollectionStats
{
    public string CollectionName { get; set; } = string.Empty;
    public long RowCount { get; set; }
    public long DataSize { get; set; }
}

/// <summary>
/// Similar component result from vector search
/// Provides SQL Server VECTOR similarity search results
/// </summary>
public class SimilarComponent
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double SimilarityScore { get; set; }
    public Dictionary<string, object> Metadata { get; set; } = new();
}