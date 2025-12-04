using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

namespace Hartonomous.API.Configuration;

/// <summary>
/// Configures rate limiting policies for API endpoints.
/// Uses fixed window algorithm with permit queuing.
/// </summary>
public static class RateLimitingConfiguration
{
    public static IServiceCollection AddRateLimitingConfiguration(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Fixed window rate limiter (default)
            options.AddFixedWindowLimiter("fixed", limiterOptions =>
            {
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:PermitLimit", 100);
                limiterOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:QueueLimit", 10);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Sliding window for high-throughput endpoints
            options.AddSlidingWindowLimiter("sliding", limiterOptions =>
            {
                limiterOptions.Window = TimeSpan.FromMinutes(1);
                limiterOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:HighThroughput:PermitLimit", 500);
                limiterOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:HighThroughput:QueueLimit", 50);
                limiterOptions.SegmentsPerWindow = 6; // 10-second segments
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Token bucket for burst traffic
            options.AddTokenBucketLimiter("burst", limiterOptions =>
            {
                limiterOptions.TokenLimit = configuration.GetValue<int>("RateLimiting:Burst:TokenLimit", 1000);
                limiterOptions.TokensPerPeriod = configuration.GetValue<int>("RateLimiting:Burst:TokensPerPeriod", 100);
                limiterOptions.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
                limiterOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:Burst:QueueLimit", 100);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Concurrency limiter for expensive operations
            options.AddConcurrencyLimiter("concurrency", limiterOptions =>
            {
                limiterOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:Concurrency:PermitLimit", 10);
                limiterOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:Concurrency:QueueLimit", 5);
                limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
            });

            // Per-user rate limiting based on authentication
            options.AddPolicy("per-user", context =>
            {
                var username = context.User?.Identity?.Name ?? "anonymous";

                return RateLimitPartition.GetFixedWindowLimiter(username, _ => new FixedWindowRateLimiterOptions
                {
                    Window = TimeSpan.FromMinutes(1),
                    PermitLimit = 200,
                    QueueLimit = 20,
                    QueueProcessingOrder = QueueProcessingOrder.OldestFirst
                });
            });

            options.OnRejected = async (context, cancellationToken) =>
            {
                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;

                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = retryAfter.TotalSeconds.ToString();

                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "Too many requests",
                        message = "Rate limit exceeded. Please retry after the specified time.",
                        retryAfterSeconds = retryAfter.TotalSeconds
                    }, cancellationToken);
                }
                else
                {
                    await context.HttpContext.Response.WriteAsJsonAsync(new
                    {
                        error = "Too many requests",
                        message = "Rate limit exceeded. Please retry later."
                    }, cancellationToken);
                }
            };
        });

        return services;
    }
}
