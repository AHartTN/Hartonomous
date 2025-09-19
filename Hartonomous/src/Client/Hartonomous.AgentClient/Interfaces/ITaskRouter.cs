using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for intelligent task routing and agent selection
/// </summary>
public interface ITaskRouter
{
    /// <summary>
    /// Selects the optimal agent for executing a task
    /// </summary>
    /// <param name="suitableAgents">List of agents capable of handling the task</param>
    /// <param name="task">Task to be executed</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Selected agent instance or null if none available</returns>
    Task<AgentInstance?> SelectOptimalAgentAsync(
        IEnumerable<AgentDefinition> suitableAgents,
        AgentTask task,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Routes a task to the best available agent
    /// </summary>
    /// <param name="task">Task to route</param>
    /// <param name="routingStrategy">Routing strategy to use</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task routing result</returns>
    Task<TaskRoutingResult> RouteTaskAsync(
        AgentTask task,
        TaskRoutingStrategy routingStrategy = TaskRoutingStrategy.Balanced,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates agent suitability for a task
    /// </summary>
    /// <param name="agent">Agent to evaluate</param>
    /// <param name="task">Task to evaluate for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Agent suitability score (0-100, higher is better)</returns>
    Task<double> EvaluateAgentSuitabilityAsync(
        AgentDefinition agent,
        AgentTask task,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the current load distribution across agents
    /// </summary>
    /// <param name="agentIds">Optional specific agent IDs to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Load distribution information</returns>
    Task<IEnumerable<AgentLoadInfo>> GetLoadDistributionAsync(
        IEnumerable<string>? agentIds = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Predicts task execution time for an agent
    /// </summary>
    /// <param name="agent">Agent to predict for</param>
    /// <param name="task">Task to predict execution time for</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Predicted execution time in milliseconds</returns>
    Task<long> PredictExecutionTimeAsync(
        AgentDefinition agent,
        AgentTask task,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an agent can handle a task type
    /// </summary>
    /// <param name="agent">Agent to check</param>
    /// <param name="taskType">Task type to check for</param>
    /// <param name="requiredCapabilities">Optional required capabilities</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if the agent can handle the task type</returns>
    Task<bool> CanHandleTaskTypeAsync(
        AgentDefinition agent,
        string taskType,
        IEnumerable<string>? requiredCapabilities = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets routing recommendations for a task
    /// </summary>
    /// <param name="task">Task to get recommendations for</param>
    /// <param name="maxRecommendations">Maximum number of recommendations</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of routing recommendations</returns>
    Task<IEnumerable<TaskRoutingRecommendation>> GetRoutingRecommendationsAsync(
        AgentTask task,
        int maxRecommendations = 5,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records task execution outcome for learning
    /// </summary>
    /// <param name="routingResult">Original routing result</param>
    /// <param name="executionResult">Task execution result</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task RecordExecutionOutcomeAsync(
        TaskRoutingResult routingResult,
        TaskResult executionResult,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when a task is routed
    /// </summary>
    event EventHandler<TaskRoutedEventArgs> TaskRouted;

    /// <summary>
    /// Event fired when routing fails
    /// </summary>
    event EventHandler<TaskRoutingFailedEventArgs> TaskRoutingFailed;
}

/// <summary>
/// Task routing strategy enumeration
/// </summary>
public enum TaskRoutingStrategy
{
    /// <summary>
    /// Balance load across available agents
    /// </summary>
    Balanced,

    /// <summary>
    /// Route to the fastest agent
    /// </summary>
    Performance,

    /// <summary>
    /// Route to the most reliable agent
    /// </summary>
    Reliability,

    /// <summary>
    /// Route to the agent with lowest cost
    /// </summary>
    Cost,

    /// <summary>
    /// Route to the agent with specific capabilities
    /// </summary>
    Capability,

    /// <summary>
    /// Round-robin routing
    /// </summary>
    RoundRobin,

    /// <summary>
    /// Route to agent with least active tasks
    /// </summary>
    LeastConnections
}

/// <summary>
/// Task routing result
/// </summary>
public sealed record TaskRoutingResult
{
    /// <summary>
    /// Task that was routed
    /// </summary>
    public required AgentTask Task { get; init; }

    /// <summary>
    /// Selected agent instance
    /// </summary>
    public AgentInstance? SelectedAgent { get; init; }

    /// <summary>
    /// Routing strategy used
    /// </summary>
    public TaskRoutingStrategy Strategy { get; init; }

    /// <summary>
    /// Whether routing was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Routing error if failed
    /// </summary>
    public AgentError? Error { get; init; }

    /// <summary>
    /// Suitability score of selected agent (0-100)
    /// </summary>
    public double SuitabilityScore { get; init; }

    /// <summary>
    /// Predicted execution time in milliseconds
    /// </summary>
    public long PredictedExecutionTimeMs { get; init; }

    /// <summary>
    /// Routing decision reasons
    /// </summary>
    public IReadOnlyList<string> DecisionReasons { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Alternative agents considered
    /// </summary>
    public IReadOnlyList<AgentDefinition> AlternativeAgents { get; init; } = Array.Empty<AgentDefinition>();

    /// <summary>
    /// Routing timestamp
    /// </summary>
    public DateTimeOffset RoutedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Routing duration in milliseconds
    /// </summary>
    public long RoutingDurationMs { get; init; }
}

/// <summary>
/// Agent load information
/// </summary>
public sealed record AgentLoadInfo
{
    /// <summary>
    /// Agent ID
    /// </summary>
    public required string AgentId { get; init; }

    /// <summary>
    /// Agent name
    /// </summary>
    public required string AgentName { get; init; }

    /// <summary>
    /// Current load score (0-100)
    /// </summary>
    public double LoadScore { get; init; }

    /// <summary>
    /// Number of active tasks
    /// </summary>
    public int ActiveTasks { get; init; }

    /// <summary>
    /// Number of queued tasks
    /// </summary>
    public int QueuedTasks { get; init; }

    /// <summary>
    /// Average response time in milliseconds
    /// </summary>
    public double AverageResponseTimeMs { get; init; }

    /// <summary>
    /// Current health status
    /// </summary>
    public HealthStatus HealthStatus { get; init; }

    /// <summary>
    /// Whether the agent is available
    /// </summary>
    public bool Available { get; init; }

    /// <summary>
    /// Last updated timestamp
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Task routing recommendation
/// </summary>
public sealed record TaskRoutingRecommendation
{
    /// <summary>
    /// Recommended agent
    /// </summary>
    public required AgentDefinition Agent { get; init; }

    /// <summary>
    /// Recommendation score (0-100, higher is better)
    /// </summary>
    public double Score { get; init; }

    /// <summary>
    /// Recommendation reasons
    /// </summary>
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Predicted execution time in milliseconds
    /// </summary>
    public long PredictedExecutionTimeMs { get; init; }

    /// <summary>
    /// Estimated cost (arbitrary units)
    /// </summary>
    public double EstimatedCost { get; init; }

    /// <summary>
    /// Confidence level of the recommendation (0-100)
    /// </summary>
    public double Confidence { get; init; }

    /// <summary>
    /// Potential risks or concerns
    /// </summary>
    public IReadOnlyList<string> Risks { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Event arguments for task routed event
/// </summary>
public class TaskRoutedEventArgs : EventArgs
{
    /// <summary>
    /// Routing result
    /// </summary>
    public required TaskRoutingResult Result { get; init; }

    /// <summary>
    /// Routing timestamp
    /// </summary>
    public DateTimeOffset RoutedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for task routing failed event
/// </summary>
public class TaskRoutingFailedEventArgs : EventArgs
{
    /// <summary>
    /// Task that failed to route
    /// </summary>
    public required AgentTask Task { get; init; }

    /// <summary>
    /// Routing error
    /// </summary>
    public required AgentError Error { get; init; }

    /// <summary>
    /// Routing strategy attempted
    /// </summary>
    public TaskRoutingStrategy Strategy { get; init; }

    /// <summary>
    /// Failure timestamp
    /// </summary>
    public DateTimeOffset FailedAt { get; init; } = DateTimeOffset.UtcNow;
}