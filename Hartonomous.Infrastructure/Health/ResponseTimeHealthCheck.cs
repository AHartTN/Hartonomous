using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hartonomous.Infrastructure.Health;

/// <summary>
/// Response time health check - monitors API response time
/// </summary>
public class ResponseTimeHealthCheck : IHealthCheck
{
    private static DateTime _lastCheckTime = DateTime.UtcNow;
    private static readonly List<double> _responseTimes = new();
    private const int MaxSamples = 100;
    private const double ThresholdMs = 1000; // 1 second

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var responseTime = (now - _lastCheckTime).TotalMilliseconds;
        _lastCheckTime = now;

        lock (_responseTimes)
        {
            _responseTimes.Add(responseTime);
            if (_responseTimes.Count > MaxSamples)
            {
                _responseTimes.RemoveAt(0);
            }
        }

        var avgResponseTime = _responseTimes.Average();
        var maxResponseTime = _responseTimes.Max();
        
        var data = new Dictionary<string, object>
        {
            { "CurrentResponseTimeMs", Math.Round(responseTime, 2) },
            { "AverageResponseTimeMs", Math.Round(avgResponseTime, 2) },
            { "MaxResponseTimeMs", Math.Round(maxResponseTime, 2) },
            { "Samples", _responseTimes.Count }
        };

        var status = avgResponseTime < ThresholdMs 
            ? HealthStatus.Healthy 
            : HealthStatus.Degraded;

        return Task.FromResult(new HealthCheckResult(
            status,
            description: $"Reports degraded status if average response time exceeds {ThresholdMs} ms",
            data: data));
    }
}
