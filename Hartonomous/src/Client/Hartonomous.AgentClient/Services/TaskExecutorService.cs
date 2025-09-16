using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        IOptions<AgentClientConfiguration> configuration)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metricsCollector = metricsCollector ?? throw new ArgumentNullException(nameof(metricsCollector));
        _agentRuntime = agentRuntime ?? throw new ArgumentNullException(nameof(agentRuntime));
        _currentUserService = currentUserService ?? throw new ArgumentNullException(nameof(currentUserService));
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

        try
        {
            // Get or create agent instance
            var instance = await GetOrCreateAgentInstanceAsync(task, cancellationToken);

            // Create execution context
            var context = new TaskExecutionContext
            {
                ExecutionId = Guid.NewGuid().ToString(),
                WorkingDirectory = instance.WorkingDirectory,
                Environment = new Dictionary<string, string>(instance.Environment),
                SecurityContext = new SecurityContext
                {
                    TrustLevel = TrustLevel.Medium // Default trust level
                }
            };

            task = task with { Context = context };
            _tasks.TryUpdate(task.TaskId, task, _tasks[task.TaskId]);

            // Execute the task (this would delegate to the specific agent)
            await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken); // Simulate work

            stopwatch.Stop();

            return new TaskResult
            {
                Success = true,
                Message = "Task completed successfully",
                DurationMs = stopwatch.ElapsedMilliseconds,
                Data = new Dictionary<string, object>
                {
                    ["result"] = "Task executed successfully",
                    ["executionTime"] = stopwatch.ElapsedMilliseconds
                }
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            return new TaskResult
            {
                Success = false,
                Message = ex.Message,
                DurationMs = stopwatch.ElapsedMilliseconds
            };
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