using Hartonomous.AgentClient.Models;

namespace Hartonomous.AgentClient.Interfaces;

/// <summary>
/// Interface for telemetry and monitoring system
/// </summary>
public interface ITelemetryService
{
    /// <summary>
    /// Records a metric value
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Metric value</param>
    /// <param name="tags">Optional tags for the metric</param>
    /// <param name="timestamp">Optional timestamp (current time if not specified)</param>
    void RecordMetric(string name, double value, Dictionary<string, string>? tags = null, DateTimeOffset? timestamp = null);

    /// <summary>
    /// Increments a counter metric
    /// </summary>
    /// <param name="name">Counter name</param>
    /// <param name="increment">Amount to increment (default 1)</param>
    /// <param name="tags">Optional tags for the counter</param>
    void IncrementCounter(string name, double increment = 1.0, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Records a histogram value (for measuring distributions)
    /// </summary>
    /// <param name="name">Histogram name</param>
    /// <param name="value">Value to record</param>
    /// <param name="tags">Optional tags for the histogram</param>
    void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Records a gauge value (for measuring current state)
    /// </summary>
    /// <param name="name">Gauge name</param>
    /// <param name="value">Current value</param>
    /// <param name="tags">Optional tags for the gauge</param>
    void RecordGauge(string name, double value, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Starts measuring execution time
    /// </summary>
    /// <param name="name">Timer name</param>
    /// <param name="tags">Optional tags for the timer</param>
    /// <returns>Disposable timer that records duration when disposed</returns>
    IDisposable StartTimer(string name, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Records execution time
    /// </summary>
    /// <param name="name">Timer name</param>
    /// <param name="duration">Duration to record</param>
    /// <param name="tags">Optional tags for the timer</param>
    void RecordDuration(string name, TimeSpan duration, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Records an event
    /// </summary>
    /// <param name="name">Event name</param>
    /// <param name="properties">Event properties</param>
    /// <param name="measurements">Event measurements</param>
    /// <param name="timestamp">Optional timestamp (current time if not specified)</param>
    void RecordEvent(string name, Dictionary<string, string>? properties = null, Dictionary<string, double>? measurements = null, DateTimeOffset? timestamp = null);

    /// <summary>
    /// Records an exception
    /// </summary>
    /// <param name="exception">Exception to record</param>
    /// <param name="properties">Additional properties</param>
    /// <param name="measurements">Additional measurements</param>
    void RecordException(Exception exception, Dictionary<string, string>? properties = null, Dictionary<string, double>? measurements = null);

    /// <summary>
    /// Records agent instance metrics
    /// </summary>
    /// <param name="instanceId">Agent instance ID</param>
    /// <param name="metrics">Metrics to record</param>
    void RecordAgentMetrics(string instanceId, AgentMetrics metrics);

    /// <summary>
    /// Records agent resource usage
    /// </summary>
    /// <param name="instanceId">Agent instance ID</param>
    /// <param name="resourceUsage">Resource usage to record</param>
    void RecordResourceUsage(string instanceId, AgentResourceUsage resourceUsage);

    /// <summary>
    /// Records task execution metrics
    /// </summary>
    /// <param name="taskId">Task ID</param>
    /// <param name="result">Task result</param>
    void RecordTaskMetrics(string taskId, TaskResult result);

    /// <summary>
    /// Records capability execution metrics
    /// </summary>
    /// <param name="capabilityId">Capability ID</param>
    /// <param name="response">Capability execution response</param>
    void RecordCapabilityMetrics(string capabilityId, CapabilityExecutionResponse response);

    /// <summary>
    /// Gets metrics for a specific agent instance
    /// </summary>
    /// <param name="instanceId">Agent instance ID</param>
    /// <param name="timeRange">Time range to query</param>
    /// <param name="metricNames">Optional specific metrics to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Time series metrics data</returns>
    Task<IEnumerable<TimeSeriesMetric>> GetAgentMetricsAsync(
        string instanceId,
        TimeRange timeRange,
        IEnumerable<string>? metricNames = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets aggregated metrics across all agents
    /// </summary>
    /// <param name="timeRange">Time range to query</param>
    /// <param name="groupBy">Optional grouping criteria</param>
    /// <param name="filters">Optional filters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Aggregated metrics data</returns>
    Task<IEnumerable<AggregatedMetric>> GetAggregatedMetricsAsync(
        TimeRange timeRange,
        string? groupBy = null,
        Dictionary<string, string>? filters = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets real-time metrics dashboard data
    /// </summary>
    /// <param name="dashboardId">Optional specific dashboard ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Dashboard data</returns>
    Task<DashboardData> GetDashboardDataAsync(string? dashboardId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a custom alert rule
    /// </summary>
    /// <param name="alert">Alert rule definition</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Created alert rule</returns>
    Task<AlertRule> CreateAlertAsync(AlertRule alert, CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an alert rule
    /// </summary>
    /// <param name="alertId">Alert rule ID</param>
    /// <param name="updates">Updates to apply</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated alert rule</returns>
    Task<AlertRule> UpdateAlertAsync(string alertId, Dictionary<string, object> updates, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes an alert rule
    /// </summary>
    /// <param name="alertId">Alert rule ID</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task DeleteAlertAsync(string alertId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all alert rules for the current user
    /// </summary>
    /// <param name="userId">Optional user ID filter</param>
    /// <param name="enabled">Optional enabled filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of alert rules</returns>
    Task<IEnumerable<AlertRule>> GetAlertsAsync(string? userId = null, bool? enabled = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets active alert instances
    /// </summary>
    /// <param name="severity">Optional severity filter</param>
    /// <param name="resolved">Optional resolved status filter</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>List of active alerts</returns>
    Task<IEnumerable<AlertInstance>> GetActiveAlertsAsync(AlertSeverity? severity = null, bool? resolved = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves an active alert
    /// </summary>
    /// <param name="alertInstanceId">Alert instance ID</param>
    /// <param name="resolution">Resolution reason</param>
    /// <param name="cancellationToken">Cancellation token</param>
    Task ResolveAlertAsync(string alertInstanceId, string? resolution = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Exports metrics data
    /// </summary>
    /// <param name="request">Export request parameters</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Export result with download information</returns>
    Task<MetricsExportResult> ExportMetricsAsync(MetricsExportRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets telemetry service health status
    /// </summary>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Service health information</returns>
    Task<ServiceHealth> GetHealthAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Event fired when an alert is triggered
    /// </summary>
    event EventHandler<AlertTriggeredEventArgs> AlertTriggered;

    /// <summary>
    /// Event fired when metrics thresholds are exceeded
    /// </summary>
    event EventHandler<MetricThresholdExceededEventArgs> MetricThresholdExceeded;
}

/// <summary>
/// Time range for metrics queries
/// </summary>
public sealed record TimeRange
{
    /// <summary>
    /// Start time (inclusive)
    /// </summary>
    public DateTimeOffset StartTime { get; init; }

    /// <summary>
    /// End time (inclusive)
    /// </summary>
    public DateTimeOffset EndTime { get; init; }

    /// <summary>
    /// Creates a time range for the last specified duration
    /// </summary>
    /// <param name="duration">Duration from now</param>
    /// <returns>Time range</returns>
    public static TimeRange Last(TimeSpan duration) => new()
    {
        StartTime = DateTimeOffset.UtcNow - duration,
        EndTime = DateTimeOffset.UtcNow
    };

    /// <summary>
    /// Creates a time range between two specific times
    /// </summary>
    /// <param name="start">Start time</param>
    /// <param name="end">End time</param>
    /// <returns>Time range</returns>
    public static TimeRange Between(DateTimeOffset start, DateTimeOffset end) => new()
    {
        StartTime = start,
        EndTime = end
    };
}

/// <summary>
/// Time series metric data point
/// </summary>
public sealed record TimeSeriesMetric
{
    /// <summary>
    /// Metric name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Data points
    /// </summary>
    public IReadOnlyList<MetricDataPoint> DataPoints { get; init; } = Array.Empty<MetricDataPoint>();

    /// <summary>
    /// Metric tags
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = new();

    /// <summary>
    /// Metric unit
    /// </summary>
    public string? Unit { get; init; }

    /// <summary>
    /// Aggregation method used
    /// </summary>
    public AggregationMethod Aggregation { get; init; } = AggregationMethod.Average;
}

/// <summary>
/// Metric data point
/// </summary>
public sealed record MetricDataPoint
{
    /// <summary>
    /// Timestamp
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Metric value
    /// </summary>
    public double Value { get; init; }

    /// <summary>
    /// Additional metadata
    /// </summary>
    public Dictionary<string, object> Metadata { get; init; } = new();
}

/// <summary>
/// Aggregated metric result
/// </summary>
public sealed record AggregatedMetric
{
    /// <summary>
    /// Metric name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Aggregated value
    /// </summary>
    public double Value { get; init; }

    /// <summary>
    /// Number of data points aggregated
    /// </summary>
    public long Count { get; init; }

    /// <summary>
    /// Minimum value
    /// </summary>
    public double Min { get; init; }

    /// <summary>
    /// Maximum value
    /// </summary>
    public double Max { get; init; }

    /// <summary>
    /// Standard deviation
    /// </summary>
    public double StandardDeviation { get; init; }

    /// <summary>
    /// Grouping criteria used
    /// </summary>
    public Dictionary<string, string> GroupBy { get; init; } = new();

    /// <summary>
    /// Time range for this aggregation
    /// </summary>
    public required TimeRange TimeRange { get; init; }
}

/// <summary>
/// Dashboard data container
/// </summary>
public sealed record DashboardData
{
    /// <summary>
    /// Dashboard ID
    /// </summary>
    public required string DashboardId { get; init; }

    /// <summary>
    /// Dashboard title
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Dashboard widgets
    /// </summary>
    public IReadOnlyList<DashboardWidget> Widgets { get; init; } = Array.Empty<DashboardWidget>();

    /// <summary>
    /// Refresh interval in seconds
    /// </summary>
    public int RefreshIntervalSeconds { get; init; } = 30;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset LastUpdated { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Dashboard widget
/// </summary>
public sealed record DashboardWidget
{
    /// <summary>
    /// Widget ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Widget title
    /// </summary>
    public required string Title { get; init; }

    /// <summary>
    /// Widget type
    /// </summary>
    public WidgetType Type { get; init; }

    /// <summary>
    /// Widget configuration
    /// </summary>
    public Dictionary<string, object> Configuration { get; init; } = new();

    /// <summary>
    /// Widget data
    /// </summary>
    public Dictionary<string, object> Data { get; init; } = new();

    /// <summary>
    /// Widget position
    /// </summary>
    public WidgetPosition Position { get; init; } = new();

    /// <summary>
    /// Widget size
    /// </summary>
    public WidgetSize Size { get; init; } = new();
}

/// <summary>
/// Alert rule definition
/// </summary>
public sealed record AlertRule
{
    /// <summary>
    /// Alert rule ID
    /// </summary>
    public string? Id { get; init; }

    /// <summary>
    /// Alert name
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Alert description
    /// </summary>
    public string? Description { get; init; }

    /// <summary>
    /// Metric query/condition
    /// </summary>
    public required string Query { get; init; }

    /// <summary>
    /// Alert threshold
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>
    /// Comparison operator
    /// </summary>
    public ComparisonOperator Operator { get; init; } = ComparisonOperator.GreaterThan;

    /// <summary>
    /// Alert severity
    /// </summary>
    public AlertSeverity Severity { get; init; } = AlertSeverity.Medium;

    /// <summary>
    /// Evaluation frequency in seconds
    /// </summary>
    public int EvaluationIntervalSeconds { get; init; } = 60;

    /// <summary>
    /// Duration threshold must be exceeded to trigger
    /// </summary>
    public int DurationSeconds { get; init; } = 300;

    /// <summary>
    /// Whether the alert is enabled
    /// </summary>
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// Notification channels
    /// </summary>
    public IReadOnlyList<string> NotificationChannels { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Alert tags
    /// </summary>
    public Dictionary<string, string> Tags { get; init; } = new();

    /// <summary>
    /// User ID that owns this alert
    /// </summary>
    public required string UserId { get; init; }

    /// <summary>
    /// Creation timestamp
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Last update timestamp
    /// </summary>
    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Alert instance (triggered alert)
/// </summary>
public sealed record AlertInstance
{
    /// <summary>
    /// Alert instance ID
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Alert rule ID
    /// </summary>
    public required string RuleId { get; init; }

    /// <summary>
    /// Alert rule name
    /// </summary>
    public required string RuleName { get; init; }

    /// <summary>
    /// Alert severity
    /// </summary>
    public AlertSeverity Severity { get; init; }

    /// <summary>
    /// Alert state
    /// </summary>
    public AlertState State { get; init; } = AlertState.Firing;

    /// <summary>
    /// Current metric value that triggered the alert
    /// </summary>
    public double CurrentValue { get; init; }

    /// <summary>
    /// Alert threshold
    /// </summary>
    public double Threshold { get; init; }

    /// <summary>
    /// Alert message
    /// </summary>
    public string? Message { get; init; }

    /// <summary>
    /// Labels/tags from the metric
    /// </summary>
    public Dictionary<string, string> Labels { get; init; } = new();

    /// <summary>
    /// When the alert was first triggered
    /// </summary>
    public DateTimeOffset TriggeredAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// When the alert was resolved (if applicable)
    /// </summary>
    public DateTimeOffset? ResolvedAt { get; init; }

    /// <summary>
    /// Resolution reason
    /// </summary>
    public string? Resolution { get; init; }

    /// <summary>
    /// Number of notifications sent
    /// </summary>
    public int NotificationCount { get; init; }

    /// <summary>
    /// Last notification sent
    /// </summary>
    public DateTimeOffset? LastNotificationSent { get; init; }
}

/// <summary>
/// Metrics export request
/// </summary>
public sealed record MetricsExportRequest
{
    /// <summary>
    /// Time range to export
    /// </summary>
    public required TimeRange TimeRange { get; init; }

    /// <summary>
    /// Metric names to export (all if not specified)
    /// </summary>
    public IEnumerable<string>? MetricNames { get; init; }

    /// <summary>
    /// Export format
    /// </summary>
    public ExportFormat Format { get; init; } = ExportFormat.Json;

    /// <summary>
    /// Filters to apply
    /// </summary>
    public Dictionary<string, string> Filters { get; init; } = new();

    /// <summary>
    /// Aggregation method
    /// </summary>
    public AggregationMethod? Aggregation { get; init; }

    /// <summary>
    /// Aggregation interval
    /// </summary>
    public TimeSpan? AggregationInterval { get; init; }

    /// <summary>
    /// Whether to include metadata
    /// </summary>
    public bool IncludeMetadata { get; init; } = true;

    /// <summary>
    /// Maximum number of data points
    /// </summary>
    public int? MaxDataPoints { get; init; }
}

/// <summary>
/// Metrics export result
/// </summary>
public sealed record MetricsExportResult
{
    /// <summary>
    /// Export ID for tracking
    /// </summary>
    public required string ExportId { get; init; }

    /// <summary>
    /// Whether export was successful
    /// </summary>
    public bool Success { get; init; }

    /// <summary>
    /// Download URL for the exported data
    /// </summary>
    public string? DownloadUrl { get; init; }

    /// <summary>
    /// Export file size in bytes
    /// </summary>
    public long FileSize { get; init; }

    /// <summary>
    /// Number of data points exported
    /// </summary>
    public long DataPointCount { get; init; }

    /// <summary>
    /// Export duration
    /// </summary>
    public TimeSpan Duration { get; init; }

    /// <summary>
    /// Error message if failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Export expiry time
    /// </summary>
    public DateTimeOffset ExpiresAt { get; init; }
}

/// <summary>
/// Service health information
/// </summary>
public sealed record ServiceHealth
{
    /// <summary>
    /// Overall health status
    /// </summary>
    public HealthStatus Status { get; init; }

    /// <summary>
    /// Health check timestamp
    /// </summary>
    public DateTimeOffset CheckedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Service uptime
    /// </summary>
    public TimeSpan Uptime { get; init; }

    /// <summary>
    /// Component health details
    /// </summary>
    public Dictionary<string, ComponentHealth> Components { get; init; } = new();

    /// <summary>
    /// Performance metrics
    /// </summary>
    public Dictionary<string, double> Metrics { get; init; } = new();

    /// <summary>
    /// Health messages
    /// </summary>
    public IReadOnlyList<string> Messages { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Component health details
/// </summary>
public sealed record ComponentHealth
{
    /// <summary>
    /// Component health status
    /// </summary>
    public HealthStatus Status { get; init; }

    /// <summary>
    /// Component response time
    /// </summary>
    public TimeSpan ResponseTime { get; init; }

    /// <summary>
    /// Component-specific details
    /// </summary>
    public Dictionary<string, object> Details { get; init; } = new();

    /// <summary>
    /// Error message if unhealthy
    /// </summary>
    public string? ErrorMessage { get; init; }
}

// Enums for telemetry service
public enum AggregationMethod { Average, Sum, Min, Max, Count, Percentile95, Percentile99 }
public enum WidgetType { LineChart, BarChart, PieChart, Gauge, Counter, Table, Heatmap }
public enum ComparisonOperator { GreaterThan, GreaterThanOrEqual, LessThan, LessThanOrEqual, Equal, NotEqual }
public enum AlertSeverity { Low, Medium, High, Critical }
public enum AlertState { Pending, Firing, Resolved }
public enum ExportFormat { Json, Csv, Parquet, Prometheus }

/// <summary>
/// Widget position information
/// </summary>
public sealed record WidgetPosition
{
    public int X { get; init; }
    public int Y { get; init; }
}

/// <summary>
/// Widget size information
/// </summary>
public sealed record WidgetSize
{
    public int Width { get; init; } = 4;
    public int Height { get; init; } = 3;
}

/// <summary>
/// Event arguments for alert triggered events
/// </summary>
public class AlertTriggeredEventArgs : EventArgs
{
    public required AlertInstance Alert { get; init; }
    public DateTimeOffset TriggeredAt { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Event arguments for metric threshold exceeded events
/// </summary>
public class MetricThresholdExceededEventArgs : EventArgs
{
    public required string MetricName { get; init; }
    public double CurrentValue { get; init; }
    public double Threshold { get; init; }
    public Dictionary<string, string> Tags { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}