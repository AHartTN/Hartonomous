using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for task execution framework supporting multiple agent types
/// </summary>
public interface ITaskExecutor
{
    /// <summary>
    /// Executes a task on the specified agent instance
    /// </summary>
    /// <param name="task">Task to execute</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated task with results</returns>
    Task<AgentTask> ExecuteTaskAsync(AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Queues a task for execution
    /// </summary>
    /// <param name="task">Task to queue</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Queued task with updated status</returns>
    Task<AgentTask> QueueTaskAsync(AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cancels a running or queued task
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="reason">Cancellation reason</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated task with cancelled status</returns>
    Task<AgentTask> CancelTaskAsync(string taskId, string? reason = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Pauses a running task
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated task with paused status</returns>
    Task<AgentTask> PauseTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resumes a paused task
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated task with running status</returns>
    Task<AgentTask> ResumeTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retries a failed task
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="resetConfiguration">Whether to reset task configuration</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated task queued for retry</returns>
    Task<AgentTask> RetryTaskAsync(string taskId, bool resetConfiguration = false, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a task by ID
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task or null if not found</returns>
    Task<AgentTask?> GetTaskAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists tasks for the current user
    /// </summary>
    /// <param name="userId">User ID to filter by (null for current user)</param>
    /// <param name="status">Optional status filter</param>
    /// <param name="agentId">Optional agent ID filter</param>
    /// <param name="instanceId">Optional instance ID filter</param>
    /// <param name="limit">Maximum number of tasks to return</param>
    /// <param name="offset">Number of tasks to skip</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of tasks</returns>
    Task<IEnumerable<AgentTask>> ListTasksAsync(
        string? userId = null,
        TaskStatus? status = null,
        string? agentId = null,
        string? instanceId = null,
        int? limit = null,
        int? offset = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets task execution history
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Task execution history</returns>
    Task<IEnumerable<TaskExecutionRecord>> GetTaskHistoryAsync(string taskId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets task execution logs
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="since">Get logs since this timestamp</param>
    /// <param name="tail">Maximum number of recent log entries</param>
    /// <param name="follow">Whether to follow/stream logs</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Log entries</returns>
    Task<IEnumerable<LogEntry>> GetTaskLogsAsync(
        string taskId,
        DateTimeOffset? since = null,
        int? tail = null,
        bool follow = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates task progress
    /// </summary>
    /// <param name="taskId">Task identifier</param>
    /// <param name="progressPercent">Progress percentage (0-100)</param>
    /// <param name="message">Progress message</param>
    /// <param name="data">Additional progress data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task UpdateTaskProgressAsync(
        string taskId,
        double progressPercent,
        string? message = null,
        Dictionary<string, object>? data = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a batch of tasks to be executed together
    /// </summary>
    /// <param name="tasks">Tasks to batch</param>
    /// <param name="parallel">Whether to execute tasks in parallel</param>
    /// <param name="failFast">Whether to cancel remaining tasks if one fails</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Batch execution result</returns>
    Task<TaskBatchResult> ExecuteTaskBatchAsync(
        IEnumerable<AgentTask> tasks,
        bool parallel = true,
        bool failFast = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Schedules a task for future execution
    /// </summary>
    /// <param name="task">Task to schedule</param>
    /// <param name="scheduledFor">When to execute the task</param>
    /// <param name="recurring">Recurring schedule pattern (optional)</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Scheduled task</returns>
    Task<AgentTask> ScheduleTaskAsync(
        AgentTask task,
        DateTimeOffset scheduledFor,
        string? recurring = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets task queue statistics
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Queue statistics</returns>
    Task<TaskQueueStatistics> GetQueueStatisticsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Estimates task execution time based on historical data
    /// </summary>
    /// <param name="task">Task to estimate</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Estimated execution time in milliseconds</returns>
    Task<long> EstimateTaskExecutionTimeAsync(AgentTask task, CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when a task status changes
    /// </summary>
    event EventHandler<TaskStatusChangedEventArgs> TaskStatusChanged;

    /// <summary>
    /// Event fired when task progress is updated
    /// </summary>
    event EventHandler<TaskProgressUpdatedEventArgs> TaskProgressUpdated;

    /// <summary>
    /// Event fired when a task completes
    /// </summary>
    event EventHandler<TaskCompletedEventArgs> TaskCompleted;

    /// <summary>
    /// Event fired when a task fails
    /// </summary>
    event EventHandler<TaskFailedEventArgs> TaskFailed;
}

/// <summary>
/// Task execution record for history tracking
/// </summary>
public sealed record TaskExecutionRecord
{
    /// <summary>
    /// Execution ID
    /// </summary>
    public required string ExecutionId { get; init; }

    /// <summary>
    /// Task ID
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// Agent instance that executed the task
    /// </summary>
    public required string InstanceId { get; init; }

    /// <summary>
    /// Execution status
    /// </summary>
    public TaskStatus Status { get; init; }

    /// <summary>
    /// Execution start time
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Execution completion time
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Execution duration in milliseconds
    /// </summary>
    public long? DurationMs { get; init; }

    /// <summary>
    /// Resource usage during execution
    /// </summary>
    public AgentResourceUsage? ResourceUsage { get; init; }

    /// <summary>
    /// Error information if failed
    /// </summary>
    public AgentError? Error { get; init; }

    /// <summary>
    /// Retry attempt number
    /// </summary>
    public int RetryAttempt { get; init; } = 0;

    /// <summary>
    /// Exit code (for process-based executions)
    /// </summary>
    public int? ExitCode { get; init; }

    /// <summary>
    /// User ID that executed the task
    /// </summary>
    public required string UserId { get; init; }
}

/// <summary>
/// Task batch execution result
/// </summary>
public sealed record TaskBatchResult
{
    /// <summary>
    /// Batch ID for tracking
    /// </summary>
    public required string BatchId { get; init; }

    /// <summary>
    /// Overall batch status
    /// </summary>
    public TaskStatus Status { get; init; }

    /// <summary>
    /// Individual task results
    /// </summary>
    public IReadOnlyList<AgentTask> TaskResults { get; init; } = Array.Empty<AgentTask>();

    /// <summary>
    /// Number of successful tasks
    /// </summary>
    public int SuccessCount { get; init; }

    /// <summary>
    /// Number of failed tasks
    /// </summary>
    public int FailureCount { get; init; }

    /// <summary>
    /// Number of cancelled tasks
    /// </summary>
    public int CancelledCount { get; init; }

    /// <summary>
    /// Batch start time
    /// </summary>
    public DateTimeOffset StartedAt { get; init; }

    /// <summary>
    /// Batch completion time
    /// </summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>
    /// Total execution duration in milliseconds
    /// </summary>
    public long? TotalDurationMs { get; init; }

    /// <summary>
    /// Batch error if any
    /// </summary>
    public AgentError? Error { get; init; }
}

/// <summary>
/// Task queue statistics
/// </summary>
public sealed record TaskQueueStatistics
{
    /// <summary>
    /// Number of pending tasks
    /// </summary>
    public long PendingTasks { get; init; }

    /// <summary>
    /// Number of queued tasks
    /// </summary>
    public long QueuedTasks { get; init; }

    /// <summary>
    /// Number of running tasks
    /// </summary>
    public long RunningTasks { get; init; }

    /// <summary>
    /// Number of completed tasks today
    /// </summary>
    public long CompletedToday { get; init; }

    /// <summary>
    /// Number of failed tasks today
    /// </summary>
    public long FailedToday { get; init; }

    /// <summary>
    /// Average queue wait time in milliseconds
    /// </summary>
    public double AverageQueueWaitTimeMs { get; init; }

    /// <summary>
    /// Average execution time in milliseconds
    /// </summary>
    public double AverageExecutionTimeMs { get; init; }

    /// <summary>
    /// Current throughput (tasks per minute)
    /// </summary>
    public double ThroughputPerMinute { get; init; }

    /// <summary>
    /// Error rate percentage (0-100)
    /// </summary>
    public double ErrorRate { get; init; }

    /// <summary>
    /// Statistics timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for task status changed event
/// </summary>
public class TaskStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// Task that changed status
    /// </summary>
    public required AgentTask Task { get; init; }

    /// <summary>
    /// Previous task status
    /// </summary>
    public TaskStatus PreviousStatus { get; init; }

    /// <summary>
    /// Status change timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for task progress updated event
/// </summary>
public class TaskProgressUpdatedEventArgs : EventArgs
{
    /// <summary>
    /// Task that was updated
    /// </summary>
    public required AgentTask Task { get; init; }

    /// <summary>
    /// Previous progress percentage
    /// </summary>
    public double PreviousProgress { get; init; }

    /// <summary>
    /// Progress update timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for task completed event
/// </summary>
public class TaskCompletedEventArgs : EventArgs
{
    /// <summary>
    /// Completed task
    /// </summary>
    public required AgentTask Task { get; init; }

    /// <summary>
    /// Task result
    /// </summary>
    public TaskResult? Result { get; init; }

    /// <summary>
    /// Completion timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for task failed event
/// </summary>
public class TaskFailedEventArgs : EventArgs
{
    /// <summary>
    /// Failed task
    /// </summary>
    public required AgentTask Task { get; init; }

    /// <summary>
    /// Error that occurred
    /// </summary>
    public required AgentError Error { get; init; }

    /// <summary>
    /// Whether the task will be retried
    /// </summary>
    public bool WillRetry { get; init; }

    /// <summary>
    /// Failure timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}