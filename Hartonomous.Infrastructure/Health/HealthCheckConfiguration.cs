using Hartonomous.Infrastructure.Extensions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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

        // Self health check handled by ServiceDefaults (removed duplicate)

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

        // Infrastructure services health checks (blob storage, message queue)
        healthChecksBuilder.AddInfrastructureHealthChecks(configuration);

        return services;
    }
}
