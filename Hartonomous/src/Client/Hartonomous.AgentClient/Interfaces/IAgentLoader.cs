using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for dynamic agent loading and capability registration
/// </summary>
public interface IAgentLoader
{
    /// <summary>
    /// Loads an agent from the specified path
    /// </summary>
    /// <param name="agentPath">Path to agent package or assembly</param>
    /// <param name="validate">Whether to validate the agent before loading</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Loaded agent definition</returns>
    Task<AgentDefinition> LoadAgentAsync(string agentPath, bool validate = true, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unloads an agent and its resources
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="force">Whether to force unload even if instances are running</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnloadAgentAsync(string agentId, bool force = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Reloads an agent with new version
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="agentPath">Path to new agent version</param>
    /// <param name="hotSwap">Whether to perform hot swap without stopping instances</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated agent definition</returns>
    Task<AgentDefinition> ReloadAgentAsync(string agentId, string agentPath, bool hotSwap = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Validates an agent package or assembly
    /// </summary>
    /// <param name="agentPath">Path to agent package or assembly</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Validation result</returns>
    Task<AgentValidationResult> ValidateAgentAsync(string agentPath, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all loaded agents
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of loaded agent definitions</returns>
    Task<IEnumerable<AgentDefinition>> GetLoadedAgentsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a specific loaded agent by ID
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent definition or null if not found</returns>
    Task<AgentDefinition?> GetLoadedAgentAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the assembly for a loaded agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent assembly or null if not found</returns>
    Task<Assembly?> GetAgentAssemblyAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an agent is currently loaded
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <returns>True if the agent is loaded</returns>
    bool IsAgentLoaded(string agentId);

    /// <summary>
    /// Gets agent capabilities from a loaded agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of capabilities provided by the agent</returns>
    Task<IEnumerable<AgentCapability>> GetAgentCapabilitiesAsync(string agentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Registers capabilities from a loaded agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of registered capability registry entries</returns>
    Task<IEnumerable<CapabilityRegistryEntry>> RegisterAgentCapabilitiesAsync(string agentId, string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Unregisters capabilities for an agent
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <param name="instanceId">Optional specific instance ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UnregisterAgentCapabilitiesAsync(string agentId, string? instanceId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets load context information for debugging
    /// </summary>
    /// <param name="agentId">Agent identifier</param>
    /// <returns>Load context information</returns>
    AgentLoadContext? GetLoadContext(string agentId);

    /// <summary>
    /// Event fired when an agent is loaded
    /// </summary>
    event EventHandler<AgentLoadedEventArgs> AgentLoaded;

    /// <summary>
    /// Event fired when an agent is unloaded
    /// </summary>
    event EventHandler<AgentUnloadedEventArgs> AgentUnloaded;

    /// <summary>
    /// Event fired when an agent load fails
    /// </summary>
    event EventHandler<AgentLoadFailedEventArgs> AgentLoadFailed;
}

/// <summary>
/// Agent validation result
/// </summary>
public sealed record AgentValidationResult
{
    /// <summary>
    /// Whether the agent is valid
    /// </summary>
    public bool IsValid { get; init; }

    /// <summary>
    /// Validation errors
    /// </summary>
    public IReadOnlyList<string> Errors { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Validation warnings
    /// </summary>
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Security issues found
    /// </summary>
    public IReadOnlyList<string> SecurityIssues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Performance concerns
    /// </summary>
    public IReadOnlyList<string> PerformanceConcerns { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Compatibility issues
    /// </summary>
    public IReadOnlyList<string> CompatibilityIssues { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Validation timestamp
    /// </summary>
    public DateTimeOffset ValidatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Validator version used
    /// </summary>
    public string? ValidatorVersion { get; init; }
}

/// <summary>
/// Agent load context information
/// </summary>
public sealed record AgentLoadContext
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Load path
    /// </summary>
    public required string LoadPath { get; init; }

    /// <summary>
    /// Assembly load context name
    /// </summary>
    public string? LoadContextName { get; init; }

    /// <summary>
    /// Loaded assemblies
    /// </summary>
    public IReadOnlyList<string> LoadedAssemblies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Dependencies resolved
    /// </summary>
    public IReadOnlyList<string> ResolvedDependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Load timestamp
    /// </summary>
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Memory usage in bytes
    /// </summary>
    public long MemoryUsage { get; init; }

    /// <summary>
    /// Whether the context is collectible
    /// </summary>
    public bool IsCollectible { get; init; }

    /// <summary>
    /// Load errors encountered
    /// </summary>
    public IReadOnlyList<string> LoadErrors { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Event arguments for agent loaded event
/// </summary>
public class AgentLoadedEventArgs : EventArgs
{
    /// <summary>
    /// Loaded agent definition
    /// </summary>
    public required AgentDefinition Agent { get; init; }

    /// <summary>
    /// Load context information
    /// </summary>
    public required AgentLoadContext LoadContext { get; init; }

    /// <summary>
    /// Load timestamp
    /// </summary>
    public DateTimeOffset LoadedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for agent unloaded event
/// </summary>
public class AgentUnloadedEventArgs : EventArgs
{
    /// <summary>
    /// Unloaded agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Whether the unload was forced
    /// </summary>
    public bool WasForced { get; init; }

    /// <summary>
    /// Unload timestamp
    /// </summary>
    public DateTimeOffset UnloadedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for agent load failed event
/// </summary>
public class AgentLoadFailedEventArgs : EventArgs
{
    /// <summary>
    /// Agent path that failed to load
    /// </summary>
    public required string AgentPath { get; init; }

    /// <summary>
    /// Error that occurred during loading
    /// </summary>
    public required Exception Error { get; init; }

    /// <summary>
    /// Validation result if available
    /// </summary>
    public AgentValidationResult? ValidationResult { get; init; }

    /// <summary>
    /// Failure timestamp
    /// </summary>
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
}