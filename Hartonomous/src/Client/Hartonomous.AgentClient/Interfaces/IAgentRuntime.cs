using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for agent runtime environment providing process isolation and resource management
/// </summary>
public interface IAgentRuntime
{
    /// <summary>
    /// Creates a new agent instance from the given definition
    /// </summary>
    /// <param name="definition">Agent definition</param>
    /// <param name="configuration">Instance-specific configuration</param>
    /// <param name="userId">User ID creating the instance</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created agent instance</returns>
    Task<AgentInstance> CreateInstanceAsync(
        AgentDefinition definition,
        Dictionary<string, object>? configuration = null,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated agent instance</returns>
    Task<AgentInstance> StartInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stops an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="graceful">Whether to stop gracefully</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated agent instance</returns>
    Task<AgentInstance> StopInstanceAsync(string instanceId, bool graceful = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated agent instance</returns>
    Task<AgentInstance> PauseInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated agent instance</returns>
    Task<AgentInstance> ResumeInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Restarts an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated agent instance</returns>
    Task<AgentInstance> RestartInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Destroys an agent instance and cleans up resources
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="force">Whether to force destroy if stopping fails</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DestroyInstanceAsync(string instanceId, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an agent instance by ID
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent instance or null if not found</returns>
    Task<AgentInstance?> GetInstanceAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all agent instances for the current user
    /// </summary>
    /// <param name="userId">User ID to filter by (null for current user)</param>
    /// <param name="status">Optional status filter</param>
    /// <param name="agentId">Optional agent ID filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of agent instances</returns>
    Task<IEnumerable<AgentInstance>> ListInstancesAsync(
        string? userId = null,
        AgentStatus? status = null,
        string? agentId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an agent instance configuration
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="configuration">New configuration values</param>
    /// <param name="restart">Whether to restart the instance to apply changes</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated agent instance</returns>
    Task<AgentInstance> UpdateInstanceConfigurationAsync(
        string instanceId,
        Dictionary<string, object> configuration,
        bool restart = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets real-time resource usage for an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current resource usage</returns>
    Task<AgentResourceUsage?> GetInstanceResourceUsageAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets execution metrics for an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Current metrics</returns>
    Task<AgentMetrics?> GetInstanceMetricsAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs a health check on an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status</returns>
    Task<HealthStatus> CheckInstanceHealthAsync(string instanceId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets logs from an agent instance
    /// </summary>
    /// <param name="instanceId">Instance identifier</param>
    /// <param name="since">Get logs since this timestamp</param>
    /// <param name="tail">Maximum number of recent log entries</param>
    /// <param name="follow">Whether to follow/stream logs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Log entries</returns>
    Task<IEnumerable<LogEntry>> GetInstanceLogsAsync(
        string instanceId,
        DateTimeOffset? since = null,
        int? tail = null,
        bool follow = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when an instance status changes
    /// </summary>
    event EventHandler<AgentInstanceEventArgs> InstanceStatusChanged;

    /// <summary>
    /// Event fired when instance metrics are updated
    /// </summary>
    event EventHandler<AgentInstanceEventArgs> InstanceMetricsUpdated;

    /// <summary>
    /// Event fired when an instance encounters an error
    /// </summary>
    event EventHandler<AgentInstanceErrorEventArgs> InstanceError;
}

/// <summary>
/// Event arguments for agent instance events
/// </summary>
public class AgentInstanceEventArgs : EventArgs
{
    /// <summary>
    /// Agent instance that triggered the event
    /// </summary>
    public required AgentInstance Instance { get; init; }

    /// <summary>
    /// Previous status (for status change events)
    /// </summary>
    public AgentStatus? PreviousStatus { get; init; }

    /// <summary>
    /// Event timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for agent instance error events
/// </summary>
public class AgentInstanceErrorEventArgs : AgentInstanceEventArgs
{
    /// <summary>
    /// Error that occurred
    /// </summary>
    public required AgentError Error { get; init; }
}