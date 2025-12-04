using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using StackExchange.Redis;

namespace Hartonomous.Infrastructure.Health;

/// <summary>
/// Comprehensive health check configuration for production monitoring
/// </summary>
public static class HealthCheckConfiguration
{
    /// <summary>
    /// Configures all health checks for the application
    /// </summary>
    public static IServiceCollection AddComprehensiveHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecksBuilder = services.AddHealthChecks();

        // Self health check - always healthy if app is running
        healthChecksBuilder.AddCheck("self", () => HealthCheckResult.Healthy("API is running"), tags: new[] { "live" });

        // Database health check
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(connectionString))
        {
            healthChecksBuilder.AddNpgSql(
                connectionString,
                name: "postgresql",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "db" },
                timeout: TimeSpan.FromSeconds(5));
        }

        // Redis cache health check
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            healthChecksBuilder.AddRedis(
                redisConnection,
                name: "redis",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "cache" },
                timeout: TimeSpan.FromSeconds(5));
        }

        // Azure Key Vault health check
        var keyVaultUri = configuration["KeyVault:Uri"];
        if (!string.IsNullOrEmpty(keyVaultUri))
        {
            healthChecksBuilder.AddAzureKeyVault(
                new Uri(keyVaultUri),
                new Azure.Identity.DefaultAzureCredential(),
                options =>
                {
                    options.AddSecret("health-check-secret");
                },
                name: "keyvault",
                failureStatus: HealthStatus.Degraded,
                tags: new[] { "ready", "secrets" },
                timeout: TimeSpan.FromSeconds(5));
        }

        // Memory health check
        healthChecksBuilder.AddCheck<MemoryHealthCheck>(
            "memory",
            failureStatus: HealthStatus.Degraded,
            tags: new[] { "ready", "memory" });

        // Disk space health check
        healthChecksBuilder.AddCheck<DiskSpaceHealthCheck>(
            "disk",
            failureStatus: HealthStatus.Degraded,
            tags: new[] { "ready", "disk" });

        // Response time health check
        healthChecksBuilder.AddCheck<ResponseTimeHealthCheck>(
            "responsetime",
            failureStatus: HealthStatus.Degraded,
            tags: new[] { "ready", "performance" });

        return services;
    }
}

/// <summary>
/// Memory health check - monitors memory usage
/// </summary>
public class MemoryHealthCheck : IHealthCheck
{
    private const long ThresholdInBytes = 1024L * 1024L * 1024L; // 1 GB

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
