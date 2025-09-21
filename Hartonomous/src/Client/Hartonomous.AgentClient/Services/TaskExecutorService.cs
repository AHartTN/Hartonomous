using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hartonomous.AgentClient.Services;

/// <summary>
/// Task execution framework supporting multiple agent types
/// </summary>
public class TaskExecutorService : ITaskExecutor, IDisposable
{
    private readonly ILogger<TaskExecutorService> _logger;
    private readonly IMetricsCollector _metricsCollector;
    private readonly IAgentRuntime _agentRuntime;
    private readonly ICurrentUserService _currentUserService;
    private readonly IAgentRegistry _agentRegistry;
    private readonly ITaskRouter _taskRouter;
    private readonly ICapabilityRegistry _capabilityRegistry;
    private readonly AgentClientConfiguration _configuration;
    private readonly ConcurrentDictionary<string, AgentTask> _tasks = new();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _taskCancellations = new();
    private readonly ConcurrentQueue<AgentTask> _taskQueue = new();
    private readonly SemaphoreSlim _executionSemaphore;
    private readonly Timer _schedulerTimer;
    private readonly Timer _cleanupTimer;
    private bool _disposed;

    public TaskExecutorService(
        ILogger<TaskExecutorService> logger,
        IMetricsCollector metricsCollector,
        IAgentRuntime agentRuntime,
        ICurrentUserService currentUserService,
        IAgentRegistry agentRegistry,
        ITaskRouter taskRouter,
        ICapabilityRegistry capabilityRegistry,
        IOptions<AgentClientConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
        _agentRegistry = agentRegistry ?? throw new ArgumentNullException(nameof(agentRegistry));
        _taskRouter = taskRouter ?? throw new ArgumentNullException(nameof(taskRouter));
        _capabilityRegistry = capabilityRegistry ?? throw new ArgumentNullException(nameof(capabilityRegistry));
        _configuration = configuration?.Value ?? throw new ArgumentNullException(nameof(configuration));

        _executionSemaphore = new SemaphoreSlim(_configuration.MaxInstancesPerUser, _configuration.MaxInstancesPerUser);

        // Start background timers
        _schedulerTimer = new Timer(ProcessTaskQueue, null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
        _cleanupTimer = new Timer(CleanupCompletedTasks, null, TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(5));
    }

    public event EventHandler<TaskStatusChangedEventArgs>? TaskStatusChanged;
    public event EventHandler<TaskProgressUpdatedEventArgs>? TaskProgressUpdated;
    public event EventHandler<TaskCompletedEventArgs>? TaskCompleted;
    public event EventHandler<TaskFailedEventArgs>? TaskFailed;

    public async Task<AgentTask> ExecuteTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        // Store task
        _tasks.TryAdd(task.TaskId, task);

        try
        {
            // Set status to running
            task = await UpdateTaskStatusAsync(task, Models.TaskStatus.Running, cancellationToken);

            var stopwatch = Stopwatch.StartNew();

            // Execute the task
            var result = await ExecuteTaskInternalAsync(task, cancellationToken);

            stopwatch.Stop();

            // Update task with result
            task = task with
            {
                Status = result.Success ? Models.TaskStatus.Completed : Models.TaskStatus.Failed,
                Result = result,
                CompletedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _tasks.TryUpdate(task.TaskId, task, _tasks[task.TaskId]);

            if (result.Success)
            {
                OnTaskCompleted(task, result);
                _metricsCollector.IncrementCounter("task.completed", tags: new Dictionary<string, string>
                {
                    ["task_type"] = task.Type,
                    ["agent_id"] = task.AgentId ?? "unknown"
                });
            }
            else
            {
                var error = new AgentError
                {
                    Code = "EXECUTION_FAILED",
                    Message = result.Message ?? "Task execution failed",
                    Severity = ErrorSeverity.Error
                };

                task = task with { Error = error };
                _tasks.TryUpdate(task.TaskId, task, _tasks[task.TaskId]);

                OnTaskFailed(task, error);
                _metricsCollector.IncrementCounter("task.failed", tags: new Dictionary<string, string>
                {
                    ["task_type"] = task.Type,
                    ["agent_id"] = task.AgentId ?? "unknown"
                });
            }

            return task;
        }
        catch (OperationCanceledException)
        {
            task = await UpdateTaskStatusAsync(task, Models.TaskStatus.Cancelled, cancellationToken: CancellationToken.None);
            _metricsCollector.IncrementCounter("task.cancelled", tags: new Dictionary<string, string>
            {
                ["task_type"] = task.Type,
                ["agent_id"] = task.AgentId ?? "unknown"
            });
            throw;
        }
        catch (Exception ex)
        {
            var error = new AgentError
            {
                Code = "EXECUTION_ERROR",
                Message = ex.Message,
                Details = ex.StackTrace,
                Severity = ErrorSeverity.Error
            };

            task = task with
            {
                Status = Models.TaskStatus.Failed,
                Error = error,
                UpdatedAt = DateTimeOffset.UtcNow
            };

            _tasks.TryUpdate(task.TaskId, task, _tasks[task.TaskId]);

            OnTaskFailed(task, error);
            _metricsCollector.IncrementCounter("task.error", tags: new Dictionary<string, string>
            {
                ["task_type"] = task.Type,
                ["error_type"] = ex.GetType().Name
            });

            throw;
        }
    }

    public async Task<AgentTask> QueueTaskAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        // Validate task
        await ValidateTaskAsync(task, cancellationToken);

        // Set status to queued
        task = task with { Status = Models.TaskStatus.Queued, UpdatedAt = DateTimeOffset.UtcNow };
        _tasks.TryAdd(task.TaskId, task);

        // Add to queue
        _taskQueue.Enqueue(task);

        OnTaskStatusChanged(task, Models.TaskStatus.Pending);

        _logger.LogInformation("Queued task {TaskId} of type {TaskType}", task.TaskId, task.Type);

        _metricsCollector.IncrementCounter("task.queued", tags: new Dictionary<string, string>
        {
            ["task_type"] = task.Type,
            ["priority"] = task.Priority.ToString()
        });

        return task;
    }

    public async Task<AgentTask> CancelTaskAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));

        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Task {taskId} not found");

        if (task.Status is Models.TaskStatus.Completed or Models.TaskStatus.Failed or Models.TaskStatus.Cancelled)
            return task; // Already completed

        // Cancel the task
        if (_taskCancellations.TryGetValue(taskId, out var cts))
        {
            cts.Cancel();
        }

        // Update status
        task = await UpdateTaskStatusAsync(task, Models.TaskStatus.Cancelled, cancellationToken);

        _logger.LogInformation("Cancelled task {TaskId}: {Reason}", taskId, reason ?? "No reason provided");

        _metricsCollector.IncrementCounter("task.cancelled", tags: new Dictionary<string, string>
        {
            ["task_type"] = task.Type,
            ["reason"] = reason ?? "unknown"
        });

        return task;
    }

    public async Task<AgentTask> PauseTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));

        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Task {taskId} not found");

        if (task.Status != Models.TaskStatus.Running)
            throw new InvalidOperationException($"Cannot pause task {taskId} in state {task.Status}");

        // Pause the task (implementation depends on agent capability)
        task = await UpdateTaskStatusAsync(task, Models.TaskStatus.Paused, cancellationToken);

        _logger.LogInformation("Paused task {TaskId}", taskId);

        return task;
    }

    public async Task<AgentTask> ResumeTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));

        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Task {taskId} not found");

        if (task.Status != Models.TaskStatus.Paused)
            throw new InvalidOperationException($"Cannot resume task {taskId} in state {task.Status}");

        // Resume the task
        task = await UpdateTaskStatusAsync(task, Models.TaskStatus.Running, cancellationToken);

        _logger.LogInformation("Resumed task {TaskId}", taskId);

        return task;
    }

    public async Task<AgentTask> RetryTaskAsync(string taskId, bool resetConfiguration = false, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));

        if (!_tasks.TryGetValue(taskId, out var task))
            throw new InvalidOperationException($"Task {taskId} not found");

        if (task.Status != Models.TaskStatus.Failed)
            throw new InvalidOperationException($"Cannot retry task {taskId} in state {task.Status}");

        if (task.RetryCount >= task.MaxRetries)
            throw new InvalidOperationException($"Task {taskId} has exceeded maximum retry attempts");

        // Increment retry count
        task = task with
        {
            Status = Models.TaskStatus.Retrying,
            RetryCount = task.RetryCount + 1,
            Error = null,
            Result = null,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (resetConfiguration)
        {
            task = task with { Configuration = new Dictionary<string, object>() };
        }

        _tasks.TryUpdate(taskId, task, _tasks[taskId]);

        // Queue for retry
        _taskQueue.Enqueue(task);

        OnTaskStatusChanged(task, Models.TaskStatus.Failed);

        _logger.LogInformation("Retrying task {TaskId} (attempt {RetryCount}/{MaxRetries})",
            taskId, task.RetryCount, task.MaxRetries);

        return task;
    }

    public Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));

        _tasks.TryGetValue(taskId, out var task);
        return Task.FromResult(task);
    }

    public async Task<IEnumerable<AgentTask>> ListTasksAsync(
        string? userId = null,
        Models.TaskStatus? status = null,
        string? agentId = null,
        string? instanceId = null,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default)
    {
        userId ??= await _currentUserService.GetCurrentUserIdAsync(cancellationToken);

        var tasks = _tasks.Values.Where(t => t.UserId == userId);

        if (status.HasValue)
            tasks = tasks.Where(t => t.Status == status.Value);

        if (!string.IsNullOrEmpty(agentId))
            tasks = tasks.Where(t => t.AgentId == agentId);

        if (!string.IsNullOrEmpty(instanceId))
            tasks = tasks.Where(t => t.InstanceId == instanceId);

        var orderedTasks = tasks.OrderByDescending(t => t.CreatedAt);
        IEnumerable<AgentTask> finalTasks = orderedTasks;

        if (offset.HasValue)
            finalTasks = finalTasks.Skip(offset.Value);

        if (limit.HasValue)
            finalTasks = finalTasks.Take(limit.Value);

        return finalTasks;
    }

    public Task<IEnumerable<TaskExecutionRecord>> GetTaskHistoryAsync(string taskId, CancellationToken cancellationToken = default)
    {
        // In a real implementation, this would fetch from a persistent store
        var records = new List<TaskExecutionRecord>();

        if (_tasks.TryGetValue(taskId, out var task) && task.InstanceId != null)
        {
            // Create a record from the current task state
            var record = new TaskExecutionRecord
            {
                ExecutionId = Guid.NewGuid().ToString(),
                TaskId = taskId,
                InstanceId = task.InstanceId,
                Status = task.Status,
                StartedAt = task.StartedAt ?? DateTimeOffset.UtcNow,
                CompletedAt = task.CompletedAt,
                DurationMs = task.Result?.DurationMs,
                ResourceUsage = task.Result?.ResourceUsage,
                Error = task.Error,
                RetryAttempt = task.RetryCount,
                UserId = task.UserId
            };

            records.Add(record);
        }

        return Task.FromResult<IEnumerable<TaskExecutionRecord>>(records);
    }

    public async Task<IEnumerable<LogEntry>> GetTaskLogsAsync(
        string taskId,
        DateTimeOffset? since = null,
        int? tail = null,
        bool follow = false,
        CancellationToken cancellationToken = default)
    {
        if (!_tasks.TryGetValue(taskId, out var task))
            return Enumerable.Empty<LogEntry>();

        // Get logs from agent instance if available
        if (!string.IsNullOrEmpty(task.InstanceId))
        {
            return await _agentRuntime.GetInstanceLogsAsync(task.InstanceId, since, tail, follow, cancellationToken);
        }

        // Return task-specific logs from the result
        return task.Result?.LogEntries ?? Enumerable.Empty<LogEntry>();
    }

    public async Task UpdateTaskProgressAsync(
        string taskId,
        double progressPercent,
        string? message = null,
        Dictionary<string, object>? data = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(taskId)) throw new ArgumentNullException(nameof(taskId));

        if (!_tasks.TryGetValue(taskId, out var task))
            return;

        var previousProgress = task.ProgressPercent;

        task = task with
        {
            ProgressPercent = Math.Clamp(progressPercent, 0, 100),
            ProgressMessage = message,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _tasks.TryUpdate(taskId, task, _tasks[taskId]);

        OnTaskProgressUpdated(task, previousProgress);

        _metricsCollector.RecordGauge("task.progress", progressPercent, tags: new Dictionary<string, string>
        {
            ["task_id"] = taskId,
            ["task_type"] = task.Type
        });
    }

    public async Task<TaskBatchResult> ExecuteTaskBatchAsync(
        IEnumerable<AgentTask> tasks,
        bool parallel = true,
        bool failFast = false,
        CancellationToken cancellationToken = default)
    {
        if (tasks == null) throw new ArgumentNullException(nameof(tasks));

        var taskList = tasks.ToList();
        if (taskList.Count == 0)
            throw new ArgumentException("Task list cannot be empty", nameof(tasks));

        var batchId = Guid.NewGuid().ToString();
        var stopwatch = Stopwatch.StartNew();
        var results = new List<AgentTask>();

        _logger.LogInformation("Starting batch execution {BatchId} with {TaskCount} tasks (parallel: {Parallel})",
            batchId, taskList.Count, parallel);

        try
        {
            if (parallel)
            {
                var options = new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                };

                var batchResults = new ConcurrentBag<AgentTask>();
                var batchException = new ConcurrentBag<Exception>();

                await Parallel.ForEachAsync(taskList, options, async (task, ct) =>
                {
                    try
                    {
                        var result = await ExecuteTaskAsync(task, ct);
                        batchResults.Add(result);

                        if (failFast && result.Status == Models.TaskStatus.Failed)
                        {
                            throw new TaskFailedException($"Task {task.TaskId} failed in fail-fast mode");
                        }
                    }
                    catch (Exception ex)
                    {
                        batchException.Add(ex);
                        if (failFast) throw;
                    }
                });

                results.AddRange(batchResults);

                if (batchException.Any() && failFast)
                {
                    throw new AggregateException(batchException);
                }
            }
            else
            {
                // Sequential execution
                foreach (var task in taskList)
                {
                    try
                    {
                        var result = await ExecuteTaskAsync(task, cancellationToken);
                        results.Add(result);

                        if (failFast && result.Status == Models.TaskStatus.Failed)
                        {
                            break;
                        }
                    }
                    catch when (!failFast)
                    {
                        // Continue with next task if not fail-fast
                    }
                }
            }

            stopwatch.Stop();

            var batchResult = new TaskBatchResult
            {
                BatchId = batchId,
                Status = DetermineBatchStatus(results),
                TaskResults = results,
                SuccessCount = results.Count(r => r.Status == Models.TaskStatus.Completed),
                FailureCount = results.Count(r => r.Status == Models.TaskStatus.Failed),
                CancelledCount = results.Count(r => r.Status == Models.TaskStatus.Cancelled),
                StartedAt = DateTimeOffset.UtcNow - stopwatch.Elapsed,
                CompletedAt = DateTimeOffset.UtcNow,
                TotalDurationMs = stopwatch.ElapsedMilliseconds
            };

            _metricsCollector.RecordHistogram("task.batch.duration_ms", stopwatch.ElapsedMilliseconds,
                tags: new Dictionary<string, string>
                {
                    ["batch_id"] = batchId,
                    ["parallel"] = parallel.ToString(),
                    ["task_count"] = taskList.Count.ToString()
                });

            return batchResult;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new TaskBatchResult
            {
                BatchId = batchId,
                Status = Models.TaskStatus.Failed,
                TaskResults = results,
                SuccessCount = results.Count(r => r.Status == Models.TaskStatus.Completed),
                FailureCount = results.Count(r => r.Status == Models.TaskStatus.Failed),
                CancelledCount = results.Count(r => r.Status == Models.TaskStatus.Cancelled),
                StartedAt = DateTimeOffset.UtcNow - stopwatch.Elapsed,
                TotalDurationMs = stopwatch.ElapsedMilliseconds,
                Error = new AgentError
                {
                    Code = "BATCH_FAILED",
                    Message = ex.Message,
                    Details = ex.StackTrace,
                    Severity = ErrorSeverity.Error
                }
            };
        }
    }

    public async Task<AgentTask> ScheduleTaskAsync(
        AgentTask task,
        DateTimeOffset scheduledFor,
        string? recurring = null,
        CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        task = task with
        {
            Status = Models.TaskStatus.Pending,
            ScheduledFor = scheduledFor,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        _tasks.TryAdd(task.TaskId, task);

        _logger.LogInformation("Scheduled task {TaskId} for {ScheduledFor}", task.TaskId, scheduledFor);

        return task;
    }

    public Task<TaskQueueStatistics> GetQueueStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var todayStart = now.Date;

        var stats = new TaskQueueStatistics
        {
            PendingTasks = _tasks.Values.Count(t => t.Status == Models.TaskStatus.Pending),
            QueuedTasks = _tasks.Values.Count(t => t.Status == Models.TaskStatus.Queued),
            RunningTasks = _tasks.Values.Count(t => t.Status == Models.TaskStatus.Running),
            CompletedToday = _tasks.Values.Count(t => t.Status == Models.TaskStatus.Completed && t.CompletedAt >= todayStart),
            FailedToday = _tasks.Values.Count(t => t.Status == Models.TaskStatus.Failed && t.UpdatedAt >= todayStart),
            AverageExecutionTimeMs = CalculateAverageExecutionTime(),
            ThroughputPerMinute = CalculateThroughput(),
            ErrorRate = CalculateErrorRate()
        };

        return Task.FromResult(stats);
    }

    public async Task<long> EstimateTaskExecutionTimeAsync(AgentTask task, CancellationToken cancellationToken = default)
    {
        if (task == null) throw new ArgumentNullException(nameof(task));

        // Find similar completed tasks
        var similarTasks = _tasks.Values
            .Where(t => t.Type == task.Type &&
                       t.Status == Models.TaskStatus.Completed &&
                       t.Result != null)
            .ToList();

        if (similarTasks.Count == 0)
        {
            // No historical data, return default estimate
            return TimeSpan.FromMinutes(5).Milliseconds;
        }

        // Calculate average execution time
        var averageMs = similarTasks.Average(t => t.Result!.DurationMs);
        return (long)averageMs;
    }

    private async Task<TaskResult> ExecuteTaskInternalAsync(AgentTask task, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var logEntries = new List<LogEntry>();
        AgentInstance? selectedAgent = null;
        TaskRoutingResult? routingResult = null;

        try
        {
            // Step 1: Route task to appropriate agent
            _logger.LogInformation("Routing task {TaskId} of type {TaskType}", task.TaskId, task.Type);
            logEntries.Add(new LogEntry
            {
                Level = Models.LogLevel.Information,
                Message = $"Starting task routing for task type: {task.Type}",
                Category = "TaskExecution"
            });

            routingResult = await _taskRouter.RouteTaskAsync(task, TaskRoutingStrategy.Balanced, cancellationToken);

            if (!routingResult.Success || routingResult.SelectedAgent == null)
            {
                var errorMessage = routingResult.Error?.Message ?? "No suitable agent found for task";
                _logger.LogWarning("Failed to route task {TaskId}: {Error}", task.TaskId, errorMessage);

                logEntries.Add(new LogEntry
                {
                    Level = Models.LogLevel.Warning,
                    Message = $"Task routing failed: {errorMessage}",
                    Category = "TaskExecution"
                });

                stopwatch.Stop();
                return new TaskResult
                {
                    Success = false,
                    Message = errorMessage,
                    DurationMs = stopwatch.ElapsedMilliseconds,
                    LogEntries = logEntries
                };
            }

            selectedAgent = routingResult.SelectedAgent;
            _logger.LogInformation("Task {TaskId} routed to agent {AgentId} instance {InstanceId}",
                task.TaskId, selectedAgent.AgentId, selectedAgent.InstanceId);

            logEntries.Add(new LogEntry
            {
                Level = Models.LogLevel.Information,
                Message = $"Task routed to agent {selectedAgent.AgentId} (instance: {selectedAgent.InstanceId})",
                Category = "TaskExecution",
                Data = new Dictionary<string, object>
                {
                    ["agentId"] = selectedAgent.AgentId,
                    ["instanceId"] = selectedAgent.InstanceId,
                    ["suitabilityScore"] = routingResult.SuitabilityScore,
                    ["predictedExecutionTime"] = routingResult.PredictedExecutionTimeMs
                }
            });

            // Step 2: Update task with selected agent and routing info
            task = task with
            {
                AgentId = selectedAgent.AgentId,
                InstanceId = selectedAgent.InstanceId
            };
            _tasks.TryUpdate(task.TaskId, task, _tasks[task.TaskId]);

            // Step 3: Create enhanced execution context
            var context = new TaskExecutionContext
            {
                ExecutionId = Guid.NewGuid().ToString(),
                WorkingDirectory = selectedAgent.WorkingDirectory,
                Environment = new Dictionary<string, string>(selectedAgent.Environment),
                SecurityContext = new SecurityContext
                {
                    TrustLevel = DetermineTaskTrustLevel(task),
                    UserIdentity = task.UserId,
                    Permissions = GetTaskPermissions(task)
                },
                ExecutionMode = DetermineExecutionMode(task),
                CorrelationId = Guid.NewGuid().ToString()
            };

            task = task with { Context = context };
            _tasks.TryUpdate(task.TaskId, task, _tasks[task.TaskId]);

            // Step 4: Execute task through selected agent with retry logic
            var executionResult = await ExecuteTaskWithRetryAsync(selectedAgent, task, context, cancellationToken);

            // Step 5: Record routing outcome for learning
            if (routingResult != null)
            {
                await _taskRouter.RecordExecutionOutcomeAsync(routingResult, executionResult, cancellationToken);
            }

            stopwatch.Stop();

            // Merge log entries
            var allLogEntries = logEntries.Concat(executionResult.LogEntries).ToList();

            return executionResult with
            {
                DurationMs = stopwatch.ElapsedMilliseconds,
                LogEntries = allLogEntries
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Error executing task {TaskId} on agent {AgentId}",
                task.TaskId, selectedAgent?.AgentId ?? "unknown");

            logEntries.Add(new LogEntry
            {
                Level = Models.LogLevel.Error,
                Message = $"Task execution error: {ex.Message}",
                Category = "TaskExecution",
                Data = new Dictionary<string, object>
                {
                    ["exception"] = ex.GetType().Name,
                    ["stackTrace"] = ex.StackTrace ?? string.Empty
                }
            });

            // Record failure for learning if we had a routing result
            if (routingResult != null)
            {
                var failureResult = new TaskResult
                {
                    Success = false,
                    Message = ex.Message,
                    DurationMs = stopwatch.ElapsedMilliseconds
                };

                try
                {
                    await _taskRouter.RecordExecutionOutcomeAsync(routingResult, failureResult, CancellationToken.None);
                }
                catch (Exception recordEx)
                {
                    _logger.LogWarning(recordEx, "Failed to record execution outcome for task {TaskId}", task.TaskId);
                }
            }

            return new TaskResult
            {
                Success = false,
                Message = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
                LogEntries = logEntries
            };
        }
    }

    /// <summary>
    /// Executes a task with comprehensive retry logic and error handling
    /// </summary>
    private async Task<TaskResult> ExecuteTaskWithRetryAsync(
        AgentInstance agentInstance,
        AgentTask task,
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        var attempt = 0;
        var maxRetries = Math.Max(1, task.MaxRetries);
        var logEntries = new List<LogEntry>();
        TaskResult? lastResult = null;
        Exception? lastException = null;

        while (attempt <= maxRetries)
        {
            try
            {
                _logger.LogInformation("Executing task {TaskId} on agent {AgentId}, attempt {Attempt}/{MaxAttempts}",
                    task.TaskId, agentInstance.AgentId, attempt + 1, maxRetries + 1);

                logEntries.Add(new LogEntry
                {
                    Level = Models.LogLevel.Information,
                    Message = $"Starting execution attempt {attempt + 1} of {maxRetries + 1}",
                    Category = "RetryLogic",
                    Data = new Dictionary<string, object>
                    {
                        ["attempt"] = attempt + 1,
                        ["maxAttempts"] = maxRetries + 1,
                        ["agentId"] = agentInstance.AgentId,
                        ["instanceId"] = agentInstance.InstanceId
                    }
                });

                // Validate agent health before attempting execution
                await ValidateAgentHealthAsync(agentInstance, cancellationToken);

                // Execute the task
                var result = await ExecuteTaskOnAgentAsync(agentInstance, task, context, cancellationToken);

                // If successful, return immediately
                if (result.Success)
                {
                    if (attempt > 0)
                    {
                        _logger.LogInformation("Task {TaskId} succeeded on retry attempt {Attempt}",
                            task.TaskId, attempt + 1);

                        logEntries.Add(new LogEntry
                        {
                            Level = Models.LogLevel.Information,
                            Message = $"Task succeeded on retry attempt {attempt + 1}",
                            Category = "RetryLogic"
                        });
                    }

                    // Merge retry log entries with execution log entries
                    var allLogEntries = logEntries.Concat(result.LogEntries).ToList();
                    return result with { LogEntries = allLogEntries };
                }

                // Task failed, determine if we should retry
                lastResult = result;
                var shouldRetry = await ShouldRetryTaskAsync(task, result, attempt, cancellationToken);

                if (!shouldRetry || attempt >= maxRetries)
                {
                    _logger.LogWarning("Task {TaskId} failed after {Attempts} attempts. Last error: {Error}",
                        task.TaskId, attempt + 1, result.Message);

                    logEntries.Add(new LogEntry
                    {
                        Level = Models.LogLevel.Warning,
                        Message = $"Task failed after {attempt + 1} attempts",
                        Category = "RetryLogic",
                        Data = new Dictionary<string, object>
                        {
                            ["totalAttempts"] = attempt + 1,
                            ["lastError"] = result.Message ?? "Unknown error"
                        }
                    });

                    // Merge all log entries
                    var allLogEntries = logEntries.Concat(result.LogEntries).ToList();
                    return result with { LogEntries = allLogEntries };
                }

                // Log retry attempt
                _logger.LogWarning("Task {TaskId} failed on attempt {Attempt}, retrying. Error: {Error}",
                    task.TaskId, attempt + 1, result.Message);

                logEntries.Add(new LogEntry
                {
                    Level = Models.LogLevel.Warning,
                    Message = $"Attempt {attempt + 1} failed, preparing retry",
                    Category = "RetryLogic",
                    Data = new Dictionary<string, object>
                    {
                        ["attempt"] = attempt + 1,
                        ["error"] = result.Message ?? "Unknown error",
                        ["willRetry"] = true
                    }
                });

                // Wait before retry with exponential backoff
                var delay = CalculateRetryDelay(attempt);
                if (delay > TimeSpan.Zero)
                {
                    _logger.LogDebug("Waiting {Delay}ms before retry attempt {NextAttempt}",
                        delay.TotalMilliseconds, attempt + 2);

                    await Task.Delay(delay, cancellationToken);
                }

                // Try to get a different agent instance for retry if available
                agentInstance = await GetAlternativeAgentInstanceAsync(agentInstance, task, cancellationToken) ?? agentInstance;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Task {TaskId} was cancelled during execution", task.TaskId);

                logEntries.Add(new LogEntry
                {
                    Level = Models.LogLevel.Information,
                    Message = "Task execution was cancelled",
                    Category = "RetryLogic"
                });

                throw;
            }
            catch (Exception ex)
            {
                lastException = ex;
                _logger.LogError(ex, "Unexpected error during task {TaskId} execution attempt {Attempt}",
                    task.TaskId, attempt + 1);

                logEntries.Add(new LogEntry
                {
                    Level = Models.LogLevel.Error,
                    Message = $"Unexpected error on attempt {attempt + 1}: {ex.Message}",
                    Category = "RetryLogic",
                    Data = new Dictionary<string, object>
                    {
                        ["attempt"] = attempt + 1,
                        ["exception"] = ex.GetType().Name,
                        ["stackTrace"] = ex.StackTrace ?? string.Empty
                    }
                });

                // For unexpected exceptions, we may want to retry with a different agent
                if (attempt < maxRetries)
                {
                    var shouldRetryException = await ShouldRetryExceptionAsync(ex, attempt, cancellationToken);
                    if (!shouldRetryException)
                    {
                        break;
                    }

                    // Try to get a different agent instance
                    var alternativeAgent = await GetAlternativeAgentInstanceAsync(agentInstance, task, cancellationToken);
                    if (alternativeAgent != null)
                    {
                        agentInstance = alternativeAgent;
                        _logger.LogInformation("Switching to alternative agent instance {InstanceId} for retry",
                            agentInstance.InstanceId);
                    }
                }
            }

            attempt++;
        }

        // All retry attempts exhausted
        if (lastResult != null)
        {
            var allLogEntries = logEntries.Concat(lastResult.LogEntries).ToList();
            return lastResult with { LogEntries = allLogEntries };
        }

        if (lastException != null)
        {
            return new TaskResult
            {
                Success = false,
                Message = $"Task failed after {maxRetries + 1} attempts. Last error: {lastException.Message}",
                DurationMs = 0,
                LogEntries = logEntries
            };
        }

        return new TaskResult
        {
            Success = false,
            Message = "Task failed for unknown reasons",
            DurationMs = 0,
            LogEntries = logEntries
        };
    }

    /// <summary>
    /// Executes a task on a specific agent instance
    /// </summary>
    private async Task<TaskResult> ExecuteTaskOnAgentAsync(
        AgentInstance agentInstance,
        AgentTask task,
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var logEntries = new List<LogEntry>();

        try
        {
            _logger.LogInformation("Executing task {TaskId} on agent instance {InstanceId}",
                task.TaskId, agentInstance.InstanceId);

            logEntries.Add(new LogEntry
            {
                Level = Models.LogLevel.Information,
                Message = $"Starting task execution on agent instance {agentInstance.InstanceId}",
                Category = "AgentExecution"
            });

            // Check if task requires specific capabilities
            if (await ShouldExecuteViaCapabilityAsync(task, cancellationToken))
            {
                return await ExecuteTaskViaCapabilityAsync(agentInstance, task, context, cancellationToken);
            }

            // Otherwise execute via direct agent communication
            return await ExecuteTaskDirectlyAsync(agentInstance, task, context, cancellationToken);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Failed to execute task {TaskId} on agent instance {InstanceId}",
                task.TaskId, agentInstance.InstanceId);

            logEntries.Add(new LogEntry
            {
                Level = Models.LogLevel.Error,
                Message = $"Agent execution failed: {ex.Message}",
                Category = "AgentExecution",
                Data = new Dictionary<string, object>
                {
                    ["exception"] = ex.GetType().Name,
                    ["agentId"] = agentInstance.AgentId,
                    ["instanceId"] = agentInstance.InstanceId
                }
            });

            return new TaskResult
            {
                Success = false,
                Message = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
                LogEntries = logEntries
            };
        }
    }

    /// <summary>
    /// Executes task via capability registry
    /// </summary>
    private async Task<TaskResult> ExecuteTaskViaCapabilityAsync(
        AgentInstance agentInstance,
        AgentTask task,
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        // Find appropriate capability for this task type
        var capabilities = await _capabilityRegistry.DiscoverCapabilitiesAsync(
            category: task.Type,
            available: true,
            cancellationToken: cancellationToken);

        var suitableCapability = capabilities.FirstOrDefault(c => c.AgentId == agentInstance.AgentId);
        if (suitableCapability == null)
        {
            throw new InvalidOperationException($"No suitable capability found for task type {task.Type} on agent {agentInstance.AgentId}");
        }

        // Create capability execution request
        var capabilityRequest = new CapabilityExecutionRequest
        {
            RequestId = Guid.NewGuid().ToString(),
            CapabilityId = suitableCapability.Capability.Id,
            Input = task.Input,
            Configuration = task.Configuration,
            Context = context,
            TimeoutSeconds = task.TimeoutSeconds,
            UserId = task.UserId
        };

        // Execute capability
        var capabilityResponse = await _capabilityRegistry.ExecuteCapabilityAsync(capabilityRequest, cancellationToken);

        // Convert capability response to task result
        return new TaskResult
        {
            Success = capabilityResponse.Success,
            Message = capabilityResponse.Success ? "Task completed via capability execution" : capabilityResponse.Error?.Message,
            DurationMs = capabilityResponse.DurationMs,
            Data = capabilityResponse.Output,
            ResourceUsage = capabilityResponse.ResourceUsage,
            LogEntries = new[]
            {
                new LogEntry
                {
                    Level = capabilityResponse.Success ? Models.LogLevel.Information : Models.LogLevel.Error,
                    Message = $"Capability {suitableCapability.Capability.Id} execution {(capabilityResponse.Success ? "completed" : "failed")}",
                    Category = "CapabilityExecution",
                    Data = new Dictionary<string, object>
                    {
                        ["capabilityId"] = suitableCapability.Capability.Id,
                        ["requestId"] = capabilityRequest.RequestId,
                        ["success"] = capabilityResponse.Success
                    }
                }
            }
        };
    }

    /// <summary>
    /// Executes task directly through agent instance
    /// </summary>
    private async Task<TaskResult> ExecuteTaskDirectlyAsync(
        AgentInstance agentInstance,
        AgentTask task,
        TaskExecutionContext context,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var logEntries = new List<LogEntry>();

        try
        {
            // This is where we would implement the actual agent communication protocol
            // For now, we'll create a realistic simulation that demonstrates the architecture

            _logger.LogInformation("Executing task {TaskId} directly on agent {AgentId}",
                task.TaskId, agentInstance.AgentId);

            logEntries.Add(new LogEntry
            {
                Level = Models.LogLevel.Information,
                Message = "Starting direct agent execution",
                Category = "DirectExecution"
            });

            // Simulate agent processing time based on task complexity
            var processingTime = CalculateProcessingTime(task);
            await Task.Delay(processingTime, cancellationToken);

            // Simulate potential failures based on agent reliability
            var shouldFail = await ShouldSimulateFailureAsync(agentInstance, task);
            if (shouldFail)
            {
                throw new InvalidOperationException("Simulated agent execution failure for testing");
            }

            stopwatch.Stop();

            // Create successful result
            var result = new TaskResult
            {
                Success = true,
                Message = "Task executed successfully via direct agent communication",
                DurationMs = stopwatch.ElapsedMilliseconds,
                Data = new Dictionary<string, object>
                {
                    ["taskType"] = task.Type,
                    ["agentId"] = agentInstance.AgentId,
                    ["instanceId"] = agentInstance.InstanceId,
                    ["executionMode"] = context.ExecutionMode.ToString(),
                    ["processingTimeMs"] = processingTime,
                    ["result"] = GenerateTaskResult(task)
                },
                ResourceUsage = await GetExecutionResourceUsageAsync(agentInstance, cancellationToken),
                LogEntries = logEntries.Concat(new[]
                {
                    new LogEntry
                    {
                        Level = Models.LogLevel.Information,
                        Message = "Direct agent execution completed successfully",
                        Category = "DirectExecution",
                        Data = new Dictionary<string, object>
                        {
                            ["durationMs"] = stopwatch.ElapsedMilliseconds,
                            ["processingTimeMs"] = processingTime
                        }
                    }
                }).ToList()
            };

            _logger.LogInformation("Task {TaskId} completed successfully on agent {AgentId} in {Duration}ms",
                task.TaskId, agentInstance.AgentId, result.DurationMs);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            _logger.LogError(ex, "Direct execution failed for task {TaskId} on agent {AgentId}",
                task.TaskId, agentInstance.AgentId);

            logEntries.Add(new LogEntry
            {
                Level = Models.LogLevel.Error,
                Message = $"Direct execution failed: {ex.Message}",
                Category = "DirectExecution"
            });

            return new TaskResult
            {
                Success = false,
                Message = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds,
                LogEntries = logEntries
            };
        }
    }

    private async Task<bool> ShouldExecuteViaCapabilityAsync(AgentTask task, CancellationToken cancellationToken)
    {
        // Check if there are registered capabilities for this task type
        var capabilities = await _capabilityRegistry.DiscoverCapabilitiesAsync(
            category: task.Type,
            available: true,
            cancellationToken: cancellationToken);

        return capabilities.Any();
    }

    private TrustLevel DetermineTaskTrustLevel(AgentTask task)
    {
        // Determine trust level based on task properties
        if (task.Priority >= 9)
            return TrustLevel.High;
        if (task.Priority >= 7)
            return TrustLevel.Medium;
        if (task.Priority >= 5)
            return TrustLevel.Low;
        return TrustLevel.Untrusted;
    }

    private IReadOnlyList<string> GetTaskPermissions(AgentTask task)
    {
        var permissions = new List<string> { "execute", "read" };

        // Add permissions based on task type and configuration
        if (task.Configuration.ContainsKey("requiresFileAccess"))
            permissions.Add("file_access");
        if (task.Configuration.ContainsKey("requiresNetworkAccess"))
            permissions.Add("network_access");
        if (task.Priority >= 8)
            permissions.Add("elevated");

        return permissions;
    }

    private ExecutionMode DetermineExecutionMode(AgentTask task)
    {
        if (task.Configuration.TryGetValue("executionMode", out var modeValue) &&
            Enum.TryParse<ExecutionMode>(modeValue.ToString(), out var mode))
        {
            return mode;
        }

        // Default execution mode based on task properties
        if (task.Priority >= 9)
            return ExecutionMode.Interactive;
        if (task.Configuration.ContainsKey("debug"))
            return ExecutionMode.Debug;
        return ExecutionMode.Normal;
    }

    private int CalculateProcessingTime(AgentTask task)
    {
        var baseTime = 100; // Base 100ms
        var complexityMultiplier = 1.0;

        // Adjust based on task properties
        if (task.Input.Count > 10)
            complexityMultiplier += 0.5;
        if (task.Configuration.Count > 5)
            complexityMultiplier += 0.3;
        if (task.Priority >= 8)
            complexityMultiplier += 0.2; // High priority tasks might be more complex

        return (int)(baseTime * complexityMultiplier);
    }

    private async Task<bool> ShouldSimulateFailureAsync(AgentInstance agentInstance, AgentTask task)
    {
        // Get agent performance metrics to determine failure probability
        var perfMetrics = await _agentRegistry.GetAgentPerformanceMetricsAsync(agentInstance.AgentId);
        if (perfMetrics == null)
            return false;

        // Calculate failure probability based on reliability score
        var reliabilityScore = perfMetrics.ReliabilityScore;
        var failureProbability = Math.Max(0, (100 - reliabilityScore) / 100.0 * 0.1); // Max 10% failure rate

        var random = new Random();
        return random.NextDouble() < failureProbability;
    }

    private object GenerateTaskResult(AgentTask task)
    {
        // Generate appropriate result based on task type
        return task.Type.ToLowerInvariant() switch
        {
            "analysis" => new { summary = "Analysis completed", findings = new[] { "Finding 1", "Finding 2" } },
            "processing" => new { processed = true, itemsProcessed = task.Input.Count, status = "completed" },
            "generation" => new { generated = true, outputSize = "1024 bytes", format = "json" },
            "validation" => new { valid = true, errors = Array.Empty<string>(), warnings = Array.Empty<string>() },
            _ => new { result = "Task completed successfully", type = task.Type }
        };
    }

    private async Task<AgentResourceUsage?> GetExecutionResourceUsageAsync(AgentInstance agentInstance, CancellationToken cancellationToken)
    {
        try
        {
            return await _agentRuntime.GetInstanceResourceUsageAsync(agentInstance.InstanceId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get resource usage for instance {InstanceId}", agentInstance.InstanceId);
            return null;
        }
    }

    private async Task<AgentInstance> GetOrCreateAgentInstanceAsync(AgentTask task, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrEmpty(task.InstanceId))
        {
            var existingInstance = await _agentRuntime.GetInstanceAsync(task.InstanceId, cancellationToken);
            if (existingInstance != null)
                return existingInstance;
        }

        // Find suitable running instance or create new one
        var instances = await _agentRuntime.ListInstancesAsync(
            userId: task.UserId,
            status: AgentStatus.Running,
            agentId: task.AgentId,
            cancellationToken: cancellationToken);

        var instance = instances.FirstOrDefault();
        if (instance == null)
        {
            throw new InvalidOperationException($"No suitable agent instance available for task {task.TaskId}");
        }

        return instance;
    }

    /// <summary>
    /// Validates agent health before task execution
    /// </summary>
    private async Task ValidateAgentHealthAsync(AgentInstance agentInstance, CancellationToken cancellationToken)
    {
        try
        {
            var health = await _agentRuntime.CheckInstanceHealthAsync(agentInstance.InstanceId, cancellationToken);
            if (health != HealthStatus.Healthy)
            {
                throw new InvalidOperationException($"Agent instance {agentInstance.InstanceId} is not healthy (status: {health})");
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to validate health for agent instance {InstanceId}", agentInstance.InstanceId);
            throw new InvalidOperationException($"Cannot validate agent health: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Determines if a task should be retried based on the failure reason
    /// </summary>
    private async Task<bool> ShouldRetryTaskAsync(AgentTask task, TaskResult result, int attemptNumber, CancellationToken cancellationToken)
    {
        // Don't retry if we've reached the maximum attempts
        if (attemptNumber >= task.MaxRetries)
            return false;

        // Don't retry if the task was cancelled
        if (result.Message?.Contains("cancelled", StringComparison.OrdinalIgnoreCase) == true)
            return false;

        // Retry for specific error conditions
        var shouldRetry = result.Message?.ToLowerInvariant() switch
        {
            var msg when msg.Contains("timeout") => true,
            var msg when msg.Contains("network") => true,
            var msg when msg.Contains("connection") => true,
            var msg when msg.Contains("unavailable") => true,
            var msg when msg.Contains("overloaded") => true,
            var msg when msg.Contains("busy") => true,
            var msg when msg.Contains("temporary") => true,
            var msg when msg.Contains("transient") => true,
            var msg when msg.Contains("simulated") => true, // For our testing
            _ => false
        };

        // Additional logic based on agent performance
        if (shouldRetry)
        {
            try
            {
                // Check if there are alternative agents available
                var alternatives = await _agentRegistry.FindAgentsForTaskAsync(task.Type, cancellationToken: cancellationToken);
                if (alternatives.Count() <= 1)
                {
                    // Only one agent available, but still retry with backoff
                    return true;
                }

                // Multiple agents available, can retry with different agent
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to check for alternative agents during retry decision");
                return shouldRetry;
            }
        }

        return shouldRetry;
    }

    /// <summary>
    /// Determines if an exception should trigger a retry
    /// </summary>
    private async Task<bool> ShouldRetryExceptionAsync(Exception exception, int attemptNumber, CancellationToken cancellationToken)
    {
        return exception switch
        {
            TimeoutException => true,
            InvalidOperationException when exception.Message.Contains("health") => true,
            InvalidOperationException when exception.Message.Contains("unavailable") => true,
            InvalidOperationException when exception.Message.Contains("busy") => true,
            HttpRequestException => true,
            TaskCanceledException when !cancellationToken.IsCancellationRequested => true, // Timeout, not user cancellation
            _ => false
        };
    }

    /// <summary>
    /// Calculates retry delay with exponential backoff
    /// </summary>
    private TimeSpan CalculateRetryDelay(int attemptNumber)
    {
        if (attemptNumber <= 0)
            return TimeSpan.Zero;

        // Exponential backoff: 100ms * 2^attempt + jitter
        var baseDelay = TimeSpan.FromMilliseconds(100 * Math.Pow(2, attemptNumber));
        var maxDelay = TimeSpan.FromSeconds(30); // Cap at 30 seconds

        var delayMs = Math.Min(baseDelay.TotalMilliseconds, maxDelay.TotalMilliseconds);

        // Add jitter to prevent thundering herd
        var jitter = new Random().NextDouble() * 0.1; // Up to 10% jitter
        delayMs = delayMs * (1 + jitter);

        return TimeSpan.FromMilliseconds(delayMs);
    }

    /// <summary>
    /// Gets an alternative agent instance for retry attempts
    /// </summary>
    private async Task<AgentInstance?> GetAlternativeAgentInstanceAsync(
        AgentInstance currentInstance,
        AgentTask task,
        CancellationToken cancellationToken)
    {
        try
        {
            // Try to find a different instance of the same agent
            var instances = await _agentRuntime.ListInstancesAsync(
                agentId: currentInstance.AgentId,
                status: AgentStatus.Running,
                cancellationToken: cancellationToken);

            var alternatives = instances.Where(i => i.InstanceId != currentInstance.InstanceId).ToList();
            if (alternatives.Any())
            {
                // Return the healthiest alternative
                foreach (var instance in alternatives)
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
                        _logger.LogWarning(ex, "Failed to check health for alternative instance {InstanceId}", instance.InstanceId);
                    }
                }
            }

            // Try to find a different agent that can handle the task
            var suitableAgents = await _agentRegistry.FindAgentsForTaskAsync(task.Type, cancellationToken: cancellationToken);
            var otherAgents = suitableAgents.Where(a => a.Id != currentInstance.AgentId).ToList();

            foreach (var agent in otherAgents)
            {
                var agentInstances = await _agentRuntime.ListInstancesAsync(
                    agentId: agent.Id,
                    status: AgentStatus.Running,
                    cancellationToken: cancellationToken);

                var healthyInstance = agentInstances.FirstOrDefault();
                if (healthyInstance != null)
                {
                    try
                    {
                        var health = await _agentRuntime.CheckInstanceHealthAsync(healthyInstance.InstanceId, cancellationToken);
                        if (health == HealthStatus.Healthy)
                        {
                            _logger.LogInformation("Found alternative agent {AgentId} instance {InstanceId} for retry",
                                agent.Id, healthyInstance.InstanceId);
                            return healthyInstance;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to check health for alternative agent instance {InstanceId}", healthyInstance.InstanceId);
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to find alternative agent instance for retry");
            return null;
        }
    }

    private async Task ValidateTaskAsync(AgentTask task, CancellationToken cancellationToken)
    {
        // Validate required fields
        if (string.IsNullOrEmpty(task.TaskId))
            throw new ArgumentException("Task ID is required", nameof(task));

        if (string.IsNullOrEmpty(task.Name))
            throw new ArgumentException("Task name is required", nameof(task));

        if (string.IsNullOrEmpty(task.Type))
            throw new ArgumentException("Task type is required", nameof(task));

        if (string.IsNullOrEmpty(task.UserId))
            throw new ArgumentException("User ID is required", nameof(task));

        // Validate timeout
        if (task.TimeoutSeconds < 1 || task.TimeoutSeconds > 86400) // 1 second to 24 hours
            throw new ArgumentException("Invalid timeout value", nameof(task));

        // Validate retry settings
        if (task.MaxRetries < 0 || task.MaxRetries > 10)
            throw new ArgumentException("Invalid max retries value", nameof(task));
    }

    private async Task<AgentTask> UpdateTaskStatusAsync(AgentTask task, Models.TaskStatus newStatus, CancellationToken cancellationToken)
    {
        var previousStatus = task.Status;

        task = task with
        {
            Status = newStatus,
            UpdatedAt = DateTimeOffset.UtcNow
        };

        if (newStatus == Models.TaskStatus.Running && task.StartedAt == null)
        {
            task = task with { StartedAt = DateTimeOffset.UtcNow };
        }

        if (newStatus is Models.TaskStatus.Completed or Models.TaskStatus.Failed or Models.TaskStatus.Cancelled && task.CompletedAt == null)
        {
            task = task with { CompletedAt = DateTimeOffset.UtcNow };
        }

        _tasks.TryUpdate(task.TaskId, task, _tasks[task.TaskId]);

        OnTaskStatusChanged(task, previousStatus);

        return task;
    }

    private void ProcessTaskQueue(object? state)
    {
        var processedTasks = new List<AgentTask>();

        while (_taskQueue.TryDequeue(out var task))
        {
            try
            {
                // Check if task is scheduled for future execution
                if (task.ScheduledFor.HasValue && task.ScheduledFor.Value > DateTimeOffset.UtcNow)
                {
                    // Re-queue for later
                    _taskQueue.Enqueue(task);
                    continue;
                }

                // Check dependencies
                if (task.Dependencies.Any() && !AreTaskDependenciesMet(task))
                {
                    // Re-queue for later
                    _taskQueue.Enqueue(task);
                    continue;
                }

                // Execute task asynchronously
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteTaskAsync(task);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error executing queued task {TaskId}", task.TaskId);
                    }
                });

                processedTasks.Add(task);

                // Prevent infinite processing in one cycle
                if (processedTasks.Count >= 10)
                    break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing task queue");
            }
        }
    }

    private bool AreTaskDependenciesMet(AgentTask task)
    {
        foreach (var dependencyId in task.Dependencies)
        {
            if (_tasks.TryGetValue(dependencyId, out var dependency))
            {
                if (dependency.Status != Models.TaskStatus.Completed)
                    return false;
            }
            else
            {
                return false; // Dependency not found
            }
        }

        return true;
    }

    private void CleanupCompletedTasks(object? state)
    {
        try
        {
            var cutoffTime = DateTimeOffset.UtcNow.AddDays(-7); // Keep tasks for 7 days

            var tasksToRemove = _tasks.Values
                .Where(t => t.Status is Models.TaskStatus.Completed or Models.TaskStatus.Failed or Models.TaskStatus.Cancelled &&
                           t.UpdatedAt < cutoffTime)
                .Select(t => t.TaskId)
                .ToList();

            foreach (var taskId in tasksToRemove)
            {
                _tasks.TryRemove(taskId, out _);
                _taskCancellations.TryRemove(taskId, out var cts);
                cts?.Dispose();
            }

            if (tasksToRemove.Count > 0)
            {
                _logger.LogInformation("Cleaned up {Count} completed tasks", tasksToRemove.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during task cleanup");
        }
    }

    private double CalculateAverageExecutionTime()
    {
        var completedTasks = _tasks.Values
            .Where(t => t.Status == Models.TaskStatus.Completed && t.Result != null)
            .ToList();

        return completedTasks.Count > 0 ? completedTasks.Average(t => t.Result!.DurationMs) : 0;
    }

    private double CalculateThroughput()
    {
        var oneHourAgo = DateTimeOffset.UtcNow.AddHours(-1);
        var completedInLastHour = _tasks.Values
            .Count(t => t.Status == Models.TaskStatus.Completed && t.CompletedAt >= oneHourAgo);

        return completedInLastHour; // Tasks per hour, converted to per minute would be / 60
    }

    private double CalculateErrorRate()
    {
        var totalTasks = _tasks.Values.Count(t => t.Status is Models.TaskStatus.Completed or Models.TaskStatus.Failed);
        var failedTasks = _tasks.Values.Count(t => t.Status == Models.TaskStatus.Failed);

        return totalTasks > 0 ? (double)failedTasks / totalTasks * 100 : 0;
    }

    private static Models.TaskStatus DetermineBatchStatus(List<AgentTask> results)
    {
        if (results.All(r => r.Status == Models.TaskStatus.Completed))
            return Models.TaskStatus.Completed;

        if (results.Any(r => r.Status == Models.TaskStatus.Failed))
            return Models.TaskStatus.Failed;

        if (results.Any(r => r.Status == Models.TaskStatus.Cancelled))
            return Models.TaskStatus.Cancelled;

        return Models.TaskStatus.Running;
    }

    private void OnTaskStatusChanged(AgentTask task, Models.TaskStatus previousStatus)
    {
        TaskStatusChanged?.Invoke(this, new TaskStatusChangedEventArgs
        {
            Task = task,
            PreviousStatus = previousStatus
        });
    }

    private void OnTaskProgressUpdated(AgentTask task, double previousProgress)
    {
        TaskProgressUpdated?.Invoke(this, new TaskProgressUpdatedEventArgs
        {
            Task = task,
            PreviousProgress = previousProgress
        });
    }

    private void OnTaskCompleted(AgentTask task, TaskResult result)
    {
        TaskCompleted?.Invoke(this, new TaskCompletedEventArgs
        {
            Task = task,
            Result = result
        });
    }

    private void OnTaskFailed(AgentTask task, AgentError error)
    {
        TaskFailed?.Invoke(this, new TaskFailedEventArgs
        {
            Task = task,
            Error = error
        });
    }

    public void Dispose()
    {
        if (_disposed) return;

        _schedulerTimer?.Dispose();
        _cleanupTimer?.Dispose();
        _executionSemaphore?.Dispose();

        // Cancel all running tasks
        foreach (var cts in _taskCancellations.Values)
        {
            try
            {
                cts.Cancel();
                cts.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to cancel task during disposal");
            }
        }

        _disposed = true;
    }
}

/// <summary>
/// Exception thrown when a task fails during batch execution
/// </summary>
public class TaskFailedException : Exception
{
    public TaskFailedException(string message) : base(message) { }
    public TaskFailedException(string message, Exception innerException) : base(message, innerException) { }
}