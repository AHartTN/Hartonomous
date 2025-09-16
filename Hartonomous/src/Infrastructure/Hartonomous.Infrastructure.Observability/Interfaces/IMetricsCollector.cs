namespace Hartonomous.Infrastructure.Observability.Interfaces;

/// <summary>
/// Interface for collecting application metrics
/// </summary>
public interface IMetricsCollector
{
    /// <summary>
    /// Increments a counter metric
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="increment">Amount to increment (default 1)</param>
    /// <param name="tags">Optional metric tags</param>
    void IncrementCounter(string name, double increment = 1.0, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Records a gauge metric value
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Metric value</param>
    /// <param name="tags">Optional metric tags</param>
    void RecordGauge(string name, double value, Dictionary<string, string>? tags = null);

    /// <summary>
    /// Records a histogram metric value
    /// </summary>
    /// <param name="name">Metric name</param>
    /// <param name="value">Metric value</param>
    /// <param name="tags">Optional metric tags</param>
    void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null);
}