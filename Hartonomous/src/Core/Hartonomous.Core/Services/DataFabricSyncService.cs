/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Data Fabric Synchronization Service - a critical component
 * of the unified data fabric architecture. The algorithms for maintaining consistency
 * between SQL Server NinaDB and Neo4j knowledge graph represent proprietary
 * intellectual property and trade secrets.
 *
 * Key Innovations Protected:
 * - Bidirectional sync algorithms between SQL Server and Neo4j
 * - Real-time graph relationship discovery from mechanistic interpretability
 * - Orphaned data cleanup patterns across heterogeneous data stores
 * - Component causal relationship mapping algorithms
 *
 * Any attempt to reverse engineer, extract, or replicate these synchronization
 * algorithms is prohibited by law and subject to legal action.
 */

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Hartonomous.Core.Data;
using Hartonomous.Core.Models;
using Hartonomous.Core.Interfaces;

namespace Hartonomous.Core.Services;

/// <summary>
/// Data fabric synchronization service
/// Ensures consistency between SQL Server entities and Neo4j knowledge graph
/// Implements the unified data fabric architecture pattern
/// </summary>
public class DataFabricSyncService : BackgroundService
{
    private readonly HartonomousDbContext _context;
    private readonly IKnowledgeGraphRepository _knowledgeGraphRepository;
    private readonly IModelComponentRepository _componentRepository;
    private readonly ILogger<DataFabricSyncService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(5);

    public DataFabricSyncService(
        HartonomousDbContext context,
        IKnowledgeGraphRepository knowledgeGraphRepository,
        IModelComponentRepository componentRepository,
        ILogger<DataFabricSyncService> logger)
    {
        _context = context;
        _knowledgeGraphRepository = knowledgeGraphRepository;
        _componentRepository = componentRepository;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Data fabric sync service started");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SyncModelComponentsAsync(stoppingToken);
                await SyncComponentRelationshipsAsync(stoppingToken);
                await CleanupOrphanedDataAsync(stoppingToken);

                _logger.LogDebug("Data fabric sync cycle completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during data fabric synchronization");
            }

            await Task.Delay(_syncInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Synchronizes model components from SQL Server to Neo4j
    /// Ensures all components have corresponding graph nodes
    /// </summary>
    private async Task SyncModelComponentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Find components that haven't been synced to the knowledge graph
            var unsyncedComponents = await _context.ModelComponents
                .Where(mc => !mc.IsSyncedToGraph)
                .Take(100) // Process in batches
                .ToListAsync(cancellationToken);

            foreach (var component in unsyncedComponents)
            {
                try
                {
                    // Create the component node in Neo4j
                    var modelComponent = new ModelComponent
                    {
                        ComponentId = component.ComponentId,
                        ModelId = component.ModelId,
                        ComponentName = component.ComponentName,
                        ComponentType = component.ComponentType
                    };

                    await _knowledgeGraphRepository.CreateModelComponentNodeAsync(
                        modelComponent,
                        component.UserId,
                        cancellationToken);

                    // Mark as synced
                    component.IsSyncedToGraph = true;
                    component.GraphSyncTimestamp = DateTime.UtcNow;

                    _logger.LogDebug("Synced component {ComponentId} to knowledge graph", component.ComponentId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync component {ComponentId} to knowledge graph", component.ComponentId);
                    // Mark sync failure for retry logic
                    component.GraphSyncFailures = (component.GraphSyncFailures ?? 0) + 1;
                }
            }

            if (unsyncedComponents.Any())
            {
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Synced {Count} model components to knowledge graph", unsyncedComponents.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing model components to knowledge graph");
        }
    }

    /// <summary>
    /// Synchronizes component relationships discovered through mechanistic interpretability
    /// Creates graph edges for computational relationships
    /// </summary>
    private async Task SyncComponentRelationshipsAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Find capability mappings that represent causal relationships
            var causalMappings = await _context.CapabilityMappings
                .Where(cm => cm.Category == "causal_mechanism" && !cm.IsSyncedToGraph)
                .Include(cm => cm.Model)
                .Take(50) // Process in batches
                .ToListAsync(cancellationToken);

            foreach (var mapping in causalMappings)
            {
                try
                {
                    // Parse analysis results to find target component
                    var analysisData = mapping.GetAnalysisResults();
                    if (analysisData != null && analysisData.ContainsKey("TargetComponentId"))
                    {
                        var targetComponentId = Guid.Parse(analysisData["TargetComponentId"].ToString()!);

                        await _knowledgeGraphRepository.CreateComponentRelationshipAsync(
                            mapping.ComponentId,
                            targetComponentId,
                            "CAUSALLY_INFLUENCES",
                            mapping.UserId,
                            cancellationToken);

                        mapping.IsSyncedToGraph = true;
                        mapping.GraphSyncTimestamp = DateTime.UtcNow;

                        _logger.LogDebug("Synced causal relationship {Source} -> {Target} to knowledge graph",
                            mapping.ComponentId, targetComponentId);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to sync causal mapping {MappingId} to knowledge graph", mapping.CapabilityMappingId);
                }
            }

            if (causalMappings.Any())
            {
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Synced {Count} component relationships to knowledge graph", causalMappings.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error syncing component relationships to knowledge graph");
        }
    }

    /// <summary>
    /// Removes orphaned data in both SQL Server and Neo4j
    /// Maintains consistency across the unified data fabric
    /// </summary>
    private async Task CleanupOrphanedDataAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Find deleted components that need cleanup in Neo4j
            var deletedComponents = await _context.ModelComponents
                .Where(mc => mc.IsDeleted && mc.IsSyncedToGraph)
                .Take(20) // Process in small batches for cleanup
                .ToListAsync(cancellationToken);

            foreach (var component in deletedComponents)
            {
                try
                {
                    await _knowledgeGraphRepository.DeleteComponentAsync(
                        component.ModelComponentId,
                        component.UserId,
                        cancellationToken);

                    component.IsSyncedToGraph = false;
                    component.GraphSyncTimestamp = null;

                    _logger.LogDebug("Cleaned up deleted component {ComponentId} from knowledge graph", component.ComponentId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to cleanup deleted component {ComponentId} from knowledge graph", component.ComponentId);
                }
            }

            if (deletedComponents.Any())
            {
                await _context.SaveChangesAsync(cancellationToken);
                _logger.LogInformation("Cleaned up {Count} deleted components from knowledge graph", deletedComponents.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during data fabric cleanup");
        }
    }

    /// <summary>
    /// Handles manual sync request for specific components
    /// Used when immediate synchronization is required
    /// </summary>
    public async Task SyncComponentAsync(Guid componentId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Manual sync requested for component {ComponentId}", componentId);

            var component = await _componentRepository.GetByIdAsync(componentId, userId);
            if (component != null)
            {
                await _knowledgeGraphRepository.CreateModelComponentNodeAsync(component, userId, cancellationToken);

                component.IsSyncedToGraph = true;
                component.GraphSyncTimestamp = DateTime.UtcNow;

                await _context.SaveChangesAsync(cancellationToken);

                _logger.LogInformation("Component {ComponentId} manually synced to knowledge graph", componentId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during manual component sync for {ComponentId}", componentId);
            throw;
        }
    }

    /// <summary>
    /// Force full resync of all components for a specific user
    /// Use with caution - expensive operation
    /// </summary>
    public async Task ForceResyncUserDataAsync(string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogWarning("Force resync initiated for user {UserId}", userId);

            // Clear sync flags to force resync
            var components = await _context.ModelComponents
                .Where(mc => mc.UserId == userId && !mc.IsDeleted)
                .ToListAsync(cancellationToken);

            foreach (var component in components)
            {
                component.IsSyncedToGraph = false;
                component.GraphSyncTimestamp = null;
                component.GraphSyncFailures = 0;
            }

            var mappings = await _context.CapabilityMappings
                .Where(cm => cm.UserId == userId)
                .ToListAsync(cancellationToken);

            foreach (var mapping in mappings)
            {
                mapping.IsSyncedToGraph = false;
                mapping.GraphSyncTimestamp = null;
            }

            await _context.SaveChangesAsync(cancellationToken);

            _logger.LogWarning("Force resync completed for user {UserId} - {ComponentCount} components and {MappingCount} mappings marked for resync",
                userId, components.Count, mappings.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during force resync for user {UserId}", userId);
            throw;
        }
    }
}