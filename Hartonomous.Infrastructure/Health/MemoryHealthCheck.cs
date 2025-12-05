using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hartonomous.Infrastructure.Health;

/// <summary>
/// Memory usage health check - monitors allocated memory
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private const long ThresholdInBytes = 1024L * 1024L * 1024L * 2L; // 2 GB

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var allocatedBytes = GC.GetTotalMemory(forceFullCollection: false);
        var data = new Dictionary<string, object>
        {
            { "AllocatedMB", allocatedBytes / 1024 / 1024 },
            { "Gen0Collections", GC.CollectionCount(0) },
            { "Gen1Collections", GC.CollectionCount(1) },
            { "Gen2Collections", GC.CollectionCount(2) }
        };

        var status = allocatedBytes < ThresholdInBytes 
            ? HealthStatus.Healthy 
            : HealthStatus.Degraded;

        return Task.FromResult(new HealthCheckResult(
            status,
            description: $"Reports degraded status if memory usage exceeds {ThresholdInBytes / 1024 / 1024} MB",
            data: data));
    }
}
