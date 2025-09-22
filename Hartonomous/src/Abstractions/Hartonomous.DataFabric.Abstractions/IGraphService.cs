namespace Hartonomous.DataFabric.Abstractions;

/// <summary>
/// Graph database abstraction for Neo4j computational circuit operations
/// Optimized for circuit discovery and relationship traversal
/// </summary>
public interface IGraphService
{
    /// <summary>
    /// Create model component node in knowledge graph
    /// Called during model ingestion to establish graph structure
    /// </summary>
    /// <param name="componentId">Unique component identifier</param>
    /// <param name="modelId">Parent model identifier</param>
    /// <param name="componentName">Component name for identification</param>
    /// <param name="componentType">Type of neural component</param>
    /// <param name="properties">Additional component properties</param>
    /// <param name="userId">User identifier for multi-tenant isolation</param>
    Task CreateModelComponentAsync(Guid componentId, Guid modelId, string componentName,
        string componentType, Dictionary<string, object> properties, string userId);

    /// <summary>
    /// Create relationship between model components
    /// Establishes neural circuit connections and dependencies
    /// </summary>
    /// <param name="sourceComponentId">Source component identifier</param>
    /// <param name="targetComponentId">Target component identifier</param>
    /// <param name="relationshipType">Type of relationship (feeds_into, depends_on, etc.)</param>
    /// <param name="weight">Relationship strength or importance</param>
    /// <param name="properties">Additional relationship properties</param>
    /// <param name="userId">User identifier for scoped operations</param>
    Task CreateComponentRelationshipAsync(Guid sourceComponentId, Guid targetComponentId,
        string relationshipType, double weight, Dictionary<string, object> properties, string userId);

    /// <summary>
    /// Discover computational circuits using graph traversal
    /// Implements circuit discovery algorithms for mechanistic interpretability
    /// </summary>
    /// <param name="modelId">Model to analyze for circuits</param>
    /// <param name="startingComponents">Components to begin circuit discovery</param>
    /// <param name="maxDepth">Maximum traversal depth</param>
    /// <param name="minCircuitSize">Minimum number of components in circuit</param>
    /// <param name="userId">User identifier for scoped discovery</param>
    /// <returns>Discovered computational circuits with metadata</returns>
    Task<IEnumerable<ComputationalCircuitDto>> DiscoverCircuitsAsync(Guid modelId,
        IEnumerable<Guid> startingComponents, int maxDepth, int minCircuitSize, string userId);

    /// <summary>
    /// Find activation patterns using graph analysis
    /// Identifies neural pathways and information flow patterns
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="activationThreshold">Minimum activation strength</param>
    /// <param name="patternType">Type of pattern to discover</param>
    /// <param name="userId">User identifier for scoped analysis</param>
    /// <returns>Discovered activation patterns</returns>
    Task<IEnumerable<ActivationPatternDto>> FindActivationPatternsAsync(Guid modelId,
        double activationThreshold, string patternType, string userId);

    /// <summary>
    /// Get component relationships with traversal options
    /// Supports complex graph queries for circuit analysis
    /// </summary>
    /// <param name="componentId">Starting component</param>
    /// <param name="relationshipTypes">Types of relationships to follow</param>
    /// <param name="direction">Traversal direction (incoming, outgoing, both)</param>
    /// <param name="maxDepth">Maximum traversal depth</param>
    /// <param name="userId">User identifier for scoped queries</param>
    /// <returns>Related components with relationship metadata</returns>
    Task<IEnumerable<ComponentRelationshipDto>> GetComponentRelationshipsAsync(Guid componentId,
        IEnumerable<string> relationshipTypes, GraphTraversalDirection direction,
        int maxDepth, string userId);

    /// <summary>
    /// Delete model and all associated graph data
    /// Cleanup operation for model removal
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped deletion</param>
    Task DeleteModelGraphAsync(Guid modelId, string userId);

    /// <summary>
    /// Get graph statistics for monitoring and optimization
    /// </summary>
    /// <param name="modelId">Model identifier</param>
    /// <param name="userId">User identifier for scoped statistics</param>
    /// <returns>Graph statistics and performance metrics</returns>
    Task<GraphStatsDto> GetGraphStatsAsync(Guid modelId, string userId);
}

/// <summary>
/// Computational circuit discovered through graph analysis
/// </summary>
public record ComputationalCircuitDto(
    Guid CircuitId,
    Guid ModelId,
    string CircuitName,
    string FunctionalDescription,
    IEnumerable<Guid> ComponentIds,
    IEnumerable<ComponentRelationshipDto> Relationships,
    double ImportanceScore,
    Dictionary<string, object> Properties);

/// <summary>
/// Activation pattern in neural network
/// </summary>
public record ActivationPatternDto(
    Guid PatternId,
    Guid ModelId,
    string PatternType,
    IEnumerable<Guid> ComponentIds,
    double ActivationStrength,
    string Description);

/// <summary>
/// Component relationship with metadata
/// </summary>
public record ComponentRelationshipDto(
    Guid SourceComponentId,
    Guid TargetComponentId,
    string RelationshipType,
    double Weight,
    Dictionary<string, object> Properties);

/// <summary>
/// Graph traversal direction options
/// </summary>
public enum GraphTraversalDirection
{
    Incoming,
    Outgoing,
    Both
}

/// <summary>
/// Graph statistics for monitoring
/// </summary>
public record GraphStatsDto(
    Guid ModelId,
    int TotalNodes,
    int TotalRelationships,
    Dictionary<string, int> NodeTypeCounts,
    Dictionary<string, int> RelationshipTypeCounts,
    double AverageConnectivity);