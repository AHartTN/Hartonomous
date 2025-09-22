/*
 * Copyright (c) 2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the EF Core Database.SqlQuery<T> implementation with SqlVector<float> operations
 * for SQL Server 2025 VECTOR data type integration in Hartonomous Infrastructure.
 */

using Microsoft.Data.SqlClient;
using Microsoft.Data.SqlTypes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;
using Hartonomous.Core.Data;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.Infrastructure.SqlServer;

/// <summary>
/// EF Core-based SQL Server VECTOR query service implementing Database.SqlQuery<T> with SqlVector<float> operations
/// Replaces legacy Dapper connection.Query patterns with modern EF Core approach
/// </summary>
public class SqlVectorQueryService : IDisposable
{
    private readonly HartonomousDbContext _dbContext;
    private readonly ILogger<SqlVectorQueryService> _logger;

    public SqlVectorQueryService(HartonomousDbContext dbContext, ILogger<SqlVectorQueryService> logger)
    {
        _dbContext = dbContext ?? throw new ArgumentNullException(nameof(dbContext));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Execute vector similarity search using EF Core Database.SqlQuery<T> with SqlVector<float> parameter binding
    /// Demonstrates the pattern: var param = new SqlParameter("@embedding", SqlDbType.VarBinary) { Value = new SqlVector<float>(embeddingData).ToSqlBytes() };
    /// </summary>
    public async Task<IEnumerable<VectorSearchResult>> ExecuteVectorSimilaritySearchAsync(
        float[] queryEmbedding,
        string userId,
        double threshold = 0.7,
        int maxResults = 10,
        string? componentType = null)
    {
        try
        {
            _logger.LogDebug("Executing vector similarity search with EF Core SqlQuery for user {UserId}", userId);

            // Create SqlVector<float> parameter with proper binding
            var embeddingParam = new SqlParameter("@embedding", SqlDbType.VarBinary)
            {
                Value = new SqlVector<float>(queryEmbedding).ToSqlBytes()
            };

            var userIdParam = new SqlParameter("@userId", SqlDbType.NVarChar) { Value = userId };
            var thresholdParam = new SqlParameter("@threshold", SqlDbType.Float) { Value = threshold };
            var maxResultsParam = new SqlParameter("@maxResults", SqlDbType.Int) { Value = maxResults };

            var sql = @"
                SELECT TOP (@maxResults)
                    ce.ComponentId,
                    ce.ModelId,
                    ce.ComponentType,
                    ce.Description,
                    VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @embedding) AS Distance,
                    (1 - VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @embedding)) AS SimilarityScore
                FROM dbo.ComponentEmbeddings ce
                WHERE ce.UserId = @userId
                    AND (1 - VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @embedding)) >= @threshold";

            // Add component type filter if specified
            if (!string.IsNullOrEmpty(componentType))
            {
                sql += " AND ce.ComponentType = @componentType";
                var componentTypeParam = new SqlParameter("@componentType", SqlDbType.NVarChar) { Value = componentType };

                var parameters = new[] { embeddingParam, userIdParam, thresholdParam, maxResultsParam, componentTypeParam };
                sql += " ORDER BY VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @embedding) ASC";

                return await _dbContext.Database.SqlQuery<VectorSearchResult>(
                    FormattableStringFactory.Create(sql, parameters)).ToListAsync();
            }
            else
            {
                var parameters = new[] { embeddingParam, userIdParam, thresholdParam, maxResultsParam };
                sql += " ORDER BY VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @embedding) ASC";

                return await _dbContext.Database.SqlQuery<VectorSearchResult>(
                    FormattableStringFactory.Create(sql, parameters)).ToListAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute vector similarity search");
            throw;
        }
    }

    /// <summary>
    /// Execute agent lookup using EF Core Database.SqlQuery<T> pattern
    /// Modernizes AgentRepository Dapper usage
    /// </summary>
    public async Task<IEnumerable<AgentQueryResult>> QueryAgentsByCapabilitiesAsync(
        string[] requiredCapabilities,
        string userId,
        AgentStatus? status = null)
    {
        try
        {
            _logger.LogDebug("Querying agents by capabilities using EF Core SqlQuery for user {UserId}", userId);

            var capabilitiesJson = JsonSerializer.Serialize(requiredCapabilities);
            var userIdParam = new SqlParameter("@userId", SqlDbType.NVarChar) { Value = userId };
            var capabilitiesParam = new SqlParameter("@capabilities", SqlDbType.NVarChar) { Value = capabilitiesJson };

            var sql = @"
                SELECT
                    a.AgentId,
                    a.AgentName,
                    a.AgentType,
                    a.ConnectionId,
                    a.Capabilities,
                    a.Description,
                    a.Status,
                    a.RegisteredAt,
                    a.LastHeartbeat
                FROM dbo.Agents a
                WHERE a.UserId = @userId";

            if (status.HasValue)
            {
                sql += " AND a.Status = @status";
                var statusParam = new SqlParameter("@status", SqlDbType.Int) { Value = (int)status.Value };

                var parameters = new[] { userIdParam, capabilitiesParam, statusParam };
                sql += " ORDER BY a.LastHeartbeat DESC";

                return await _dbContext.Database.SqlQuery<AgentQueryResult>(
                    FormattableStringFactory.Create(sql, parameters)).ToListAsync();
            }
            else
            {
                var parameters = new[] { userIdParam, capabilitiesParam };
                sql += " ORDER BY a.LastHeartbeat DESC";

                return await _dbContext.Database.SqlQuery<AgentQueryResult>(
                    FormattableStringFactory.Create(sql, parameters)).ToListAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query agents by capabilities");
            throw;
        }
    }

    /// <summary>
    /// Execute message conversation queries using EF Core Database.SqlQuery<T>
    /// Modernizes MessageRepository Dapper usage
    /// </summary>
    public async Task<IEnumerable<MessageQueryResult>> QueryConversationMessagesAsync(
        Guid fromAgentId,
        Guid toAgentId,
        string userId,
        int limit = 100)
    {
        try
        {
            _logger.LogDebug("Querying conversation messages using EF Core SqlQuery for user {UserId}", userId);

            var fromAgentParam = new SqlParameter("@fromAgentId", SqlDbType.UniqueIdentifier) { Value = fromAgentId };
            var toAgentParam = new SqlParameter("@toAgentId", SqlDbType.UniqueIdentifier) { Value = toAgentId };
            var userIdParam = new SqlParameter("@userId", SqlDbType.NVarChar) { Value = userId };
            var limitParam = new SqlParameter("@limit", SqlDbType.Int) { Value = limit };

            var sql = @"
                SELECT TOP (@limit)
                    m.MessageId,
                    m.FromAgentId,
                    m.ToAgentId,
                    m.Payload,
                    m.MessageType,
                    m.Metadata,
                    m.Timestamp,
                    m.ProcessedAt
                FROM dbo.McpMessages m
                WHERE m.UserId = @userId
                    AND ((m.FromAgentId = @fromAgentId AND m.ToAgentId = @toAgentId)
                         OR (m.FromAgentId = @toAgentId AND m.ToAgentId = @fromAgentId))
                ORDER BY m.Timestamp DESC";

            var parameters = new[] { limitParam, userIdParam, fromAgentParam, toAgentParam };

            return await _dbContext.Database.SqlQuery<MessageQueryResult>(
                FormattableStringFactory.Create(sql, parameters)).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query conversation messages");
            throw;
        }
    }

    /// <summary>
    /// Execute complex model component analysis using vector embeddings
    /// Combines vector search with relational data using EF Core SqlQuery<T>
    /// </summary>
    public async Task<IEnumerable<ModelComponentAnalysisResult>> ExecuteModelComponentAnalysisAsync(
        Guid modelId,
        float[] seedEmbedding,
        string userId,
        int maxComponents = 50)
    {
        try
        {
            _logger.LogDebug("Executing model component analysis with vector embeddings for model {ModelId}", modelId);

            // Create SqlVector<float> parameter for embedding similarity
            var embeddingParam = new SqlParameter("@seedEmbedding", SqlDbType.VarBinary)
            {
                Value = new SqlVector<float>(seedEmbedding).ToSqlBytes()
            };

            var modelIdParam = new SqlParameter("@modelId", SqlDbType.UniqueIdentifier) { Value = modelId };
            var userIdParam = new SqlParameter("@userId", SqlDbType.NVarChar) { Value = userId };
            var maxComponentsParam = new SqlParameter("@maxComponents", SqlDbType.Int) { Value = maxComponents };

            var sql = @"
                SELECT TOP (@maxComponents)
                    mc.ComponentId,
                    mc.ComponentName,
                    mc.ComponentType,
                    mc.Description,
                    mc.CreatedAt,
                    ce.EmbeddingVector,
                    VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @seedEmbedding) AS VectorDistance,
                    (1 - VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @seedEmbedding)) AS SimilarityScore
                FROM dbo.ModelComponents mc
                INNER JOIN dbo.ComponentEmbeddings ce ON mc.ComponentId = ce.ComponentId
                INNER JOIN dbo.Models m ON mc.ModelId = m.ModelId
                WHERE m.ModelId = @modelId
                    AND m.UserId = @userId
                    AND ce.UserId = @userId
                ORDER BY VECTOR_DISTANCE('cosine', ce.EmbeddingVector, @seedEmbedding) ASC";

            var parameters = new[] { maxComponentsParam, embeddingParam, modelIdParam, userIdParam };

            return await _dbContext.Database.SqlQuery<ModelComponentAnalysisResult>(
                FormattableStringFactory.Create(sql, parameters)).ToListAsync();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute model component analysis");
            throw;
        }
    }

    /// <summary>
    /// Batch insert component embeddings using SqlVector<float> parameter binding
    /// Demonstrates modern EF Core approach for vector data insertion
    /// </summary>
    public async Task<int> BatchInsertComponentEmbeddingsAsync(
        IEnumerable<ComponentEmbeddingInsert> embeddings,
        string userId)
    {
        try
        {
            _logger.LogDebug("Batch inserting component embeddings using EF Core for user {UserId}", userId);

            var insertedCount = 0;

            foreach (var embedding in embeddings)
            {
                // Create SqlVector<float> parameter for each embedding
                var embeddingParam = new SqlParameter("@embeddingVector", SqlDbType.VarBinary)
                {
                    Value = new SqlVector<float>(embedding.EmbeddingVector).ToSqlBytes()
                };

                var componentIdParam = new SqlParameter("@componentId", SqlDbType.UniqueIdentifier) { Value = embedding.ComponentId };
                var modelIdParam = new SqlParameter("@modelId", SqlDbType.UniqueIdentifier) { Value = embedding.ModelId };
                var userIdParam = new SqlParameter("@userId", SqlDbType.NVarChar) { Value = userId };
                var componentTypeParam = new SqlParameter("@componentType", SqlDbType.NVarChar) { Value = embedding.ComponentType };
                var descriptionParam = new SqlParameter("@description", SqlDbType.NVarChar) { Value = embedding.Description ?? (object)DBNull.Value };

                var sql = @"
                    MERGE dbo.ComponentEmbeddings AS target
                    USING (SELECT @componentId AS ComponentId) AS source
                    ON target.ComponentId = source.ComponentId AND target.UserId = @userId
                    WHEN MATCHED THEN
                        UPDATE SET
                            EmbeddingVector = @embeddingVector,
                            ComponentType = @componentType,
                            Description = @description,
                            CreatedAt = GETUTCDATE()
                    WHEN NOT MATCHED THEN
                        INSERT (ComponentId, ModelId, UserId, ComponentType, Description, EmbeddingVector)
                        VALUES (@componentId, @modelId, @userId, @componentType, @description, @embeddingVector);";

                var parameters = new[] { componentIdParam, embeddingParam, modelIdParam, userIdParam, componentTypeParam, descriptionParam };

                var rowsAffected = await _dbContext.Database.ExecuteSqlAsync(
                    FormattableStringFactory.Create(sql, parameters));

                insertedCount += rowsAffected;
            }

            return insertedCount;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to batch insert component embeddings");
            throw;
        }
    }

    public void Dispose()
    {
        // DbContext disposal is handled by DI container
        _logger.LogDebug("SqlVectorQueryService disposed");
    }
}

/// <summary>
/// Result model for vector similarity search operations
/// </summary>
public class VectorSearchResult
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public double Distance { get; set; }
    public double SimilarityScore { get; set; }
}

/// <summary>
/// Result model for agent queries
/// </summary>
public class AgentQueryResult
{
    public Guid AgentId { get; set; }
    public string AgentName { get; set; } = string.Empty;
    public string AgentType { get; set; } = string.Empty;
    public string? ConnectionId { get; set; }
    public string? Capabilities { get; set; }
    public string? Description { get; set; }
    public int Status { get; set; }
    public DateTime RegisteredAt { get; set; }
    public DateTime? LastHeartbeat { get; set; }
}

/// <summary>
/// Result model for message queries
/// </summary>
public class MessageQueryResult
{
    public Guid MessageId { get; set; }
    public Guid FromAgentId { get; set; }
    public Guid? ToAgentId { get; set; }
    public string? Payload { get; set; }
    public int MessageType { get; set; }
    public string? Metadata { get; set; }
    public DateTime Timestamp { get; set; }
    public DateTime? ProcessedAt { get; set; }
}

/// <summary>
/// Result model for model component analysis
/// </summary>
public class ModelComponentAnalysisResult
{
    public Guid ComponentId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public byte[]? EmbeddingVector { get; set; }
    public double VectorDistance { get; set; }
    public double SimilarityScore { get; set; }
}

/// <summary>
/// Input model for component embedding insertion
/// </summary>
public class ComponentEmbeddingInsert
{
    public Guid ComponentId { get; set; }
    public Guid ModelId { get; set; }
    public string ComponentType { get; set; } = string.Empty;
    public string? Description { get; set; }
    public float[] EmbeddingVector { get; set; } = Array.Empty<float>();
}

/// <summary>
/// Agent status enumeration for queries
/// </summary>
public enum AgentStatus
{
    Offline = 0,
    Online = 1,
    Busy = 2,
    Error = 3
}