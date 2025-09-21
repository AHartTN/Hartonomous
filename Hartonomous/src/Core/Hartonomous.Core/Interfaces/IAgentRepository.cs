/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the agent repository interface for Multi-Context Protocol (MCP) agent management.
 * Features agent registration, discovery, heartbeat monitoring, and user-scoped agent lifecycle operations.
 */

using Hartonomous.Core.DTOs;
using Hartonomous.Core.Entities;
using Hartonomous.Core.Enums;
using Hartonomous.Core.Abstractions;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Repository for managing agents in the MCP system
/// </summary>
public interface IAgentRepository : IRepository<Agent, Guid>
{
    /// <summary>
    /// Register a new agent
    /// </summary>
    Task<Guid> RegisterAgentAsync(AgentRegistrationRequest request, string connectionId, string userId);

    /// <summary>
    /// Get agent by connection ID
    /// </summary>
    Task<AgentDto?> GetAgentByConnectionIdAsync(string connectionId);

    /// <summary>
    /// Update agent status
    /// </summary>
    Task<bool> UpdateAgentStatusAsync(Guid agentId, AgentStatus status, string userId);

    /// <summary>
    /// Discover agents based on criteria
    /// </summary>
    Task<IEnumerable<AgentDto>> DiscoverAgentsAsync(AgentDiscoveryRequest request, string userId);

    /// <summary>
    /// Update agent heartbeat
    /// </summary>
    Task<bool> UpdateAgentHeartbeatAsync(Guid agentId, AgentStatus status, string userId, Dictionary<string, object>? metrics = null);

    /// <summary>
    /// Get agents by user
    /// </summary>
    Task<IEnumerable<AgentDto>> GetAgentsByUserAsync(string userId);

    /// <summary>
    /// Get agent by ID
    /// </summary>
    Task<AgentDto?> GetAgentByIdAsync(Guid agentId, string userId);

    /// <summary>
    /// Unregister agent
    /// </summary>
    Task<bool> UnregisterAgentAsync(Guid agentId, string userId);
}