/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the AgentInstance models for running agent lifecycle management,
 * supporting distributed agent execution with resource monitoring and health tracking.
 */

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hartonomous.AgentClient.Models;

/// <summary>
/// Represents a running instance of an agent
/// </summary>
public sealed record AgentInstance
{
    /// <summary>
    /// Unique instance identifier
    /// </summary>
    [JsonPropertyName("instanceId")]
    public required string InstanceId { get; init; }

    /// <summary>
    /// Agent definition this instance is based on
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    /// <summary>
    /// Agent name for display
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Agent version
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Current instance status
    /// </summary>
    [JsonPropertyName("status")]
    public AgentInstanceStatus Status { get; set; } = AgentInstanceStatus.Stopped;

    /// <summary>
    /// Process ID if running in separate process
    /// </summary>
    [JsonPropertyName("processId")]
    public int? ProcessId { get; set; }

    /// <summary>
    /// Container ID if running in container
    /// </summary>
    [JsonPropertyName("containerId")]
    public string? ContainerId { get; set; }

    /// <summary>
    /// Working directory path
    /// </summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    /// <summary>
    /// Configuration values for this instance
    /// </summary>
    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; set; } = new();

    /// <summary>
    /// Environment variables
    /// </summary>
    [JsonPropertyName("environment")]
    public Dictionary<string, string> Environment { get; set; } = new();

    /// <summary>
    /// Resource usage statistics
    /// </summary>
    [JsonPropertyName("resourceUsage")]
    public AgentResourceUsage? ResourceUsage { get; set; }

    /// <summary>
    /// Execution metrics
    /// </summary>
    [JsonPropertyName("metrics")]
    public AgentMetrics? Metrics { get; set; }

    /// <summary>
    /// Error information if failed
    /// </summary>
    [JsonPropertyName("error")]
    public AgentError? Error { get; set; }

    /// <summary>
    /// Instance creation time
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last status update time
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Instance start time
    /// </summary>
    [JsonPropertyName("startedAt")]
    public DateTimeOffset? StartedAt { get; set; }

    /// <summary>
    /// Instance stop time
    /// </summary>
    [JsonPropertyName("stoppedAt")]
    public DateTimeOffset? StoppedAt { get; set; }

    /// <summary>
    /// User ID that owns this instance
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Tags for categorization
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Agent instance status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentInstanceStatus
{
    /// <summary>
    /// Agent is stopped
    /// </summary>
    Stopped,

    /// <summary>
    /// Agent is starting up
    /// </summary>
    Starting,

    /// <summary>
    /// Agent is running normally
    /// </summary>
    Running,

    /// <summary>
    /// Agent is paused
    /// </summary>
    Paused,

    /// <summary>
    /// Agent is stopping
    /// </summary>
    Stopping,

    /// <summary>
    /// Agent has failed
    /// </summary>
    Failed,

    /// <summary>
    /// Agent is unhealthy but still running
    /// </summary>
    Unhealthy,

    /// <summary>
    /// Agent status is unknown
    /// </summary>
    Unknown
}

/// <summary>
/// Agent resource usage statistics
/// </summary>
public sealed record AgentResourceUsage
{
    /// <summary>
    /// CPU usage percentage (0-100)
    /// </summary>
    [JsonPropertyName("cpuUsagePercent")]
    public double CpuUsagePercent { get; set; }

    /// <summary>
    /// Memory usage in MB
    /// </summary>
    [JsonPropertyName("memoryUsageMb")]
    public long MemoryUsageMb { get; set; }

    /// <summary>
    /// Disk usage in MB
    /// </summary>
    [JsonPropertyName("diskUsageMb")]
    public long DiskUsageMb { get; set; }

    /// <summary>
    /// Network bytes sent
    /// </summary>
    [JsonPropertyName("networkBytesSent")]
    public long NetworkBytesSent { get; set; }

    /// <summary>
    /// Network bytes received
    /// </summary>
    [JsonPropertyName("networkBytesReceived")]
    public long NetworkBytesReceived { get; set; }

    /// <summary>
    /// Number of file handles open
    /// </summary>
    [JsonPropertyName("fileHandles")]
    public int FileHandles { get; set; }

    /// <summary>
    /// Number of threads
    /// </summary>
    [JsonPropertyName("threadCount")]
    public int ThreadCount { get; set; }

    /// <summary>
    /// Last measurement time
    /// </summary>
    [JsonPropertyName("measuredAt")]
    public DateTimeOffset MeasuredAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Agent execution metrics
/// </summary>
public sealed record AgentMetrics
{
    /// <summary>
    /// Total number of tasks executed
    /// </summary>
    [JsonPropertyName("tasksExecuted")]
    public long TasksExecuted { get; set; }

    /// <summary>
    /// Number of successful task executions
    /// </summary>
    [JsonPropertyName("tasksSucceeded")]
    public long TasksSucceeded { get; set; }

    /// <summary>
    /// Number of failed task executions
    /// </summary>
    [JsonPropertyName("tasksFailed")]
    public long TasksFailed { get; set; }

    /// <summary>
    /// Average task execution time in milliseconds
    /// </summary>
    [JsonPropertyName("averageExecutionTimeMs")]
    public double AverageExecutionTimeMs { get; set; }

    /// <summary>
    /// Total uptime in seconds
    /// </summary>
    [JsonPropertyName("uptimeSeconds")]
    public long UptimeSeconds { get; set; }

    /// <summary>
    /// Number of restarts
    /// </summary>
    [JsonPropertyName("restartCount")]
    public int RestartCount { get; set; }

    /// <summary>
    /// Last health check time
    /// </summary>
    [JsonPropertyName("lastHealthCheck")]
    public DateTimeOffset? LastHealthCheck { get; set; }

    /// <summary>
    /// Health check status
    /// </summary>
    [JsonPropertyName("healthStatus")]
    public HealthStatus HealthStatus { get; set; } = HealthStatus.Unknown;

    /// <summary>
    /// Custom metrics dictionary
    /// </summary>
    [JsonPropertyName("customMetrics")]
    public Dictionary<string, object> CustomMetrics { get; set; } = new();
}

/// <summary>
/// Health status enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum HealthStatus
{
    /// <summary>
    /// Health status is unknown
    /// </summary>
    Unknown,

    /// <summary>
    /// Agent is healthy
    /// </summary>
    Healthy,

    /// <summary>
    /// Agent is degraded but functional
    /// </summary>
    Degraded,

    /// <summary>
    /// Agent is unhealthy
    /// </summary>
    Unhealthy
}

/// <summary>
/// Agent error information
/// </summary>
public sealed record AgentError
{
    /// <summary>
    /// Error code
    /// </summary>
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    /// <summary>
    /// Error message
    /// </summary>
    [JsonPropertyName("message")]
    public required string Message { get; init; }

    /// <summary>
    /// Detailed error description
    /// </summary>
    [JsonPropertyName("details")]
    public string? Details { get; init; }

    /// <summary>
    /// Stack trace if available
    /// </summary>
    [JsonPropertyName("stackTrace")]
    public string? StackTrace { get; init; }

    /// <summary>
    /// Inner exception information
    /// </summary>
    [JsonPropertyName("innerError")]
    public AgentError? InnerError { get; init; }

    /// <summary>
    /// Error occurrence time
    /// </summary>
    [JsonPropertyName("occurredAt")]
    public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Error severity level
    /// </summary>
    [JsonPropertyName("severity")]
    public ErrorSeverity Severity { get; init; } = ErrorSeverity.Error;
}

/// <summary>
/// Error severity enumeration
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ErrorSeverity
{
    /// <summary>
    /// Informational message
    /// </summary>
    Info,

    /// <summary>
    /// Warning message
    /// </summary>
    Warning,

    /// <summary>
    /// Error occurred but agent can continue
    /// </summary>
    Error,

    /// <summary>
    /// Critical error requiring immediate attention
    /// </summary>
    Critical,

    /// <summary>
    /// Fatal error causing agent termination
    /// </summary>
    Fatal
}