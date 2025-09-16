using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for capability registry and discovery system
/// </summary>
public interface ICapabilityRegistry
{
    /// <summary>
    /// Registers a capability with the registry
    /// </summary>
    /// <param name="capability">Capability to register</param>
    /// <param name="agentId">Agent providing this capability</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="endpoint">Optional endpoint URL</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Registry entry for the capability</returns>
    Task<CapabilityRegistryEntry> RegisterCapabilityAsync(
        AgentCapability capability,
        string agentId,
        string? instanceId = null,
        string? endpoint = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters a capability from the registry
    /// </summary>
    /// <param name="capabilityId">Capability ID to unregister</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnregisterCapabilityAsync(
        string capabilityId,
        string agentId,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates capability registration information
    /// </summary>
    /// <param name="capabilityId">Capability ID</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="updates">Updates to apply</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated registry entry</returns>
    Task<CapabilityRegistryEntry> UpdateCapabilityAsync(
        string capabilityId,
        string agentId,
        Dictionary<string, object> updates,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Discovers capabilities by criteria
    /// </summary>
    /// <param name="category">Optional capability category filter</param>
    /// <param name="tags">Optional tags to match</param>
    /// <param name="requiredPermissions">Optional required permissions</param>
    /// <param name="healthStatus">Optional health status filter</param>
    /// <param name="available">Optional availability filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matching capability registry entries</returns>
    Task<IEnumerable<CapabilityRegistryEntry>> DiscoverCapabilitiesAsync(
        string? category = null,
        IEnumerable<string>? tags = null,
        IEnumerable<string>? requiredPermissions = null,
        HealthStatus? healthStatus = null,
        bool? available = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific capability by ID
    /// </summary>
    /// <param name="capabilityId">Capability ID</param>
    /// <param name="agentId">Optional agent ID filter</param>
    /// <param name="instanceId">Optional instance ID filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Capability registry entry or null if not found</returns>
    Task<CapabilityRegistryEntry?> GetCapabilityAsync(
        string capabilityId,
        string? agentId = null,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists all registered capabilities
    /// </summary>
    /// <param name="agentId">Optional agent ID filter</param>
    /// <param name="instanceId">Optional instance ID filter</param>
    /// <param name="userId">Optional user ID filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of capability registry entries</returns>
    Task<IEnumerable<CapabilityRegistryEntry>> ListCapabilitiesAsync(
        string? agentId = null,
        string? instanceId = null,
        string? userId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a capability
    /// </summary>
    /// <param name="request">Capability execution request</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Capability execution response</returns>
    Task<CapabilityExecutionResponse> ExecuteCapabilityAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the health of a capability
    /// </summary>
    /// <param name="capabilityId">Capability ID</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Health status</returns>
    Task<HealthStatus> CheckCapabilityHealthAsync(
        string capabilityId,
        string agentId,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates capability health status
    /// </summary>
    /// <param name="capabilityId">Capability ID</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="healthStatus">New health status</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateCapabilityHealthAsync(
        string capabilityId,
        string agentId,
        HealthStatus healthStatus,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets capability usage statistics
    /// </summary>
    /// <param name="capabilityId">Capability ID</param>
    /// <param name="agentId">Optional agent ID filter</param>
    /// <param name="instanceId">Optional instance ID filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Usage statistics</returns>
    Task<CapabilityUsage?> GetCapabilityUsageAsync(
        string capabilityId,
        string? agentId = null,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates capability usage statistics
    /// </summary>
    /// <param name="capabilityId">Capability ID</param>
    /// <param name="agentId">Agent ID</param>
    /// <param name="executionResult">Result of capability execution</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateCapabilityUsageAsync(
        string capabilityId,
        string agentId,
        CapabilityExecutionResponse executionResult,
        string? instanceId = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets capability categories available in the registry
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of categories</returns>
    Task<IEnumerable<string>> GetCategoriesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all tags used by capabilities in the registry
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tags</returns>
    Task<IEnumerable<string>> GetTagsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches capabilities using full-text search
    /// </summary>
    /// <param name="searchTerm">Search term</param>
    /// <param name="limit">Maximum results to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Matching capability registry entries with relevance scores</returns>
    Task<IEnumerable<CapabilitySearchResult>> SearchCapabilitiesAsync(
        string searchTerm,
        int limit = 50,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets recommended capabilities based on usage patterns
    /// </summary>
    /// <param name="userId">User ID for personalization</param>
    /// <param name="context">Optional context for recommendations</param>
    /// <param name="limit">Maximum recommendations to return</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Recommended capability registry entries</returns>
    Task<IEnumerable<CapabilityRegistryEntry>> GetRecommendedCapabilitiesAsync(
        string userId,
        Dictionary<string, object>? context = null,
        int limit = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when a capability is registered
    /// </summary>
    event EventHandler<CapabilityRegisteredEventArgs> CapabilityRegistered;

    /// <summary>
    /// Event fired when a capability is unregistered
    /// </summary>
    event EventHandler<CapabilityUnregisteredEventArgs> CapabilityUnregistered;

    /// <summary>
    /// Event fired when a capability health changes
    /// </summary>
    event EventHandler<CapabilityHealthChangedEventArgs> CapabilityHealthChanged;

    /// <summary>
    /// Event fired when a capability is executed
    /// </summary>
    event EventHandler<CapabilityExecutedEventArgs> CapabilityExecuted;
}

/// <summary>
/// Capability search result with relevance score
/// </summary>
public sealed record CapabilitySearchResult
{
    /// <summary>
    /// Capability registry entry
    /// </summary>
    public required CapabilityRegistryEntry Entry { get; init; }

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
/// Event arguments for capability registered event
/// </summary>
public class CapabilityRegisteredEventArgs : EventArgs
{
    /// <summary>
    /// Registered capability entry
    /// </summary>
    public required CapabilityRegistryEntry Entry { get; init; }

    /// <summary>
    /// Registration timestamp
    /// </summary>
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for capability unregistered event
/// </summary>
public class CapabilityUnregisteredEventArgs : EventArgs
{
    /// <summary>
    /// Capability ID that was unregistered
    /// </summary>
    public required string CapabilityId { get; init; }

    /// <summary>
    /// Agent ID that provided the capability
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Instance ID if specific to an instance
    /// </summary>
    public string? InstanceId { get; init; }

    /// <summary>
    /// Unregistration timestamp
    /// </summary>
    public DateTimeOffset UnregisteredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for capability health changed event
/// </summary>
public class CapabilityHealthChangedEventArgs : EventArgs
{
    /// <summary>
    /// Capability ID
    /// </summary>
    public required string CapabilityId { get; init; }

    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Instance ID if specific to an instance
    /// </summary>
    public string? InstanceId { get; init; }

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

/// <summary>
/// Event arguments for capability executed event
/// </summary>
public class CapabilityExecutedEventArgs : EventArgs
{
    /// <summary>
    /// Execution request
    /// </summary>
    public required CapabilityExecutionRequest Request { get; init; }

    /// <summary>
    /// Execution response
    /// </summary>
    public required CapabilityExecutionResponse Response { get; init; }

    /// <summary>
    /// Execution timestamp
    /// </summary>
    public DateTimeOffset ExecutedAt { get; init; } = DateTimeOffset.UtcNow;
}