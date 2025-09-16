using Hartonomous.Infrastructure.Observability.Interfaces;

namespace Hartonomous.Infrastructure.Observability.Services;

/// <summary>
/// No-operation metrics collector for testing or when metrics are disabled
/// </summary>
public class NoOpMetricsCollector : IMetricsCollector
{
    /// <inheritdoc />
    public void IncrementCounter(string name, double increment = 1.0, Dictionary<string, string>? tags = null)
    {
        // No operation
    }

    /// <inheritdoc />
    public void RecordGauge(string name, double value, Dictionary<string, string>? tags = null)
    {
        // No operation
    }

    /// <inheritdoc />
    public void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null)
    {
        // No operation
    }
}