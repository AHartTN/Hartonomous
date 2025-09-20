using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hartonomous.Infrastructure.Neo4j.Interfaces;

/// <summary>
/// Interface for graph database operations
/// Provides abstraction over graph storage implementations (Neo4j, etc.)
/// </summary>
public interface IGraphService
{
    /// <summary>
    /// Create model component node in knowledge graph
    /// </summary>
    Task CreateModelComponentAsync(Guid componentId, Guid modelId, string componentName, string componentType, string userId);

    /// <summary>
    /// Create relationship between model components
    /// </summary>
    Task CreateComponentRelationshipAsync(Guid fromComponentId, Guid toComponentId, string relationshipType, string userId);

    /// <summary>
    /// Query model structure using graph traversal
    /// </summary>
    Task<IEnumerable<ModelComponentPath>> GetModelPathsAsync(Guid startComponentId, int maxDepth, string userId);

    /// <summary>
    /// Find similar components by type and structure
    /// </summary>
    Task<IEnumerable<ModelComponentInfo>> FindSimilarComponentsAsync(Guid componentId, string userId, int limit = 10);

    /// <summary>
    /// Remove component and all its relationships
    /// </summary>
    Task DeleteComponentAsync(Guid componentId, string userId);

    /// <summary>
    /// Find domain-relevant components using graph analysis
    /// </summary>
    Task<IEnumerable<DomainRelevantComponentInfo>> FindDomainRelevantComponentsAsync(string domain, string capability, double minImportance, string userId);

    /// <summary>
    /// Analyze component importance using graph centrality algorithms
    /// </summary>
    Task<IEnumerable<ComponentImportanceInfo>> AnalyzeComponentImportanceAsync(string taskDescription, int topK, string userId);

    /// <summary>
    /// Discover computational circuits using advanced graph traversal
    /// </summary>
    Task<IEnumerable<CircuitInfo>> DiscoverCircuitsAsync(string domain, double minStrength, int maxDepth, string userId);

    /// <summary>
    /// Clear all circuit data for a specific project
    /// </summary>
    Task ClearProjectCircuitsAsync(int projectId, string userId);

    /// <summary>
    /// Get circuit patterns for analysis and visualization
    /// </summary>
    Task<IEnumerable<CircuitPatternInfo>> GetCircuitPatternsAsync(string userId, int limit = 50);
}