using Hartonomous.MCP.DTOs;

namespace Hartonomous.MCP.Interfaces;

/// <summary>
/// Repository interface for agent management in MCP system
/// </summary>
public interface IAgentRepository
{
    /// <summary>
    /// Register a new agent for a specific user
    /// </summary>
    Task<Guid> RegisterAgentAsync(AgentRegistrationRequest request, string connectionId, string userId);

    /// <summary>
    /// Update agent connection ID when reconnecting
    /// </summary>
    Task<bool> UpdateAgentConnectionAsync(Guid agentId, string connectionId, string userId);

    /// <summary>
    /// Get agent by ID with user scope validation
    /// </summary>
    Task<AgentDto?> GetAgentByIdAsync(Guid agentId, string userId);

    /// <summary>
    /// Get all agents for a specific user
    /// </summary>
    Task<IEnumerable<AgentDto>> GetAgentsByUserAsync(string userId);

    /// <summary>
    /// Update agent heartbeat and status
    /// </summary>
    Task<bool> UpdateAgentHeartbeatAsync(Guid agentId, AgentStatus status, string userId, Dictionary<string, object>? metrics = null);

    /// <summary>
    /// Discover available agents by criteria for a user
    /// </summary>
    Task<IEnumerable<AgentDto>> DiscoverAgentsAsync(AgentDiscoveryRequest request, string userId);

    /// <summary>
    /// Unregister an agent
    /// </summary>
    Task<bool> UnregisterAgentAsync(Guid agentId, string userId);

    /// <summary>
    /// Update agent status
    /// </summary>
    Task<bool> UpdateAgentStatusAsync(Guid agentId, AgentStatus status, string userId);

    /// <summary>
    /// Get agent by connection ID
    /// </summary>
    Task<AgentDto?> GetAgentByConnectionIdAsync(string connectionId);
}