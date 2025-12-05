using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hartonomous.Infrastructure.Health;

/// <summary>
/// Disk space health check - monitors available disk space
/// </summary>
public class DiskSpaceHealthCheck : IHealthCheck
{
    private readonly IWebHostEnvironment _environment;
    private const long MinimumFreeBytesThreshold = 1024L * 1024L * 1024L * 5L; // 5 GB

    public DiskSpaceHealthCheck(IWebHostEnvironment environment)
    {
        _environment = environment;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var drive = new DriveInfo(Path.GetPathRoot(_environment.ContentRootPath)!);
            var freeSpaceGB = drive.AvailableFreeSpace / 1024.0 / 1024.0 / 1024.0;
            
            var data = new Dictionary<string, object>
            {
                { "Drive", drive.Name },
                { "FreeSpaceGB", Math.Round(freeSpaceGB, 2) },
                { "TotalSizeGB", Math.Round(drive.TotalSize / 1024.0 / 1024.0 / 1024.0, 2) }
            };

            var status = drive.AvailableFreeSpace >= MinimumFreeBytesThreshold
                ? HealthStatus.Healthy
                : HealthStatus.Degraded;

            return Task.FromResult(new HealthCheckResult(
                status,
                description: $"Reports degraded status if available disk space is less than {MinimumFreeBytesThreshold / 1024 / 1024 / 1024} GB",
                data: data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(new HealthCheckResult(
                HealthStatus.Unhealthy,
                description: "Failed to check disk space",
                exception: ex));
        }
    }
}
