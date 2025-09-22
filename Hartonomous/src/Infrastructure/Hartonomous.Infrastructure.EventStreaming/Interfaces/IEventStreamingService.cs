using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Infrastructure.SqlServer;

namespace Hartonomous.Infrastructure.EventStreaming.Interfaces;

/// <summary>
/// Interface for event streaming and data fabric orchestration operations
/// Provides abstraction over data fabric coordination and event processing
/// </summary>
public interface IEventStreamingService
{
    /// <summary>
    /// Initialize the entire data fabric
    /// </summary>
    Task InitializeAsync();

    /// <summary>
    /// Get comprehensive model insights by combining graph and vector search
    /// </summary>
    Task<ModelInsights> GetModelInsightsAsync(Guid modelId, string userId);

    /// <summary>
    /// Perform semantic search across the entire model knowledge base
    /// </summary>
    Task<SemanticSearchResult> PerformSemanticSearchAsync(float[] queryEmbedding, string userId,
        string? componentType = null, int topK = 20);

    /// <summary>
    /// Health check for all data fabric components
    /// </summary>
    Task<DataFabricHealth> CheckHealthAsync();
}

/// <summary>
/// Interface for CDC (Change Data Capture) event processing
/// Handles synchronization between SQL Server and other data stores
/// </summary>
public interface ICdcEventProcessor
{
    /// <summary>
    /// Process model component creation events
    /// </summary>
    Task ProcessComponentCreatedAsync(Guid componentId, Guid modelId, string componentName, string componentType, string userId);

    /// <summary>
    /// Process model component update events
    /// </summary>
    Task ProcessComponentUpdatedAsync(Guid componentId, string componentName, string componentType, string userId);

    /// <summary>
    /// Process model component deletion events
    /// </summary>
    Task ProcessComponentDeletedAsync(Guid componentId, string userId);

    /// <summary>
    /// Process component relationship creation events
    /// </summary>
    Task ProcessRelationshipCreatedAsync(Guid fromComponentId, Guid toComponentId, string relationshipType, string userId);

    /// <summary>
    /// Process embedding generation events
    /// </summary>
    Task ProcessEmbeddingGeneratedAsync(Guid componentId, Guid modelId, string userId, float[] embedding, string componentType, string description);

    /// <summary>
    /// Start CDC event processing
    /// </summary>
    Task StartAsync();

    /// <summary>
    /// Stop CDC event processing
    /// </summary>
    Task StopAsync();
}