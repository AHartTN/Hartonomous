using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Infrastructure.Neo4j.Interfaces;
using Hartonomous.Infrastructure.Milvus;
using Hartonomous.Infrastructure.Milvus.Interfaces;
using Hartonomous.Infrastructure.EventStreaming.Interfaces;

namespace Hartonomous.Infrastructure.EventStreaming;

/// <summary>
/// Orchestrates operations across the data fabric components
/// Provides high-level interface for data fabric operations
/// </summary>
public class DataFabricOrchestrator : IEventStreamingService
{
    private readonly IGraphService _neo4jService;
    private readonly IVectorService _vectorService;
    private readonly ILogger<DataFabricOrchestrator> _logger;
    private readonly string _connectionString;

    public DataFabricOrchestrator(
        IGraphService neo4jService,
        IVectorService vectorService,
        ILogger<DataFabricOrchestrator> logger,
        IConfiguration configuration)
    {
        _neo4jService = neo4jService ?? throw new ArgumentNullException(nameof(neo4jService));
        _vectorService = vectorService ?? throw new ArgumentNullException(nameof(vectorService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
    }

    /// <summary>
    /// Initialize the entire data fabric
    /// Called during application startup
    /// </summary>
    public async Task InitializeAsync()
    {
        _logger.LogInformation("Initializing Hartonomous data fabric...");

        try
        {
            // Initialize SQL Server vector tables
            await _vectorService.InitializeCollectionAsync();
            _logger.LogInformation("SQL Server vector database initialized");

            // Neo4j doesn't require explicit initialization, but we could add schema setup here
            _logger.LogInformation("Neo4j knowledge graph ready");

            _logger.LogInformation("Hartonomous data fabric initialization complete");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize data fabric");
            throw;
        }
    }

    /// <summary>
    /// Get comprehensive model insights by combining graph and vector search
    /// </summary>
    public async Task<ModelInsights> GetModelInsightsAsync(Guid modelId, string userId)
    {
        _logger.LogDebug("Getting model insights for model: {ModelId}", modelId);

        try
        {
            // Get a sample component from this model to start analysis
            var sampleComponent = await GetSampleComponentAsync(modelId, userId);
            if (sampleComponent == null)
            {
                return new ModelInsights { ModelId = modelId, Message = "No components found for this model" };
            }

            // Get graph relationships
            var relationshipPaths = await _neo4jService.GetModelPathsAsync(sampleComponent.Id, 3, userId);

            // Get similar components via vector search
            var similarComponents = await _neo4jService.FindSimilarComponentsAsync(sampleComponent.Id, userId, 10);

            // Get collection statistics
            var vectorStats = await _vectorService.GetCollectionStatsAsync();

            return new ModelInsights
            {
                ModelId = modelId,
                ComponentCount = relationshipPaths.Count(),
                RelationshipPaths = relationshipPaths.ToList(),
                SimilarComponents = similarComponents.ToList(),
                VectorIndexSize = vectorStats.RowCount,
                VectorDataSize = vectorStats.DataSize,
                Message = "Analysis complete"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get model insights for model: {ModelId}", modelId);
            throw;
        }
    }

    /// <summary>
    /// Perform semantic search across the entire model knowledge base
    /// </summary>
    public async Task<SemanticSearchResult> PerformSemanticSearchAsync(float[] queryEmbedding, string userId,
        string? componentType = null, int topK = 20)
    {
        _logger.LogDebug("Performing semantic search for user: {UserId}", userId);

        try
        {
            // Get vector similarity matches
            var vectorMatches = await _vectorService.SearchSimilarAsync(queryEmbedding, userId, topK, componentType);

            // For each vector match, get its graph context
            var enrichedResults = new List<EnrichedSearchResult>();

            foreach (var match in vectorMatches.Take(10)) // Limit to prevent too many graph queries
            {
                var graphContext = await _neo4jService.GetModelPathsAsync(match.ComponentId, 2, userId);

                enrichedResults.Add(new EnrichedSearchResult
                {
                    Component = match,
                    GraphContext = graphContext.ToList()
                });
            }

            return new SemanticSearchResult
            {
                Query = "Embedding search",
                TotalMatches = vectorMatches.Count(),
                Results = enrichedResults,
                SearchTime = DateTime.UtcNow
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform semantic search for user: {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Health check for all data fabric components
    /// </summary>
    public async Task<DataFabricHealth> CheckHealthAsync()
    {
        _logger.LogDebug("Starting data fabric health check");

        var health = new DataFabricHealth
        {
            CheckTime = DateTime.UtcNow
        };

        try
        {
            // Check Vector Service
            var vectorStats = await _vectorService.GetCollectionStatsAsync();
            health.MilvusStatus = "Healthy";
            health.MilvusDetails = $"Collections: 1, Rows: {vectorStats.RowCount:N0}";
            _logger.LogDebug("Vector service health check passed");
        }
        catch (Exception ex)
        {
            health.MilvusStatus = "Unhealthy";
            health.MilvusDetails = ex.Message;
            _logger.LogWarning(ex, "Vector service health check failed");
        }

        try
        {
            // Check Neo4j with a simple query
            var testResults = await _neo4jService.FindSimilarComponentsAsync(Guid.NewGuid(), "health-check", 1);
            health.Neo4jStatus = "Healthy";
            health.Neo4jDetails = "Connection successful";
            _logger.LogDebug("Neo4j health check passed");
        }
        catch (Exception ex)
        {
            health.Neo4jStatus = "Unhealthy";
            health.Neo4jDetails = ex.Message;
            _logger.LogWarning(ex, "Neo4j health check failed");
        }

        health.OverallStatus = (health.MilvusStatus == "Healthy" && health.Neo4jStatus == "Healthy")
            ? "Healthy" : "Degraded";

        _logger.LogDebug("Data fabric health check completed: {Status}", health.OverallStatus);
        return health;
    }

    /// <summary>
    /// Get a sample component for analysis (placeholder implementation)
    /// In production, this would query the SQL Server database
    /// </summary>
    private async Task<ModelComponentInfo?> GetSampleComponentAsync(Guid modelId, string userId)
    {
        const string sql = @"
            SELECT TOP 1 c.ComponentId, c.ComponentName, c.ComponentType, c.Description
            FROM dbo.ModelComponents c
            INNER JOIN dbo.Models m ON c.ModelId = m.ModelId
            WHERE m.ModelId = @ModelId AND m.UserId = @UserId
            ORDER BY c.CreatedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var component = await connection.QueryFirstOrDefaultAsync(sql, new { ModelId = modelId, UserId = userId });

        if (component == null)
            return null;

        return new ModelComponentInfo
        {
            Id = component.ComponentId,
            Name = component.ComponentName,
            Type = component.ComponentType
        };
    }
}

/// <summary>
/// Comprehensive model insights combining graph and vector analysis
/// </summary>
public class ModelInsights
{
    public Guid ModelId { get; set; }
    public int ComponentCount { get; set; }
    public List<ModelComponentPath> RelationshipPaths { get; set; } = new();
    public List<ModelComponentInfo> SimilarComponents { get; set; } = new();
    public long VectorIndexSize { get; set; }
    public long VectorDataSize { get; set; }
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// Semantic search results with graph context
/// </summary>
public class SemanticSearchResult
{
    public string Query { get; set; } = string.Empty;
    public int TotalMatches { get; set; }
    public List<EnrichedSearchResult> Results { get; set; } = new();
    public DateTime SearchTime { get; set; }
}

/// <summary>
/// Search result enriched with graph relationship context
/// </summary>
public class EnrichedSearchResult
{
    public SimilarComponent Component { get; set; } = new();
    public List<ModelComponentPath> GraphContext { get; set; } = new();
}

/// <summary>
/// Health status of the data fabric
/// </summary>
public class DataFabricHealth
{
    public DateTime CheckTime { get; set; }
    public string OverallStatus { get; set; } = string.Empty;
    public string Neo4jStatus { get; set; } = string.Empty;
    public string Neo4jDetails { get; set; } = string.Empty;
    public string MilvusStatus { get; set; } = string.Empty;
    public string MilvusDetails { get; set; } = string.Empty;
}