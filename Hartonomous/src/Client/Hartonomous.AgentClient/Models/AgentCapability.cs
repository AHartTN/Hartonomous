using System.Text.Json.Serialization;

namespace Hartonomous.AgentClient.Models;

/// <summary>
/// Represents a capability that an agent can provide
/// </summary>
public sealed record AgentCapability
{
    /// <summary>
    /// Unique capability identifier
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>
    /// Capability display name
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Capability version
    /// </summary>
    [JsonPropertyName("version")]
    public required string Version { get; init; }

    /// <summary>
    /// Capability description
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Capability category
    /// </summary>
    [JsonPropertyName("category")]
    public required string Category { get; init; }

    /// <summary>
    /// Input schema for this capability
    /// </summary>
    [JsonPropertyName("inputSchema")]
    public Dictionary<string, object>? InputSchema { get; init; }

    /// <summary>
    /// Output schema for this capability
    /// </summary>
    [JsonPropertyName("outputSchema")]
    public Dictionary<string, object>? OutputSchema { get; init; }

    /// <summary>
    /// Configuration schema for this capability
    /// </summary>
    [JsonPropertyName("configurationSchema")]
    public Dictionary<string, object>? ConfigurationSchema { get; init; }

    /// <summary>
    /// Required permissions to use this capability
    /// </summary>
    [JsonPropertyName("requiredPermissions")]
    public IReadOnlyList<string> RequiredPermissions { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Capability tags for discovery
    /// </summary>
    [JsonPropertyName("tags")]
    public IReadOnlyList<string> Tags { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Whether this capability is enabled
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Examples of using this capability
    /// </summary>
    [JsonPropertyName("examples")]
    public IReadOnlyList<CapabilityExample> Examples { get; init; } = Array.Empty<CapabilityExample>();

    /// <summary>
    /// Performance characteristics
    /// </summary>
    [JsonPropertyName("performance")]
    public CapabilityPerformance? Performance { get; init; }

    /// <summary>
    /// Usage statistics
    /// </summary>
    [JsonPropertyName("usage")]
    public CapabilityUsage? Usage { get; set; }

    /// <summary>
    /// Registration timestamp
    /// </summary>
    [JsonPropertyName("registeredAt")]
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Example usage of a capability
/// </summary>
public sealed record CapabilityExample
{
    /// <summary>
    /// Example name
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Example description
    /// </summary>
    [JsonPropertyName("description")]
    public required string Description { get; init; }

    /// <summary>
    /// Example input data
    /// </summary>
    [JsonPropertyName("input")]
    public Dictionary<string, object> Input { get; init; } = new();

    /// <summary>
    /// Expected output data
    /// </summary>
    [JsonPropertyName("expectedOutput")]
    public Dictionary<string, object> ExpectedOutput { get; init; } = new();

    /// <summary>
    /// Configuration for this example
    /// </summary>
    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; init; } = new();
}

/// <summary>
/// Performance characteristics of a capability
/// </summary>
public sealed record CapabilityPerformance
{
    /// <summary>
    /// Average execution time in milliseconds
    /// </summary>
    [JsonPropertyName("averageExecutionTimeMs")]
    public double AverageExecutionTimeMs { get; init; }

    /// <summary>
    /// Maximum execution time in milliseconds
    /// </summary>
    [JsonPropertyName("maxExecutionTimeMs")]
    public double MaxExecutionTimeMs { get; init; }

    /// <summary>
    /// Minimum execution time in milliseconds
    /// </summary>
    [JsonPropertyName("minExecutionTimeMs")]
    public double MinExecutionTimeMs { get; init; }

    /// <summary>
    /// Memory usage in MB
    /// </summary>
    [JsonPropertyName("memoryUsageMb")]
    public double MemoryUsageMb { get; init; }

    /// <summary>
    /// CPU intensity (1-10 scale)
    /// </summary>
    [JsonPropertyName("cpuIntensity")]
    public int CpuIntensity { get; init; } = 5;

    /// <summary>
    /// IO intensity (1-10 scale)
    /// </summary>
    [JsonPropertyName("ioIntensity")]
    public int IoIntensity { get; init; } = 5;

    /// <summary>
    /// Network intensity (1-10 scale)
    /// </summary>
    [JsonPropertyName("networkIntensity")]
    public int NetworkIntensity { get; init; } = 1;

    /// <summary>
    /// Scalability factor (concurrent executions supported)
    /// </summary>
    [JsonPropertyName("concurrencyLevel")]
    public int ConcurrencyLevel { get; init; } = 1;

    /// <summary>
    /// Reliability score (0-100)
    /// </summary>
    [JsonPropertyName("reliabilityScore")]
    public double ReliabilityScore { get; init; } = 100.0;
}

/// <summary>
/// Usage statistics for a capability
/// </summary>
public sealed record CapabilityUsage
{
    /// <summary>
    /// Total number of executions
    /// </summary>
    [JsonPropertyName("totalExecutions")]
    public long TotalExecutions { get; set; } = 0;

    /// <summary>
    /// Number of successful executions
    /// </summary>
    [JsonPropertyName("successfulExecutions")]
    public long SuccessfulExecutions { get; set; } = 0;

    /// <summary>
    /// Number of failed executions
    /// </summary>
    [JsonPropertyName("failedExecutions")]
    public long FailedExecutions { get; set; } = 0;

    /// <summary>
    /// Last execution timestamp
    /// </summary>
    [JsonPropertyName("lastExecutedAt")]
    public DateTimeOffset? LastExecutedAt { get; set; }

    /// <summary>
    /// Average execution duration in milliseconds
    /// </summary>
    [JsonPropertyName("averageDurationMs")]
    public double AverageDurationMs { get; set; } = 0;

    /// <summary>
    /// Error rate percentage (0-100)
    /// </summary>
    [JsonPropertyName("errorRate")]
    public double ErrorRate { get; set; } = 0;

    /// <summary>
    /// Usage frequency (executions per day)
    /// </summary>
    [JsonPropertyName("dailyFrequency")]
    public double DailyFrequency { get; set; } = 0;

    /// <summary>
    /// Peak usage periods
    /// </summary>
    [JsonPropertyName("peakUsagePeriods")]
    public IReadOnlyList<UsagePeriod> PeakUsagePeriods { get; set; } = Array.Empty<UsagePeriod>();
}

/// <summary>
/// Usage period statistics
/// </summary>
public sealed record UsagePeriod
{
    /// <summary>
    /// Period start time
    /// </summary>
    [JsonPropertyName("startTime")]
    public TimeSpan StartTime { get; init; }

    /// <summary>
    /// Period end time
    /// </summary>
    [JsonPropertyName("endTime")]
    public TimeSpan EndTime { get; init; }

    /// <summary>
    /// Average executions during this period
    /// </summary>
    [JsonPropertyName("averageExecutions")]
    public double AverageExecutions { get; init; }

    /// <summary>
    /// Days of week this period applies to (0 = Sunday)
    /// </summary>
    [JsonPropertyName("daysOfWeek")]
    public IReadOnlyList<DayOfWeek> DaysOfWeek { get; init; } = Array.Empty<DayOfWeek>();
}

/// <summary>
/// Capability registry entry for discovery and management
/// </summary>
public sealed record CapabilityRegistryEntry
{
    /// <summary>
    /// Capability information
    /// </summary>
    [JsonPropertyName("capability")]
    public required AgentCapability Capability { get; init; }

    /// <summary>
    /// Agent ID that provides this capability
    /// </summary>
    [JsonPropertyName("agentId")]
    public required string AgentId { get; init; }

    /// <summary>
    /// Agent instance ID if specific to an instance
    /// </summary>
    [JsonPropertyName("instanceId")]
    public string? InstanceId { get; init; }

    /// <summary>
    /// Endpoint URL for accessing this capability
    /// </summary>
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; init; }

    /// <summary>
    /// Health status of this capability
    /// </summary>
    [JsonPropertyName("healthStatus")]
    public HealthStatus HealthStatus { get; set; } = HealthStatus.Unknown;

    /// <summary>
    /// Last health check timestamp
    /// </summary>
    [JsonPropertyName("lastHealthCheck")]
    public DateTimeOffset? LastHealthCheck { get; set; }

    /// <summary>
    /// Registration timestamp
    /// </summary>
    [JsonPropertyName("registeredAt")]
    public DateTimeOffset RegisteredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// User ID that registered this capability
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Whether this capability is currently available
    /// </summary>
    [JsonPropertyName("available")]
    public bool Available { get; set; } = true;
}

/// <summary>
/// Capability execution request
/// </summary>
public sealed record CapabilityExecutionRequest
{
    /// <summary>
    /// Request ID for tracking
    /// </summary>
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    /// <summary>
    /// Capability ID to execute
    /// </summary>
    [JsonPropertyName("capabilityId")]
    public required string CapabilityId { get; init; }

    /// <summary>
    /// Input data for the capability
    /// </summary>
    [JsonPropertyName("input")]
    public Dictionary<string, object> Input { get; init; } = new();

    /// <summary>
    /// Configuration overrides
    /// </summary>
    [JsonPropertyName("configuration")]
    public Dictionary<string, object> Configuration { get; init; } = new();

    /// <summary>
    /// Execution context
    /// </summary>
    [JsonPropertyName("context")]
    public TaskExecutionContext? Context { get; init; }

    /// <summary>
    /// Timeout for this execution in seconds
    /// </summary>
    [JsonPropertyName("timeoutSeconds")]
    public int TimeoutSeconds { get; init; } = 300;

    /// <summary>
    /// User ID making the request
    /// </summary>
    [JsonPropertyName("userId")]
    public required string UserId { get; init; }

    /// <summary>
    /// Request creation timestamp
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Capability execution response
/// </summary>
public sealed record CapabilityExecutionResponse
{
    /// <summary>
    /// Request ID this response is for
    /// </summary>
    [JsonPropertyName("requestId")]
    public required string RequestId { get; init; }

    /// <summary>
    /// Whether the execution was successful
    /// </summary>
    [JsonPropertyName("success")]
    public bool Success { get; init; }

    /// <summary>
    /// Output data from the capability
    /// </summary>
    [JsonPropertyName("output")]
    public Dictionary<string, object> Output { get; init; } = new();

    /// <summary>
    /// Error information if failed
    /// </summary>
    [JsonPropertyName("error")]
    public AgentError? Error { get; init; }

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
    /// Response creation timestamp
    /// </summary>
    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}