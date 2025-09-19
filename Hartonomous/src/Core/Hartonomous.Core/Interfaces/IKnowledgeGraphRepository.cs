/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the knowledge graph repository interface for AI model component relationship management.
 * Features Neo4j integration, computational circuit discovery, graph traversal algorithms, and mechanistic interpretability.
 */

using Hartonomous.Core.Models;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Repository interface for knowledge graph operations
/// Bridges SQL Server entities with Neo4j graph relationships
/// </summary>
public interface IKnowledgeGraphRepository
{
    /// <summary>
    /// Create or update a model component node in the knowledge graph
    /// </summary>
    Task CreateModelComponentNodeAsync(ModelComponent component, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create relationship between model components
    /// </summary>
    Task CreateComponentRelationshipAsync(Guid fromComponentId, Guid toComponentId, string relationshipType, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Query component paths for circuit discovery
    /// </summary>
    Task<IEnumerable<ComponentPath>> GetComponentPathsAsync(Guid startComponentId, int maxDepth, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find components similar in structure to the given component
    /// </summary>
    Task<IEnumerable<ModelComponent>> FindSimilarComponentsAsync(Guid componentId, string userId, int limit = 10, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find components relevant to a specific domain or capability
    /// Used during agent distillation
    /// </summary>
    Task<IEnumerable<ModelComponent>> FindDomainRelevantComponentsAsync(string domain, string capability, double minImportance, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Analyze component importance for specific tasks using graph centrality
    /// </summary>
    Task<IEnumerable<ComponentImportance>> AnalyzeComponentImportanceAsync(string taskDescription, int topK, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Discover computational circuits - connected subgraphs of components
    /// </summary>
    Task<IEnumerable<ComputationalCircuit>> DiscoverCircuitsAsync(string domain, double minStrength, int maxDepth, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete component and all relationships
    /// </summary>
    Task DeleteComponentAsync(Guid componentId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Clear all circuit data for a project
    /// </summary>
    Task ClearProjectCircuitsAsync(int projectId, string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get circuit patterns for analysis and visualization
    /// </summary>
    Task<IEnumerable<CircuitPattern>> GetCircuitPatternsAsync(string userId, int limit = 50, CancellationToken cancellationToken = default);
}

/// <summary>
/// Represents a path through component relationships
/// </summary>
public class ComponentPath
{
    public List<ModelComponent> Components { get; set; } = new();
    public List<string> RelationshipTypes { get; set; } = new();
    public double TotalStrength { get; set; }
    public int PathLength { get; set; }
}

/// <summary>
/// Component importance analysis result
/// </summary>
public class ComponentImportance
{
    public Guid ComponentId { get; set; }
    public string ComponentName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public double ImportanceScore { get; set; }
    public double ActivationLevel { get; set; }
}

/// <summary>
/// Discovered computational circuit
/// </summary>
public class ComputationalCircuit
{
    public Guid CircuitId { get; set; } = Guid.NewGuid();
    public Guid StartComponentId { get; set; }
    public Guid EndComponentId { get; set; }
    public List<Guid> ComponentIds { get; set; } = new();
    public double CircuitStrength { get; set; }
    public int PathLength { get; set; }
    public string Domain { get; set; } = string.Empty;
    public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Circuit pattern for analysis and visualization
/// </summary>
public class CircuitPattern
{
    public Guid PatternId { get; set; } = Guid.NewGuid();
    public string PatternType { get; set; } = string.Empty;
    public List<Guid> ComponentIds { get; set; } = new();
    public List<string> ComponentTypes { get; set; } = new();
    public List<string> RelationshipTypes { get; set; } = new();
    public double PatternStrength { get; set; }
    public int Frequency { get; set; }
    public string Domain { get; set; } = string.Empty;
}