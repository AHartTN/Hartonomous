/*
 * Copyright (c) 2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the MCP Vector Search Handler implementing SqlVector<float> operations
 * for SQL Server 2025 VECTOR data type in Multi-Context Protocol communication.
 */

using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using Hartonomous.Core.Data;
using Hartonomous.Core.DTOs;
using Hartonomous.Infrastructure.SqlServer;

namespace Hartonomous.MCP.Handlers;

/// <summary>
/// MCP handler for vector search operations using SQL Server 2025 VECTOR data type
/// Implements EF Core Database.SqlQuery<T> with SqlVector<float> parameter binding
/// Provides integrated SQL Server vector search capabilities for MCP agents
/// </summary>
public class VectorSearchHandler : IDisposable
{
    private readonly SqlVectorQueryService _vectorQueryService;
    private readonly HartonomousDbContext _dbContext;
    private readonly ILogger<VectorSearchHandler> _logger;

    public VectorSearchHandler(
        SqlVectorQueryService vectorQueryService,
        HartonomousDbContext dbContext,
        ILogger<VectorSearchHandler> logger)
    {
        _vectorQueryService = vectorQueryService ?? throw new ArgumentNullException(nameof(vectorQueryService));
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Handle vector similarity search requests from MCP agents
    /// Uses SqlVector<float> parameter binding for optimal performance
    /// </summary>
    public async Task<VectorSearchResponse> HandleVectorSearchAsync(
        VectorSearchRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing vector search request for user {UserId} with {Dimensions} dimensions",
                userId, request.QueryEmbedding.Length);

            // Validate embedding dimensions
            if (request.QueryEmbedding.Length != 1536)
            {
                throw new ArgumentException("Query embedding must be 1536 dimensions for OpenAI compatibility");
            }

            // Execute vector similarity search using SqlVector<float>
            var searchResults = await _vectorQueryService.ExecuteVectorSimilaritySearchAsync(
                request.QueryEmbedding,
                userId,
                request.SimilarityThreshold,
                request.MaxResults,
                request.ComponentType);

            // Convert to response format
            var results = searchResults.Select(r => new VectorSearchResultDto
            {
                ComponentId = r.ComponentId,
                ModelId = r.ModelId,
                ComponentType = r.ComponentType,
                Description = r.Description,
                SimilarityScore = r.SimilarityScore,
                Distance = r.Distance,
                Metadata = new Dictionary<string, object>
                {
                    ["search_method"] = "sql_server_vector",
                    ["vector_distance"] = r.Distance,
                    ["timestamp"] = DateTime.UtcNow.ToString("O")
                }
            }).ToList();

            _logger.LogInformation("Vector search completed. Found {ResultCount} similar components", results.Count);

            return new VectorSearchResponse
            {
                RequestId = request.RequestId,
                Results = results,
                TotalResults = results.Count,
                SearchMetadata = new Dictionary<string, object>
                {
                    ["query_dimensions"] = request.QueryEmbedding.Length,
                    ["similarity_threshold"] = request.SimilarityThreshold,
                    ["max_results"] = request.MaxResults,
                    ["component_type_filter"] = request.ComponentType ?? "all",
                    ["execution_time_ms"] = 0, // Would be measured in production
                    ["database_engine"] = "sql_server_2025_vector"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle vector search request for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Handle agent capability-based search using modernized SQL patterns
    /// Demonstrates EF Core Database.SqlQuery<T> for agent discovery
    /// </summary>
    public async Task<AgentSearchResponse> HandleAgentCapabilitySearchAsync(
        AgentCapabilitySearchRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing agent capability search for user {UserId} with {CapabilityCount} required capabilities",
                userId, request.RequiredCapabilities?.Length ?? 0);

            // Execute agent query using EF Core SqlQuery<T>
            var agentResults = await _vectorQueryService.QueryAgentsByCapabilitiesAsync(
                request.RequiredCapabilities ?? Array.Empty<string>(),
                userId,
                request.RequiredStatus);

            // Filter results by capability matching in application layer
            var filteredResults = agentResults.ToList();
            if (request.RequiredCapabilities?.Any() == true)
            {
                filteredResults = filteredResults.Where(agent =>
                {
                    if (string.IsNullOrEmpty(agent.Capabilities))
                        return false;

                    var agentCapabilities = JsonSerializer.Deserialize<string[]>(agent.Capabilities) ?? Array.Empty<string>();
                    return request.RequiredCapabilities.All(required =>
                        agentCapabilities.Contains(required, StringComparer.OrdinalIgnoreCase));
                }).ToList();
            }

            // Convert to response format
            var results = filteredResults.Select(a => new AgentSearchResultDto
            {
                AgentId = a.AgentId,
                AgentName = a.AgentName,
                AgentType = a.AgentType,
                ConnectionId = a.ConnectionId,
                Capabilities = string.IsNullOrEmpty(a.Capabilities)
                    ? Array.Empty<string>()
                    : JsonSerializer.Deserialize<string[]>(a.Capabilities) ?? Array.Empty<string>(),
                Description = a.Description,
                Status = (Core.Enums.AgentStatus)a.Status,
                LastHeartbeat = a.LastHeartbeat,
                Metadata = new Dictionary<string, object>
                {
                    ["registered_at"] = a.RegisteredAt.ToString("O"),
                    ["capability_match_count"] = request.RequiredCapabilities?.Length ?? 0
                }
            }).ToList();

            _logger.LogInformation("Agent capability search completed. Found {ResultCount} matching agents", results.Count);

            return new AgentSearchResponse
            {
                RequestId = request.RequestId,
                Results = results,
                TotalResults = results.Count,
                SearchMetadata = new Dictionary<string, object>
                {
                    ["required_capabilities"] = request.RequiredCapabilities ?? Array.Empty<string>(),
                    ["required_status"] = request.RequiredStatus?.ToString() ?? "any",
                    ["total_agents_checked"] = agentResults.Count(),
                    ["agents_after_filtering"] = results.Count
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle agent capability search for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Handle model component analysis using vector embeddings
    /// Combines relational and vector data for comprehensive analysis
    /// </summary>
    public async Task<ModelAnalysisResponse> HandleModelComponentAnalysisAsync(
        ModelAnalysisRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing model component analysis for model {ModelId} with user {UserId}",
                request.ModelId, userId);

            // Execute model component analysis using vector embeddings
            var analysisResults = await _vectorQueryService.ExecuteModelComponentAnalysisAsync(
                request.ModelId,
                request.SeedEmbedding,
                userId,
                request.MaxComponents);

            // Convert to response format
            var components = analysisResults.Select(r => new ModelComponentAnalysisDto
            {
                ComponentId = r.ComponentId,
                ComponentName = r.ComponentName,
                ComponentType = r.ComponentType,
                Description = r.Description,
                CreatedAt = r.CreatedAt,
                SimilarityScore = r.SimilarityScore,
                VectorDistance = r.VectorDistance,
                Metadata = new Dictionary<string, object>
                {
                    ["has_embedding"] = r.EmbeddingVector != null,
                    ["embedding_size"] = r.EmbeddingVector?.Length ?? 0,
                    ["analysis_method"] = "vector_similarity"
                }
            }).ToList();

            _logger.LogInformation("Model component analysis completed. Analyzed {ComponentCount} components", components.Count);

            return new ModelAnalysisResponse
            {
                RequestId = request.RequestId,
                ModelId = request.ModelId,
                Components = components,
                TotalComponents = components.Count,
                AnalysisMetadata = new Dictionary<string, object>
                {
                    ["seed_embedding_dimensions"] = request.SeedEmbedding.Length,
                    ["max_components_requested"] = request.MaxComponents,
                    ["components_found"] = components.Count,
                    ["analysis_timestamp"] = DateTime.UtcNow.ToString("O"),
                    ["vector_engine"] = "sql_server_2025"
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle model component analysis for user {UserId}", userId);
            throw;
        }
    }

    /// <summary>
    /// Handle batch embedding storage operations
    /// Demonstrates SqlVector<float> parameter binding for insertions
    /// </summary>
    public async Task<EmbeddingStorageResponse> HandleBatchEmbeddingStorageAsync(
        BatchEmbeddingStorageRequest request,
        string userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Processing batch embedding storage for user {UserId} with {EmbeddingCount} embeddings",
                userId, request.Embeddings.Count);

            // Convert request embeddings to insertion format
            var embeddingInserts = request.Embeddings.Select(e => new ComponentEmbeddingInsert
            {
                ComponentId = e.ComponentId,
                ModelId = e.ModelId,
                ComponentType = e.ComponentType,
                Description = e.Description,
                EmbeddingVector = e.EmbeddingVector
            }).ToList();

            // Execute batch insertion using SqlVector<float> parameters
            var insertedCount = await _vectorQueryService.BatchInsertComponentEmbeddingsAsync(embeddingInserts, userId);

            _logger.LogInformation("Batch embedding storage completed. Inserted {InsertedCount} embeddings", insertedCount);

            return new EmbeddingStorageResponse
            {
                RequestId = request.RequestId,
                InsertedCount = insertedCount,
                TotalRequested = request.Embeddings.Count,
                Success = insertedCount > 0,
                StorageMetadata = new Dictionary<string, object>
                {
                    ["embedding_dimensions"] = request.Embeddings.FirstOrDefault()?.EmbeddingVector.Length ?? 0,
                    ["total_embeddings_requested"] = request.Embeddings.Count,
                    ["embeddings_inserted"] = insertedCount,
                    ["storage_method"] = "sql_server_vector_batch",
                    ["storage_timestamp"] = DateTime.UtcNow.ToString("O")
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to handle batch embedding storage for user {UserId}", userId);
            throw;
        }
    }

    public void Dispose()
    {
        _vectorQueryService?.Dispose();
        _logger.LogDebug("VectorSearchHandler disposed");
    }
}

#region Request/Response DTOs

/// <summary>
/// Vector search request from MCP agents
/// </summary>
public class VectorSearchRequest
{
    public Guid RequestId { get; set; }
    public float[] QueryEmbedding { get; set; } = Array.Empty<float>();
    public double SimilarityThreshold { get; set; } = 0.7;
    public int MaxResults { get; set; } = 10;
    public string? ComponentType { get; set; }
}

/// <summary>
/// Vector search response to MCP agents
/// </summary>
public class VectorSearchResponse
{
    public Guid RequestId { get; set; }
    public List<VectorSearchResultDto> Results { get; set; } = new();
    public int TotalResults { get; set; }
    public Dictionary<string, object> SearchMetadata { get; set; } = new();
}

/// <summary>
/// Agent capability search request
/// </summary>
public class AgentCapabilitySearchRequest
{
    public Guid RequestId { get; set; }
    public string[]? RequiredCapabilities { get; set; }
    public Core.Enums.AgentStatus? RequiredStatus { get; set; }
}

/// <summary>
/// Agent capability search response
/// </summary>
public class AgentSearchResponse
{
    public Guid RequestId { get; set; }
    public List<AgentSearchResultDto> Results { get; set; } = new();
    public int TotalResults { get; set; }
    public Dictionary<string, object> SearchMetadata { get; set; } = new();
}

/// <summary>
/// Model analysis request
/// </summary>
public class ModelAnalysisRequest
{
    public Guid RequestId { get; set; }
    public Guid ModelId { get; set; }
    public float[] SeedEmbedding { get; set; } = Array.Empty<float>();
    public int MaxComponents { get; set; } = 50;
}

/// <summary>
/// Model analysis response
/// </summary>
public class ModelAnalysisResponse
{
    public Guid RequestId { get; set; }
    public Guid ModelId { get; set; }
    public List<ModelComponentAnalysisDto> Components { get; set; } = new();
    public int TotalComponents { get; set; }
    public Dictionary<string, object> AnalysisMetadata { get; set; } = new();
}

/// <summary>
/// Batch embedding storage request
/// </summary>
public class BatchEmbeddingStorageRequest
{
    public Guid RequestId { get; set; }
    public List<EmbeddingStorageDto> Embeddings { get; set; } = new();
}

/// <summary>
/// Batch embedding storage response
/// </summary>
public class EmbeddingStorageResponse
{
    public Guid RequestId { get; set; }
    public int InsertedCount { get; set; }
    public int TotalRequested { get; set; }
    public bool Success { get; set; }
    public Dictionary<string, object> StorageMetadata { get; set; } = new();
}

#endregion