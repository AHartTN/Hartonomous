using Hartonomous.Infrastructure.Caching;
using Hartonomous.Infrastructure.Messaging;
using Hartonomous.Infrastructure.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Infrastructure.Extensions;

/// <summary>
/// Health check extensions for infrastructure services.
/// </summary>
public static class InfrastructureHealthChecksExtensions
{
    /// <summary>
    /// Add health checks for all infrastructure services.
    /// </summary>
    public static IHealthChecksBuilder AddInfrastructureHealthChecks(
        this IHealthChecksBuilder builder,
        IConfiguration configuration)
    {
        // Redis health check
        var redisConnection = configuration.GetConnectionString("Redis");
        if (!string.IsNullOrEmpty(redisConnection))
        {
            builder.AddRedis(
                redisConnection,
                name: "redis",
                tags: new[] { "cache", "infrastructure", "ready" });
        }

        // Blob Storage health check
        builder.AddCheck<BlobStorageHealthCheck>(
            name: "blob_storage",
            tags: new[] { "storage", "infrastructure", "ready" });

        // Message Queue health check
        builder.AddCheck<MessageQueueHealthCheck>(
            name: "message_queue",
            tags: new[] { "messaging", "infrastructure", "ready" });

        return builder;
    }
}
