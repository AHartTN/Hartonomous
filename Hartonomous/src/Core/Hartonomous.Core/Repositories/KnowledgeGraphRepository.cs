/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the knowledge graph repository for AI model component relationships.
 * Features Neo4j integration, computational circuit discovery, and mechanistic interpretability algorithms.
 */

using Microsoft.Extensions.Logging;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Models;
using Hartonomous.Core.Data;
using Hartonomous.Infrastructure.Neo4j;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Core.Repositories;

/// <summary>
/// Knowledge graph repository implementation using Neo4j service
/// Provides high-level graph operations for model component relationships
/// </summary>
public class KnowledgeGraphRepository : IKnowledgeGraphRepository
{
    private readonly Neo4jService _neo4jService;
    private readonly ILogger<KnowledgeGraphRepository> _logger;
    private readonly HartonomousDbContext _context;

    public KnowledgeGraphRepository(Neo4jService neo4jService, ILogger<KnowledgeGraphRepository> logger, HartonomousDbContext context)
    {
        _neo4jService = neo4jService ?? throw new ArgumentNullException(nameof(neo4jService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    /// <summary>
    /// Create or update a model component node in the knowledge graph
    /// Maps Core domain models to Neo4j operations
    /// </summary>
    public async Task CreateModelComponentNodeAsync(ModelComponent component, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating knowledge graph node for component {ComponentId}", component.ComponentId);

            await _neo4jService.CreateModelComponentAsync(
                component.ComponentId,
                component.ModelId,
                component.ComponentName ?? "Unknown",
                component.ComponentType ?? "Unknown",
                userId);

            _logger.LogDebug("Successfully created knowledge graph node for component {ComponentId}", component.ComponentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create knowledge graph node for component {ComponentId}", component.ComponentId);
            throw;
        }
    }

    /// <summary>
    /// Create relationship between model components
    /// Establishes graph edges for computational relationships
    /// </summary>
    public async Task CreateComponentRelationshipAsync(Guid fromComponentId, Guid toComponentId, string relationshipType, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Creating relationship: {From} -> {To} ({Type})", fromComponentId, toComponentId, relationshipType);

            await _neo4jService.CreateComponentRelationshipAsync(fromComponentId, toComponentId, relationshipType, userId);

            _logger.LogDebug("Successfully created relationship: {From} -> {To} ({Type})", fromComponentId, toComponentId, relationshipType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create relationship: {From} -> {To} ({Type})", fromComponentId, toComponentId, relationshipType);
            throw;
        }
    }

    /// <summary>
    /// Query component paths for circuit discovery
    /// Converts Neo4j path results to Core domain models
    /// </summary>
    public async Task<IEnumerable<ComponentPath>> GetComponentPathsAsync(Guid startComponentId, int maxDepth, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Querying component paths from {ComponentId} with max depth {MaxDepth}", startComponentId, maxDepth);

            var neo4jPaths = await _neo4jService.GetModelPathsAsync(startComponentId, maxDepth, userId);

            var componentPaths = neo4jPaths.Select(path => new ComponentPath
            {
                Components = path.Components.Select(info => new ModelComponent
                {
                    ComponentId = info.Id,
                    ComponentName = info.Name,
                    ComponentType = info.Type
                }).ToList(),
                RelationshipTypes = path.RelationshipTypes,
                PathLength = path.Components.Count,
                TotalStrength = 1.0 // Would be calculated from relationship strengths
            });

            _logger.LogDebug("Found {Count} component paths from {ComponentId}", componentPaths.Count(), startComponentId);
            return componentPaths;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to query component paths from {ComponentId}", startComponentId);
            throw;
        }
    }

    /// <summary>
    /// Find components similar in structure to the given component
    /// Uses graph topology analysis for similarity
    /// </summary>
    public async Task<IEnumerable<ModelComponent>> FindSimilarComponentsAsync(Guid componentId, string userId, int limit = 10, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Finding similar components to {ComponentId} (limit: {Limit})", componentId, limit);

            var similarComponents = await _neo4jService.FindSimilarComponentsAsync(componentId, userId, limit);

            var modelComponents = similarComponents.Select(info => new ModelComponent
            {
                ComponentId = info.Id,
                ComponentName = info.Name,
                ComponentType = info.Type
            });

            _logger.LogDebug("Found {Count} similar components to {ComponentId}", modelComponents.Count(), componentId);
            return modelComponents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find similar components to {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Find components relevant to a specific domain or capability
    /// Implements domain-aware component discovery for agent distillation
    /// </summary>
    public async Task<IEnumerable<ModelComponent>> FindDomainRelevantComponentsAsync(string domain, string capability, double minImportance, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Finding domain-relevant components for {Domain}/{Capability} (min importance: {MinImportance})", domain, capability, minImportance);

            // Use Neo4j to find domain-relevant components by traversing relationships
            // and analyzing component metadata for domain and capability matches
            var relevantComponents = await _neo4jService.FindDomainRelevantComponentsAsync(domain, capability, minImportance, userId);

            // Convert Neo4j results to Core domain models
            var modelComponents = relevantComponents.Select(info => new ModelComponent
            {
                ComponentId = info.Id,
                ComponentName = info.Name,
                ComponentType = info.Type,
                RelevanceScore = info.RelevanceScore,
                UserId = userId
            });

            _logger.LogDebug("Found {Count} domain-relevant components using Neo4j graph analysis", modelComponents.Count());

            _logger.LogDebug("Found {Count} domain-relevant components for {Domain}/{Capability}", modelComponents.Count(), domain, capability);
            return modelComponents;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to find domain-relevant components for {Domain}/{Capability}", domain, capability);
            throw;
        }
    }

    /// <summary>
    /// Analyze component importance for specific tasks using graph centrality
    /// Leverages Neo4j's graph algorithms for importance scoring
    /// </summary>
    public async Task<IEnumerable<ComponentImportance>> AnalyzeComponentImportanceAsync(string taskDescription, int topK, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Analyzing component importance for task '{TaskDescription}' (top {TopK})", taskDescription, topK);

            // Use Neo4j graph algorithms for centrality-based importance analysis
            var importanceResults = await _neo4jService.AnalyzeComponentImportanceAsync(taskDescription, topK, userId);

            // Convert Neo4j results to Core domain models
            var componentImportances = importanceResults.Select(result => new ComponentImportance
            {
                ComponentId = result.ComponentId,
                ComponentName = result.ComponentName,
                Description = result.Description,
                ImportanceScore = result.ImportanceScore,
                ActivationLevel = result.ActivationLevel
            });

            _logger.LogDebug("Completed Neo4j-based component importance analysis for task: {Count} results", componentImportances.Count());

            return componentImportances;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to analyze component importance for task '{TaskDescription}'", taskDescription);
            throw;
        }
    }

    /// <summary>
    /// Discover computational circuits - connected subgraphs of components
    /// Core functionality for mechanistic interpretability and agent distillation
    /// </summary>
    public async Task<IEnumerable<ComputationalCircuit>> DiscoverCircuitsAsync(string domain, double minStrength, int maxDepth, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Discovering circuits for domain '{Domain}' (min strength: {MinStrength}, max depth: {MaxDepth})", domain, minStrength, maxDepth);

            // Use Neo4j for sophisticated circuit discovery using graph traversal algorithms
            var discoveredCircuits = await _neo4jService.DiscoverCircuitsAsync(domain, minStrength, maxDepth, userId);

            // Convert Neo4j results to Core domain models
            var circuits = discoveredCircuits.Select(circuit => new ComputationalCircuit
            {
                CircuitId = circuit.CircuitId,
                StartComponentId = circuit.StartComponentId,
                EndComponentId = circuit.EndComponentId,
                ComponentIds = circuit.ComponentIds,
                CircuitStrength = circuit.CircuitStrength,
                PathLength = circuit.PathLength,
                Domain = circuit.Domain,
                DiscoveredAt = circuit.DiscoveredAt
            });

            _logger.LogDebug("Completed Neo4j-based circuit discovery for domain: {Count} circuits found", circuits.Count());

            return circuits;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover circuits for domain '{Domain}'", domain);
            throw;
        }
    }

    /// <summary>
    /// Delete component and all relationships from knowledge graph
    /// Maintains graph consistency when components are removed
    /// </summary>
    public async Task DeleteComponentAsync(Guid componentId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Deleting component {ComponentId} from knowledge graph", componentId);

            await _neo4jService.DeleteComponentAsync(componentId, userId);

            _logger.LogDebug("Successfully deleted component {ComponentId} from knowledge graph", componentId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete component {ComponentId} from knowledge graph", componentId);
            throw;
        }
    }

    /// <summary>
    /// Clear all circuit data for a project
    /// Cleanup operation for project resets or deletions
    /// </summary>
    public async Task ClearProjectCircuitsAsync(int projectId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Clearing circuit data for project {ProjectId}", projectId);

            // Use Neo4j to clear all circuit data for the specified project
            await _neo4jService.ClearProjectCircuitsAsync(projectId, userId);

            _logger.LogDebug("Successfully cleared Neo4j circuit data for project {ProjectId}", projectId);

            _logger.LogDebug("Cleared circuit data for project {ProjectId}", projectId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear circuit data for project {ProjectId}", projectId);
            throw;
        }
    }

    /// <summary>
    /// Get circuit patterns for analysis and visualization
    /// Retrieves recurring structural patterns from the knowledge graph
    /// </summary>
    public async Task<IEnumerable<CircuitPattern>> GetCircuitPatternsAsync(string userId, int limit = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Getting circuit patterns for user {UserId} (limit: {Limit})", userId, limit);

            // Use Neo4j to analyze and identify circuit patterns
            var neo4jPatterns = await _neo4jService.GetCircuitPatternsAsync(userId, limit);

            // Convert Neo4j results to Core domain models
            var circuitPatterns = neo4jPatterns.Select(pattern => new CircuitPattern
            {
                PatternId = pattern.PatternId,
                PatternType = pattern.PatternType,
                ComponentIds = pattern.ComponentIds,
                ComponentTypes = pattern.ComponentTypes,
                RelationshipTypes = pattern.RelationshipTypes,
                PatternStrength = pattern.PatternStrength,
                Frequency = pattern.Frequency,
                Domain = pattern.Domain
            });

            _logger.LogDebug("Retrieved {Count} circuit patterns for user {UserId}", circuitPatterns.Count(), userId);
            return circuitPatterns;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get circuit patterns for user {UserId}", userId);
            throw;
        }
    }
}