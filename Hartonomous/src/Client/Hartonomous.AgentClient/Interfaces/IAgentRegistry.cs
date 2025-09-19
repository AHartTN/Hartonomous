using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for agent registry providing agent discovery, routing, and load balancing
/// </summary>
public interface IAgentRegistry
{
    /// <summary>
    /// Registers an agent with the registry
    /// </summary>
    /// <param name="agent">Agent definition to register</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Registered agent definition</returns>
    Task<AgentDefinition> RegisterAgentAsync(AgentDefinition agent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters an agent from the registry
    /// </summary>
    /// <param name="agentId">Agent ID to unregister</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnregisterAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds agents capable of handling a specific task type
    /// </summary>
    /// <param name="taskType">Task type to find agents for</param>
    /// <param name="requiredCapabilities">Optional specific capabilities required</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of suitable agents</returns>
    Task<IEnumerable<AgentDefinition>> FindAgentsForTaskAsync(
        string taskType,
        IEnumerable<string>? requiredCapabilities = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Finds agents by capability
    /// </summary>
    /// <param name="capabilityId">Capability ID to search for</param>
    /// <param name="healthStatus">Optional health status filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of agents providing the capability</returns>
    Task<IEnumerable<AgentDefinition>> FindAgentsByCapabilityAsync(
        string capabilityId,
        HealthStatus? healthStatus = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific agent by ID
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent definition or null if not found</returns>
    Task<AgentDefinition?> GetAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all registered agents
    /// </summary>
    /// <param name="healthStatus">Optional health status filter</param>
    /// <param name="agentType">Optional agent type filter</param>
    /// <param name="tags">Optional tags filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of registered agents</returns>
    Task<IEnumerable<AgentDefinition>> ListAgentsAsync(
        HealthStatus? healthStatus = null,
        string? agentType = null,
        IEnumerable<string>? tags = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets available instances for a specific agent
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="status">Optional status filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of available agent instances</returns>
    Task<IEnumerable<AgentInstance>> GetAvailableInstancesAsync(
        string agentId,
        AgentStatus? status = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates agent health status
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="healthStatus">New health status</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateAgentHealthAsync(string agentId, HealthStatus healthStatus, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets agent load metrics for load balancing decisions
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent load metrics</returns>
    Task<AgentLoadMetrics?> GetAgentLoadMetricsAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets agent performance metrics
    /// </summary>
    /// <param name="agentId">Agent ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent performance metrics</returns>
    Task<AgentPerformanceMetrics?> GetAgentPerformanceMetricsAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches agents using full-text search
    /// </summary>
    /// <param name="searchTerm">Search term</param>
    /// <param name="limit">Maximum results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matching agents with relevance scores</returns>
    Task<IEnumerable<AgentSearchResult>> SearchAgentsAsync(
        string searchTerm,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when an agent is registered
    /// </summary>
    event EventHandler<AgentRegisteredEventArgs> AgentRegistered;

    /// <summary>
    /// Event fired when an agent is unregistered
    /// </summary>
    event EventHandler<AgentUnregisteredEventArgs> AgentUnregistered;

    /// <summary>
    /// Event fired when agent health changes
    /// </summary>
    event EventHandler<AgentHealthChangedEventArgs> AgentHealthChanged;
}

/// <summary>
/// Agent load metrics for load balancing
/// </summary>
public sealed record AgentLoadMetrics
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Current CPU usage percentage (0-100)
    /// </summary>
    public double CpuUsagePercent { get; init; }

    /// <summary>
    /// Current memory usage percentage (0-100)
    /// </summary>
    public double MemoryUsagePercent { get; init; }

    /// <summary>
    /// Number of active tasks
    /// </summary>
    public int ActiveTasks { get; init; }

    /// <summary>
    /// Number of queued tasks
    /// </summary>
    public int QueuedTasks { get; init; }

    /// <summary>
    /// Average task execution time in milliseconds
    /// </summary>
    public double AverageExecutionTimeMs { get; init; }

    /// <summary>
    /// Current load score (0-100, higher means more loaded)
    /// </summary>
    public double LoadScore { get; init; }

    /// <summary>
    /// Whether the agent is currently available
    /// </summary>
    public bool Available { get; init; }

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Agent performance metrics
/// </summary>
public sealed record AgentPerformanceMetrics
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Total tasks executed
    /// </summary>
    public long TotalTasksExecuted { get; init; }

    /// <summary>
    /// Successful task count
    /// </summary>
    public long SuccessfulTasks { get; init; }

    /// <summary>
    /// Failed task count
    /// </summary>
    public long FailedTasks { get; init; }

    /// <summary>
    /// Average task execution time in milliseconds
    /// </summary>
    public double AverageExecutionTimeMs { get; init; }

    /// <summary>
    /// Success rate percentage (0-100)
    /// </summary>
    public double SuccessRate { get; init; }

    /// <summary>
    /// Reliability score (0-100)
    /// </summary>
    public double ReliabilityScore { get; init; }

    /// <summary>
    /// Performance score (0-100)
    /// </summary>
    public double PerformanceScore { get; init; }

    /// <summary>
    /// Last task execution timestamp
    /// </summary>
    public DateTimeOffset? LastTaskExecuted { get; init; }

    /// <summary>
    /// Performance metrics collection period
    /// </summary>
    public TimeSpan MetricsPeriod { get; init; }

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Agent search result with relevance score
/// </summary>
public sealed record AgentSearchResult
{
    /// <summary>
    /// Agent definition
    /// </summary>
    public required AgentDefinition Agent { get; init; }

    /// <summary>
    /// Relevance score (0-100)
    /// </summary>
    public double RelevanceScore { get; init; }

    /// <summary>
    /// Search highlights
    /// </summary>
    public IReadOnlyList<string> Highlights { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Event arguments for agent registered event
/// </summary>
public class AgentRegisteredEventArgs : EventArgs
{
    /// <summary>
    /// Registered agent
    /// </summary>
    public required AgentDefinition Agent { get; init; }

    /// <summary>
    /// Registration timestamp
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for agent unregistered event
/// </summary>
public class AgentUnregisteredEventArgs : EventArgs
{
    /// <summary>
    /// Agent ID that was unregistered
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Unregistration timestamp
    /// </summary>
    public DateTimeOffset UnregisteredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for agent health changed event
/// </summary>
public class AgentHealthChangedEventArgs : EventArgs
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// New health status
    /// </summary>
    public HealthStatus NewHealthStatus { get; init; }

    /// <summary>
    /// Previous health status
    /// </summary>
    public HealthStatus PreviousHealthStatus { get; init; }

    /// <summary>
    /// Health change timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}