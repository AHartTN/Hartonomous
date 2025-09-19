/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the distilled agent repository interface for specialized AI agent management.
 * Features agent distillation, deployment tracking, capability discovery, and domain-specific agent operations.
 */

using Hartonomous.Core.Models;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Specialized repository interface for DistilledAgent entities
/// Supports agent discovery, deployment tracking, and capability management
/// </summary>
public interface IDistilledAgentRepository : IRepository<DistilledAgent>
{
    // Agent discovery and search
    Task<IEnumerable<DistilledAgent>> GetAgentsByDomainAsync(string domain, string userId);
    Task<IEnumerable<DistilledAgent>> GetAgentsByStatusAsync(AgentStatus status, string userId);
    Task<IEnumerable<DistilledAgent>> SearchAgentsByCapabilityAsync(string capability, string userId);
    Task<DistilledAgent?> GetAgentByNameAsync(string agentName, string userId);

    // Agent lifecycle management
    Task<IEnumerable<DistilledAgent>> GetDeployedAgentsAsync(string userId);
    Task<IEnumerable<DistilledAgent>> GetAgentsReadyForDeploymentAsync(string userId);
    Task UpdateAgentStatusAsync(Guid agentId, AgentStatus status, string userId);
    Task RecordAgentAccessAsync(Guid agentId, string userId);

    // Performance and analytics
    Task<Dictionary<string, int>> GetDomainStatsAsync(string userId);
    Task<Dictionary<AgentStatus, int>> GetStatusStatsAsync(string userId);
    Task<IEnumerable<DistilledAgent>> GetTopPerformingAgentsAsync(int count, string userId);

    // Component relationships
    Task<IEnumerable<ModelComponent>> GetAgentComponentsAsync(Guid agentId, string userId);
    Task<IEnumerable<AgentCapability>> GetAgentCapabilitiesAsync(Guid agentId, string userId);
    Task<IEnumerable<DistilledAgent>> GetAgentsBySourceModelAsync(Guid modelId, string userId);

    // Deployment operations
    Task UpdateDeploymentConfigAsync(Guid agentId, string deploymentConfig, string userId);
    Task RecordPerformanceMetricAsync(Guid agentId, string metricName, double value, string userId);
}