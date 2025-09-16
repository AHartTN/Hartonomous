using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hartonomous.AgentClient.Models;

/// <summary>
/// Represents a task to be executed by an agent
/// </summary>
public sealed record AgentTask
{
    /// <summary>
    /// Unique task identifier
    /// </summary>
    [JsonPropertyName("taskId")]
    public required string TaskId { get; init; }

    /// <summary>
    /// Task name for display
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Task description
    /// </summary>
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Task type/category
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    /// <summary>
    /// Task priority (1-10, 10 being highest)
    /// </summary>
    [JsonPropertyName("priority")]
    public int Priority { get; init; } = 5;

    /// <summary>
    /// Current task status
    /// </summary>
    [JsonPropertyName("status")]
    public TaskStatus Status { get; set; } = TaskStatus.Pending;

    /// <summary>
    /// Target agent ID to execute this task
    /// </summary>
    [JsonPropertyName("agentId")]
    public string? AgentId { get; set; }

    /// <summary>
    /// Agent instance ID if assigned to specific instance
    /// </summary>
    [JsonPropertyName("instanceId")]
    public string? InstanceId { get; set; }

    /// <summary>
    /// Task input data
    /// </summary>
    [JsonPropertyName("input")]
    public Dictionary<string, object> Input { get; init; } = new();

    /// <summary>
    /// Task output data
    /// </summary>
    [JsonPropertyName("output")]
    public Dictionary<string, object> Output { get; set; } = new();

    /// <summary>
    /// Task configuration and parameters
    /// </summary>
    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; init; } = new();

    /// <summary>
    /// Task execution timeout in seconds
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// Maximum retry attempts
    /// </summary>
    [JsonPropertyName("maxRetries")]
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// Current retry count
    /// </summary>
    [JsonPropertyName("retryCount")]
    public int RetryCount { get; set; } = 0;

    /// <summary>
    /// Task dependencies (must complete before this task can run)
    /// </summary>
    [JsonPropertyName("dependencies")]
    public IReadOnlyList<string> Dependencies { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Task tags for categorization and filtering
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Task execution context
    /// </summary>
    [JsonPropertyName("context")]
    public TaskExecutionContext? Context { get; set; }

    /// <summary>
    /// Task result information
    /// </summary>
    [JsonPropertyName("result")]
    public TaskResult? Result { get; set; }

    /// <summary>
    /// Task error information if failed
    /// </summary>
    [JsonPropertyName("error")]
    public AgentError? Error { get; set; }

    /// <summary>
    /// Progress percentage (0-100)
    /// </summary>
    [JsonPropertyName("progressPercent")]
    public double ProgressPercent { get; set; } = 0;

    /// <summary>
    /// Progress status message
    /// </summary>
    [JsonPropertyName("progressMessage")]
    public string? ProgressMessage { get; set; }

    /// <summary>
    /// Task creation time
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Task last update time
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Task start time
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Task completion time
    /// </summary>
    [JsonPropertyName("completedAt")]
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// User ID that created this task
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Scheduled execution time (null for immediate execution)
    /// </summary>
    [JsonPropertyName("scheduledFor")]
    public DateTimeOffset? ScheduledFor { get; init; }

    /// <summary>
    /// Whether this task can be cancelled
    /// </summary>
    [JsonPropertyName("cancellable")]
    public bool Cancellable { get; init; } = true;
}

/// <summary>
/// Task status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TaskStatus
{
    /// <summary>
    /// Task is waiting to be executed
    /// </summary>
    Pending,

    /// <summary>
    /// Task is queued for execution
    /// </summary>
    Queued,

    /// <summary>
    /// Task is currently running
    /// </summary>
    Running,

    /// <summary>
    /// Task completed successfully
    /// </summary>
    Completed,

    /// <summary>
    /// Task failed with error
    /// </summary>
    Failed,

    /// <summary>
    /// Task was cancelled
    /// </summary>
    Cancelled,

    /// <summary>
    /// Task timed out
    /// </summary>
    TimedOut,

    /// <summary>
    /// Task is paused
    /// </summary>
    Paused,

    /// <summary>
    /// Task is being retried
    /// </summary>
    Retrying
}

/// <summary>
/// Task execution context
/// </summary>
public sealed record TaskExecutionContext
{
    /// <summary>
    /// Execution ID for tracking
    /// </summary>
    [JsonPropertyName("executionId")]
    public required string ExecutionId { get; init; }

    /// <summary>
    /// Working directory for task execution
    /// </summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Environment variables
    /// </summary>
    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; init; } = new();

    /// <summary>
    /// Resource limits for this execution
    /// </summary>
    [JsonPropertyName("resourceLimits")]
    public AgentResourceRequirements? ResourceLimits { get; init; }

    /// <summary>
    /// Security context
    /// </summary>
    [JsonPropertyName("securityContext")]
    public SecurityContext? SecurityContext { get; init; }

    /// <summary>
    /// Execution mode
    /// </summary>
    [JsonPropertyName("executionMode")]
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.Normal;

    /// <summary>
    /// Parent task ID if this is a subtask
    /// </summary>
    [JsonPropertyName("parentTaskId")]
    public string? ParentTaskId { get; init; }

    /// <summary>
    /// Correlation ID for tracing across services
    /// </summary>
    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }
}

/// <summary>
/// Security context for task execution
/// </summary>
public sealed record SecurityContext
{
    /// <summary>
    /// User identity for execution
    /// </summary>
    [JsonPropertyName("userIdentity")]
    public string? UserIdentity { get; init; }

    /// <summary>
    /// Security permissions
    /// </summary>
    [JsonPropertyName("permissions")]
    public IReadOnlyList<string> Permissions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Security constraints
    /// </summary>
    [JsonPropertyName("constraints")]
    public Dictionary<string, object> Constraints { get; init; } = new();

    /// <summary>
    /// Trust level for this execution
    /// </summary>
    [JsonPropertyName("trustLevel")]
    public TrustLevel TrustLevel { get; init; } = TrustLevel.Untrusted;
}

/// <summary>
/// Task execution mode enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExecutionMode
{
    /// <summary>
    /// Normal execution mode
    /// </summary>
    Normal,

    /// <summary>
    /// Debug mode with additional logging
    /// </summary>
    Debug,

    /// <summary>
    /// Dry run mode (simulate without actual changes)
    /// </summary>
    DryRun,

    /// <summary>
    /// Interactive mode requiring user input
    /// </summary>
    Interactive,

    /// <summary>
    /// Background batch execution
    /// </summary>
    Batch
}

/// <summary>
/// Task execution result
/// </summary>
public sealed record TaskResult
{
    /// <summary>
    /// Whether the task was successful
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Result data
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; init; } = new();

    /// <summary>
    /// Result message
    /// </summary>
    [JsonPropertyName("message")]
    public string? Message { get; init; }

    /// <summary>
    /// Execution duration in milliseconds
    /// </summary>
    [JsonPropertyName("durationMs")]
    public long DurationMs { get; init; }

    /// <summary>
    /// Resource usage during execution
    /// </summary>
    [JsonPropertyName("resourceUsage")]
    public AgentResourceUsage? ResourceUsage { get; init; }

    /// <summary>
    /// Output files produced by the task
    /// </summary>
    [JsonPropertyName("outputFiles")]
    public IReadOnlyList<string> OutputFiles { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Log entries from task execution
    /// </summary>
    [JsonPropertyName("logEntries")]
    public IReadOnlyList<LogEntry> LogEntries { get; init; } = Array.Empty<LogEntry>();

    /// <summary>
    /// Metrics collected during execution
    /// </summary>
    [JsonPropertyName("metrics")]
    public Dictionary<string, object> Metrics { get; init; } = new();
}

/// <summary>
/// Log entry from task execution
/// </summary>
public sealed record LogEntry
{
    /// <summary>
    /// Log timestamp
    /// </summary>
    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Log level
    /// </summary>
    [JsonPropertyName("level")]
    public LogLevel Level { get; init; }

    /// <summary>
    /// Log message
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Log category/source
    /// </summary>
    [JsonPropertyName("category")]
    public string? Category { get; init; }

    /// <summary>
    /// Additional log data
    /// </summary>
    [JsonPropertyName("data")]
    public Dictionary<string, object> Data { get; init; } = new();
}

/// <summary>
/// Log level enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogLevel
{
    /// <summary>
    /// Trace level logging
    /// </summary>
    Trace,

    /// <summary>
    /// Debug level logging
    /// </summary>
    Debug,

    /// <summary>
    /// Information level logging
    /// </summary>
    Information,

    /// <summary>
    /// Warning level logging
    /// </summary>
    Warning,

    /// <summary>
    /// Error level logging
    /// </summary>
    Error,

    /// <summary>
    /// Critical level logging
    /// </summary>
    Critical
}