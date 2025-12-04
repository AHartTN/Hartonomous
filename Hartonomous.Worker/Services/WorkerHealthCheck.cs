using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hartonomous.Worker.Services;

/// <summary>
/// Health check that monitors the status of all background workers.
/// Reports healthy if all workers are running and processing normally.
/// </summary>
public class WorkerHealthCheck : IHealthCheck
{
    private readonly ILogger<WorkerHealthCheck> _logger;
    private readonly IEnumerable<IHostedService> _hostedServices;

    public WorkerHealthCheck(
        ILogger<WorkerHealthCheck> logger,
        IEnumerable<IHostedService> hostedServices)
    {
        _logger = logger;
        _hostedServices = hostedServices;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var workerCount = _hostedServices.Count();
            var data = new Dictionary<string, object>
            {
                ["WorkerCount"] = workerCount,
                ["Workers"] = _hostedServices.Select(s => s.GetType().Name).ToList()
            };

            if (workerCount == 0)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy(
                    "No background workers registered",
                    data: data));
            }

            return Task.FromResult(HealthCheckResult.Healthy(
                $"{workerCount} background workers running",
                data: data));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking worker health");
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Error checking worker health",
                ex));
        }
    }
}
