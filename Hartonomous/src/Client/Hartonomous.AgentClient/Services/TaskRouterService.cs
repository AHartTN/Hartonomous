using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient.Services;

/// <summary>
/// Service for intelligent task routing and agent selection
/// </summary>
public class TaskRouterService : ITaskRouter, IDisposable
{
    private readonly ILogger<TaskRouterService> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IAgentRegistry _agentRegistry;
    private readonly IAgentRuntime _agentRuntime;
    private readonly ConcurrentDictionary<string, TaskRoutingHistory> _routingHistory = new();
    private readonly ConcurrentDictionary<string, int> _roundRobinCounters = new();
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public TaskRouterService(
        ILogger<TaskRouterService> logger,
        IMetricsCollector metricsCollector,
        IAgentRegistry agentRegistry,
        IAgentRuntime agentRuntime)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));

        // Clean up old routing history periodically
        _cleanupTimer = new Timer(CleanupHistory, null, TimeSpan.FromHours(1), TimeSpan.FromHours(6));
    }

    public event EventHandler<TaskRoutedEventArgs>? TaskRouted;
    public event EventHandler<TaskRoutingFailedEventArgs>? TaskRoutingFailed;

    public async Task<AgentInstance?> SelectOptimalAgentAsync(
        IEnumerable<AgentDefinition> suitableAgents,
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        if (suitableAgents == null) throw new ArgumentNullException(nameof(suitableAgents));
        if (task == null) throw new ArgumentNullException(nameof(task));

        var agents = suitableAgents.ToList();
        if (agents.Count == 0)
            return null;

        var evaluations = new List<(AgentDefinition agent, double score, AgentInstance? instance)>();

        foreach (var agent in agents)
        {
            try
            {
                var score = await EvaluateAgentSuitabilityAsync(agent, task, cancellationToken);
                var instances = await _agentRegistry.GetAvailableInstancesAsync(agent.Id, AgentStatus.Running, cancellationToken);
                var bestInstance = await SelectBestInstanceAsync(instances, task, cancellationToken);

                if (bestInstance != null)
                {
                    evaluations.Add((agent, score, bestInstance));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to evaluate agent {AgentId} for task {TaskId}", agent.Id, task.TaskId);
            }
        }

        if (evaluations.Count == 0)
            return null;

        // Select the agent with the highest score
        var best = evaluations.OrderByDescending(e => e.score).First();
        return best.instance;
    }

    public async Task<TaskRoutingResult> RouteTaskAsync(
        AgentTask task,
        TaskRoutingStrategy routingStrategy = TaskRoutingStrategy.Balanced,
        CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        var stopwatch = Stopwatch.StartNew();
        var decisionReasons = new List<string>();

        try
        {
            // Find suitable agents for the task
            var suitableAgents = await _agentRegistry.FindAgentsForTaskAsync(task.Type, cancellationToken: cancellationToken);
            var agents = suitableAgents.ToList();

            if (agents.Count == 0)
            {
                var error = new AgentError
                {
                    Code = "NO_SUITABLE_AGENTS",
                    Message = $"No agents found capable of handling task type '{task.Type}'",
                    Severity = ErrorSeverity.Warning
                };

                var failureResult = new TaskRoutingResult
                {
                    Task = task,
                    Strategy = routingStrategy,
                    Success = false,
                    Error = error,
                    RoutingDurationMs = stopwatch.ElapsedMilliseconds,
                    DecisionReasons = new[] { "No suitable agents found" }
                };

                OnTaskRoutingFailed(task, error, routingStrategy);
                return failureResult;
            }

            decisionReasons.Add($"Found {agents.Count} suitable agents");

            // Select agent based on strategy
            AgentInstance? selectedAgent = null;
            double suitabilityScore = 0;
            long predictedExecutionTime = 0;

            switch (routingStrategy)
            {
                case TaskRoutingStrategy.Balanced:
                    selectedAgent = await SelectBalancedAgentAsync(agents, task, cancellationToken);
                    decisionReasons.Add("Used balanced load strategy");
                    break;

                case TaskRoutingStrategy.Performance:
                    selectedAgent = await SelectPerformanceAgentAsync(agents, task, cancellationToken);
                    decisionReasons.Add("Used performance-optimized strategy");
                    break;

                case TaskRoutingStrategy.Reliability:
                    selectedAgent = await SelectReliabilityAgentAsync(agents, task, cancellationToken);
                    decisionReasons.Add("Used reliability-focused strategy");
                    break;

                case TaskRoutingStrategy.LeastConnections:
                    selectedAgent = await SelectLeastConnectionsAgentAsync(agents, task, cancellationToken);
                    decisionReasons.Add("Used least connections strategy");
                    break;

                case TaskRoutingStrategy.RoundRobin:
                    selectedAgent = await SelectRoundRobinAgentAsync(agents, task, cancellationToken);
                    decisionReasons.Add("Used round-robin strategy");
                    break;

                default:
                    selectedAgent = await SelectOptimalAgentAsync(agents, task, cancellationToken);
                    decisionReasons.Add("Used default optimal selection");
                    break;
            }

            if (selectedAgent == null)
            {
                var error = new AgentError
                {
                    Code = "NO_AVAILABLE_INSTANCES",
                    Message = "No available agent instances found",
                    Severity = ErrorSeverity.Warning
                };

                var failureResult = new TaskRoutingResult
                {
                    Task = task,
                    Strategy = routingStrategy,
                    Success = false,
                    Error = error,
                    RoutingDurationMs = stopwatch.ElapsedMilliseconds,
                    DecisionReasons = decisionReasons,
                    AlternativeAgents = agents
                };

                OnTaskRoutingFailed(task, error, routingStrategy);
                return failureResult;
            }

            // Calculate metrics for the selected agent
            var selectedAgentDef = agents.First(a => a.Id == selectedAgent.AgentId);
            suitabilityScore = await EvaluateAgentSuitabilityAsync(selectedAgentDef, task, cancellationToken);
            predictedExecutionTime = await PredictExecutionTimeAsync(selectedAgentDef, task, cancellationToken);

            stopwatch.Stop();

            var result = new TaskRoutingResult
            {
                Task = task,
                SelectedAgent = selectedAgent,
                Strategy = routingStrategy,
                Success = true,
                SuitabilityScore = suitabilityScore,
                PredictedExecutionTimeMs = predictedExecutionTime,
                RoutingDurationMs = stopwatch.ElapsedMilliseconds,
                DecisionReasons = decisionReasons,
                AlternativeAgents = agents.Where(a => a.Id != selectedAgent.AgentId).ToList()
            };

            // Record routing decision
            RecordRoutingDecision(result);

            _logger.LogInformation("Routed task {TaskId} to agent {AgentId} instance {InstanceId} using {Strategy} strategy",
                task.TaskId, selectedAgent.AgentId, selectedAgent.InstanceId, routingStrategy);

            _metricsCollector.IncrementCounter("task.routed", tags: new Dictionary<string, string>
            {
                ["task_type"] = task.Type,
                ["agent_id"] = selectedAgent.AgentId,
                ["strategy"] = routingStrategy.ToString(),
                ["duration_ms"] = stopwatch.ElapsedMilliseconds.ToString()
            });

            OnTaskRouted(result);
            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            var error = new AgentError
            {
                Code = "ROUTING_ERROR",
                Message = ex.Message,
                Details = ex.StackTrace,
                Severity = ErrorSeverity.Error
            };

            var errorResult = new TaskRoutingResult
            {
                Task = task,
                Strategy = routingStrategy,
                Success = false,
                Error = error,
                RoutingDurationMs = stopwatch.ElapsedMilliseconds,
                DecisionReasons = decisionReasons
            };

            OnTaskRoutingFailed(task, error, routingStrategy);
            return errorResult;
        }
    }

    public async Task<double> EvaluateAgentSuitabilityAsync(
        AgentDefinition agent,
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        if (agent == null) throw new ArgumentNullException(nameof(agent));
        if (task == null) throw new ArgumentNullException(nameof(task));

        var score = 0.0;

        try
        {
            // Base score for task type compatibility
            if (await CanHandleTaskTypeAsync(agent, task.Type, cancellationToken: cancellationToken))
            {
                score += 40.0;
            }

            // Load metrics contribution
            var loadMetrics = await _agentRegistry.GetAgentLoadMetricsAsync(agent.Id, cancellationToken);
            if (loadMetrics != null)
            {
                if (loadMetrics.Available)
                {
                    score += 20.0;
                    // Lower load score is better, so invert it
                    score += (100 - loadMetrics.LoadScore) * 0.2;
                }
            }

            // Performance metrics contribution
            var perfMetrics = await _agentRegistry.GetAgentPerformanceMetricsAsync(agent.Id, cancellationToken);
            if (perfMetrics != null)
            {
                score += perfMetrics.SuccessRate * 0.2;
                score += perfMetrics.PerformanceScore * 0.1;
            }

            // Historical success for this task type
            var history = GetTaskTypeHistory(agent.Id, task.Type);
            if (history.TotalAttempts > 0)
            {
                var successRate = (double)history.Successes / history.TotalAttempts;
                score += successRate * 10.0;
            }

            // Priority matching
            if (task.Priority >= 8) // High priority tasks
            {
                score += 5.0;
            }

        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate agent {AgentId} suitability", agent.Id);
            score = 0.0;
        }

        return Math.Clamp(score, 0.0, 100.0);
    }

    public async Task<IEnumerable<AgentLoadInfo>> GetLoadDistributionAsync(
        IEnumerable<string>? agentIds = null,
        CancellationToken cancellationToken = default)
    {
        var targetAgentIds = agentIds?.ToList() ?? (await _agentRegistry.ListAgentsAsync(cancellationToken: cancellationToken))
            .Select(a => a.Id).ToList();

        var loadInfos = new List<AgentLoadInfo>();

        foreach (var agentId in targetAgentIds)
        {
            try
            {
                var agent = await _agentRegistry.GetAgentAsync(agentId, cancellationToken);
                var loadMetrics = await _agentRegistry.GetAgentLoadMetricsAsync(agentId, cancellationToken);

                if (agent != null && loadMetrics != null)
                {
                    loadInfos.Add(new AgentLoadInfo
                    {
                        AgentId = agentId,
                        AgentName = agent.Name,
                        LoadScore = loadMetrics.LoadScore,
                        ActiveTasks = loadMetrics.ActiveTasks,
                        QueuedTasks = loadMetrics.QueuedTasks,
                        AverageResponseTimeMs = loadMetrics.AverageExecutionTimeMs,
                        HealthStatus = HealthStatus.Healthy, // Would check actual health
                        Available = loadMetrics.Available,
                        LastUpdated = loadMetrics.LastUpdated
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get load info for agent {AgentId}", agentId);
            }
        }

        return loadInfos;
    }

    public async Task<long> PredictExecutionTimeAsync(
        AgentDefinition agent,
        AgentTask task,
        CancellationToken cancellationToken = default)
    {
        if (agent == null) throw new ArgumentNullException(nameof(agent));
        if (task == null) throw new ArgumentNullException(nameof(task));

        try
        {
            // Get historical data for this agent and task type
            var history = GetTaskTypeHistory(agent.Id, task.Type);
            if (history.TotalAttempts > 0)
            {
                return (long)history.AverageExecutionTimeMs;
            }

            // Get performance metrics
            var perfMetrics = await _agentRegistry.GetAgentPerformanceMetricsAsync(agent.Id, cancellationToken);
            if (perfMetrics != null)
            {
                return (long)perfMetrics.AverageExecutionTimeMs;
            }

            // Default prediction based on task complexity
            var baseTime = task.TimeoutSeconds * 1000 * 0.1; // 10% of timeout as estimate
            return (long)Math.Max(baseTime, 5000); // Minimum 5 seconds
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to predict execution time for agent {AgentId}", agent.Id);
            return 60000; // Default 1 minute
        }
    }

    public async Task<bool> CanHandleTaskTypeAsync(
        AgentDefinition agent,
        string taskType,
        IEnumerable<string>? requiredCapabilities = null,
        CancellationToken cancellationToken = default)
    {
        if (agent == null) throw new ArgumentNullException(nameof(agent));
        if (string.IsNullOrEmpty(taskType)) throw new ArgumentNullException(nameof(taskType));

        try
        {
            // Check if agent type can handle task type (basic mapping)
            if (agent.Type.ToString().Equals(taskType, StringComparison.OrdinalIgnoreCase) ||
                agent.Tags.Any(t => t.Equals(taskType, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            // Check if agent has required capabilities
            if (requiredCapabilities?.Any() == true)
            {
                foreach (var capability in requiredCapabilities)
                {
                    var agents = await _agentRegistry.FindAgentsByCapabilityAsync(capability, HealthStatus.Healthy, cancellationToken);
                    if (!agents.Any(a => a.Id == agent.Id))
                    {
                        return false;
                    }
                }
                return true;
            }

            // Check capabilities for task type mapping
            var taskTypeAgents = await _agentRegistry.FindAgentsForTaskAsync(taskType, cancellationToken: cancellationToken);
            return taskTypeAgents.Any(a => a.Id == agent.Id);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if agent {AgentId} can handle task type {TaskType}", agent.Id, taskType);
            return false;
        }
    }

    public async Task<IEnumerable<TaskRoutingRecommendation>> GetRoutingRecommendationsAsync(
        AgentTask task,
        int maxRecommendations = 5,
        CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        var suitableAgents = await _agentRegistry.FindAgentsForTaskAsync(task.Type, cancellationToken: cancellationToken);
        var recommendations = new List<TaskRoutingRecommendation>();

        foreach (var agent in suitableAgents.Take(maxRecommendations))
        {
            try
            {
                var score = await EvaluateAgentSuitabilityAsync(agent, task, cancellationToken);
                var predictedTime = await PredictExecutionTimeAsync(agent, task, cancellationToken);
                var reasons = new List<string>();
                var risks = new List<string>();

                // Add reasoning
                if (score > 80)
                    reasons.Add("High suitability score");
                if (predictedTime < 30000)
                    reasons.Add("Fast predicted execution time");

                var loadMetrics = await _agentRegistry.GetAgentLoadMetricsAsync(agent.Id, cancellationToken);
                if (loadMetrics != null)
                {
                    if (loadMetrics.LoadScore < 50)
                        reasons.Add("Low current load");
                    else if (loadMetrics.LoadScore > 80)
                        risks.Add("High current load");
                }

                recommendations.Add(new TaskRoutingRecommendation
                {
                    Agent = agent,
                    Score = score,
                    Reasons = reasons,
                    PredictedExecutionTimeMs = predictedTime,
                    EstimatedCost = CalculateEstimatedCost(agent, task),
                    Confidence = CalculateConfidence(agent, task),
                    Risks = risks
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create recommendation for agent {AgentId}", agent.Id);
            }
        }

        return recommendations.OrderByDescending(r => r.Score).ToList();
    }

    public async Task RecordExecutionOutcomeAsync(
        TaskRoutingResult routingResult,
        TaskResult executionResult,
        CancellationToken cancellationToken = default)
    {
        if (routingResult?.SelectedAgent == null) return;

        var agentId = routingResult.SelectedAgent.AgentId;
        var taskType = routingResult.Task.Type;

        var historyKey = $"{agentId}:{taskType}";
        var history = _routingHistory.GetOrAdd(historyKey, _ => new TaskRoutingHistory
        {
            AgentId = agentId,
            TaskType = taskType
        });

        history.TotalAttempts++;
        if (executionResult.Success)
        {
            history.Successes++;
        }
        else
        {
            history.Failures++;
        }

        // Update average execution time
        var totalTime = history.AverageExecutionTimeMs * (history.TotalAttempts - 1) + executionResult.DurationMs;
        history.AverageExecutionTimeMs = totalTime / history.TotalAttempts;
        history.LastExecuted = DateTimeOffset.UtcNow;

        _logger.LogDebug("Recorded execution outcome for agent {AgentId}, task type {TaskType}: {Success}",
            agentId, taskType, executionResult.Success);

        await Task.CompletedTask;
    }

    // Private helper methods for different routing strategies

    private async Task<AgentInstance?> SelectBalancedAgentAsync(
        List<AgentDefinition> agents,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        var evaluations = new List<(AgentDefinition agent, double combinedScore, AgentInstance? instance)>();

        foreach (var agent in agents)
        {
            var suitabilityScore = await EvaluateAgentSuitabilityAsync(agent, task, cancellationToken);
            var loadMetrics = await _agentRegistry.GetAgentLoadMetricsAsync(agent.Id, cancellationToken);
            var loadScore = loadMetrics?.LoadScore ?? 100;

            // Balanced score considers both suitability and load (prefer lower load)
            var combinedScore = (suitabilityScore * 0.6) + ((100 - loadScore) * 0.4);

            var instances = await _agentRegistry.GetAvailableInstancesAsync(agent.Id, AgentStatus.Running, cancellationToken);
            var bestInstance = await SelectBestInstanceAsync(instances, task, cancellationToken);

            if (bestInstance != null)
            {
                evaluations.Add((agent, combinedScore, bestInstance));
            }
        }

        return evaluations.OrderByDescending(e => e.combinedScore).FirstOrDefault().instance;
    }

    private async Task<AgentInstance?> SelectPerformanceAgentAsync(
        List<AgentDefinition> agents,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        AgentInstance? bestInstance = null;
        double bestPerformanceScore = 0;

        foreach (var agent in agents)
        {
            var perfMetrics = await _agentRegistry.GetAgentPerformanceMetricsAsync(agent.Id, cancellationToken);
            if (perfMetrics != null)
            {
                var instances = await _agentRegistry.GetAvailableInstancesAsync(agent.Id, AgentStatus.Running, cancellationToken);
                var instance = await SelectBestInstanceAsync(instances, task, cancellationToken);

                if (instance != null && perfMetrics.PerformanceScore > bestPerformanceScore)
                {
                    bestPerformanceScore = perfMetrics.PerformanceScore;
                    bestInstance = instance;
                }
            }
        }

        return bestInstance;
    }

    private async Task<AgentInstance?> SelectReliabilityAgentAsync(
        List<AgentDefinition> agents,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        AgentInstance? bestInstance = null;
        double bestReliabilityScore = 0;

        foreach (var agent in agents)
        {
            var perfMetrics = await _agentRegistry.GetAgentPerformanceMetricsAsync(agent.Id, cancellationToken);
            if (perfMetrics != null)
            {
                var instances = await _agentRegistry.GetAvailableInstancesAsync(agent.Id, AgentStatus.Running, cancellationToken);
                var instance = await SelectBestInstanceAsync(instances, task, cancellationToken);

                if (instance != null && perfMetrics.ReliabilityScore > bestReliabilityScore)
                {
                    bestReliabilityScore = perfMetrics.ReliabilityScore;
                    bestInstance = instance;
                }
            }
        }

        return bestInstance;
    }

    private async Task<AgentInstance?> SelectLeastConnectionsAgentAsync(
        List<AgentDefinition> agents,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        AgentInstance? bestInstance = null;
        int lowestConnections = int.MaxValue;

        foreach (var agent in agents)
        {
            var loadMetrics = await _agentRegistry.GetAgentLoadMetricsAsync(agent.Id, cancellationToken);
            if (loadMetrics != null)
            {
                var connections = loadMetrics.ActiveTasks + loadMetrics.QueuedTasks;
                if (connections < lowestConnections)
                {
                    var instances = await _agentRegistry.GetAvailableInstancesAsync(agent.Id, AgentStatus.Running, cancellationToken);
                    var instance = await SelectBestInstanceAsync(instances, task, cancellationToken);

                    if (instance != null)
                    {
                        lowestConnections = connections;
                        bestInstance = instance;
                    }
                }
            }
        }

        return bestInstance;
    }

    private async Task<AgentInstance?> SelectRoundRobinAgentAsync(
        List<AgentDefinition> agents,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        if (agents.Count == 0) return null;

        var taskTypeKey = task.Type;
        var counter = _roundRobinCounters.AddOrUpdate(taskTypeKey, 0, (_, current) => (current + 1) % agents.Count);
        var selectedAgent = agents[counter];

        var instances = await _agentRegistry.GetAvailableInstancesAsync(selectedAgent.Id, AgentStatus.Running, cancellationToken);
        return await SelectBestInstanceAsync(instances, task, cancellationToken);
    }

    private async Task<AgentInstance?> SelectBestInstanceAsync(
        IEnumerable<AgentInstance> instances,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        var instanceList = instances.ToList();
        if (instanceList.Count == 0) return null;

        // For now, select the first healthy instance
        // Could be enhanced with instance-specific load balancing
        foreach (var instance in instanceList)
        {
            try
            {
                var health = await _agentRuntime.CheckInstanceHealthAsync(instance.InstanceId, cancellationToken);
                if (health == HealthStatus.Healthy)
                {
                    return instance;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check health for instance {InstanceId}", instance.InstanceId);
            }
        }

        // If no healthy instances found, return the first one as fallback
        return instanceList.FirstOrDefault();
    }

    private TaskRoutingHistory GetTaskTypeHistory(string agentId, string taskType)
    {
        var key = $"{agentId}:{taskType}";
        return _routingHistory.GetOrAdd(key, _ => new TaskRoutingHistory
        {
            AgentId = agentId,
            TaskType = taskType
        });
    }

    private double CalculateEstimatedCost(AgentDefinition agent, AgentTask task)
    {
        // Simple cost calculation - could be enhanced with real pricing models
        return 1.0; // Base cost unit
    }

    private double CalculateConfidence(AgentDefinition agent, AgentTask task)
    {
        var history = GetTaskTypeHistory(agent.Id, task.Type);
        if (history.TotalAttempts < 5)
            return 50.0; // Low confidence for new combinations

        var successRate = (double)history.Successes / history.TotalAttempts;
        return Math.Min(95.0, 50.0 + (successRate * 45.0));
    }

    private void RecordRoutingDecision(TaskRoutingResult result)
    {
        // This could be enhanced to persist routing decisions for ML training
        _logger.LogDebug("Routing decision recorded for task {TaskId}", result.Task.TaskId);
    }

    private void CleanupHistory(object? state)
    {
        try
        {
            var cutoffTime = DateTimeOffset.UtcNow.AddDays(-30);
            var keysToRemove = _routingHistory
                .Where(kvp => kvp.Value.LastExecuted < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _routingHistory.TryRemove(key, out _);
            }

            if (keysToRemove.Count > 0)
            {
                _logger.LogDebug("Cleaned up {Count} old routing history entries", keysToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during routing history cleanup");
        }
    }

    private void OnTaskRouted(TaskRoutingResult result)
    {
        TaskRouted?.Invoke(this, new TaskRoutedEventArgs { Result = result });
    }

    private void OnTaskRoutingFailed(AgentTask task, AgentError error, TaskRoutingStrategy strategy)
    {
        TaskRoutingFailed?.Invoke(this, new TaskRoutingFailedEventArgs
        {
            Task = task,
            Error = error,
            Strategy = strategy
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _cleanupTimer?.Dispose();
        _disposed = true;
    }
}

/// <summary>
/// Task routing history for learning and optimization
/// </summary>
internal class TaskRoutingHistory
{
    public required string AgentId { get; init; }
    public required string TaskType { get; init; }
    public int TotalAttempts { get; set; }
    public int Successes { get; set; }
    public int Failures { get; set; }
    public double AverageExecutionTimeMs { get; set; }
    public DateTimeOffset LastExecuted { get; set; } = DateTimeOffset.UtcNow;
}