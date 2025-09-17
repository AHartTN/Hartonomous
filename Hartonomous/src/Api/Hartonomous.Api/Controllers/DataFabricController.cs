using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Hartonomous.Infrastructure.Security;
using Hartonomous.Infrastructure.EventStreaming;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Infrastructure.Milvus;

namespace Hartonomous.Api.Controllers;

/// <summary>
/// API controller for data fabric operations
/// Exposes Neo4j, Milvus, and orchestrated operations
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class DataFabricController : ControllerBase
{
    private readonly DataFabricOrchestrator _orchestrator;
    private readonly Neo4jService _neo4jService;
    private readonly MilvusService _milvusService;
    private readonly ILogger<DataFabricController> _logger;

    public DataFabricController(
        DataFabricOrchestrator orchestrator,
        Neo4jService neo4jService,
        MilvusService milvusService,
        ILogger<DataFabricController> logger)
    {
        _orchestrator = orchestrator;
        _neo4jService = neo4jService;
        _milvusService = milvusService;
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive insights for a model using the complete data fabric
    /// </summary>
    [HttpGet("models/{modelId}/insights")]
    public async Task<ActionResult<ModelInsights>> GetModelInsights(Guid modelId)
    {
        try
        {
            var userId = User.GetUserId();
            var insights = await _orchestrator.GetModelInsightsAsync(modelId, userId);
            return Ok(insights);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get model insights for model: {ModelId}", modelId);
            return StatusCode(500, "Failed to retrieve model insights");
        }
    }

    /// <summary>
    /// Perform semantic search across model components
    /// </summary>
    [HttpPost("search/semantic")]
    public async Task<ActionResult<SemanticSearchResult>> SemanticSearch([FromBody] SemanticSearchRequest request)
    {
        try
        {
            var userId = User.GetUserId();

            if (request.QueryEmbedding == null || request.QueryEmbedding.Length == 0)
            {
                return BadRequest("Query embedding is required");
            }

            var results = await _orchestrator.PerformSemanticSearchAsync(
                request.QueryEmbedding, userId, request.ComponentType, request.TopK ?? 20);

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform semantic search");
            return StatusCode(500, "Failed to perform semantic search");
        }
    }

    /// <summary>
    /// Get model component relationships using Neo4j graph traversal
    /// </summary>
    [HttpGet("components/{componentId}/paths")]
    public async Task<ActionResult<IEnumerable<ModelComponentPath>>> GetComponentPaths(
        Guid componentId, [FromQuery] int maxDepth = 3)
    {
        try
        {
            var userId = User.GetUserId();
            var paths = await _neo4jService.GetModelPathsAsync(componentId, maxDepth, userId);
            return Ok(paths);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get component paths for: {ComponentId}", componentId);
            return StatusCode(500, "Failed to retrieve component paths");
        }
    }

    /// <summary>
    /// Find similar components using Neo4j graph analysis
    /// </summary>
    [HttpGet("components/{componentId}/similar")]
    public async Task<ActionResult<IEnumerable<ModelComponentInfo>>> GetSimilarComponents(
        Guid componentId, [FromQuery] int limit = 10)
    {
        try
        {
            var userId = User.GetUserId();
            var similar = await _neo4jService.FindSimilarComponentsAsync(componentId, userId, limit);
            return Ok(similar);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find similar components for: {ComponentId}", componentId);
            return StatusCode(500, "Failed to find similar components");
        }
    }

    /// <summary>
    /// Get vector database statistics
    /// </summary>
    [HttpGet("milvus/stats")]
    public async Task<ActionResult<MilvusCollectionStats>> GetMilvusStats()
    {
        try
        {
            var stats = await _milvusService.GetCollectionStatsAsync();
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get Milvus statistics");
            return StatusCode(500, "Failed to retrieve vector database statistics");
        }
    }

    /// <summary>
    /// Health check for the entire data fabric
    /// </summary>
    [HttpGet("health")]
    public async Task<ActionResult<DataFabricHealth>> GetDataFabricHealth()
    {
        try
        {
            var health = await _orchestrator.CheckHealthAsync();
            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check data fabric health");
            return StatusCode(500, "Health check failed");
        }
    }

    /// <summary>
    /// Search components by vector similarity
    /// </summary>
    [HttpPost("search/vector")]
    public async Task<ActionResult<IEnumerable<SimilarComponent>>> VectorSearch([FromBody] VectorSearchRequest request)
    {
        try
        {
            var userId = User.GetUserId();

            if (request.QueryEmbedding == null || request.QueryEmbedding.Length == 0)
            {
                return BadRequest("Query embedding is required");
            }

            var results = await _milvusService.SearchSimilarAsync(
                request.QueryEmbedding, userId, request.TopK ?? 10, request.ComponentType);

            return Ok(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform vector search");
            return StatusCode(500, "Failed to perform vector search");
        }
    }
}

/// <summary>
/// Request model for semantic search
/// </summary>
public class SemanticSearchRequest
{
    public float[] QueryEmbedding { get; set; } = Array.Empty<float>();
    public string? ComponentType { get; set; }
    public int? TopK { get; set; }
}

/// <summary>
/// Request model for vector search
/// </summary>
public class VectorSearchRequest
{
    public float[] QueryEmbedding { get; set; } = Array.Empty<float>();
    public string? ComponentType { get; set; }
    public int? TopK { get; set; }
}