using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Http;
using System.Threading.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Infrastructure.Security;

/// <summary>
/// Rate limiting configuration for API protection
/// Implements multiple rate limiting strategies for defense in depth
/// </summary>
public static class RateLimitingConfiguration
{
    /// <summary>
    /// Configures comprehensive rate limiting policies
    /// </summary>
    public static IServiceCollection AddApiRateLimiting(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddRateLimiter(limiterOptions =>
        {
            // Global rate limit - applies to all requests
            limiterOptions.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
            {
                var userId = context.User?.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                
                return RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: userId,
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = configuration.GetValue<int>("RateLimiting:Global:PermitLimit", 1000),
                        Window = TimeSpan.FromMinutes(1),
                        QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                        QueueLimit = configuration.GetValue<int>("RateLimiting:Global:QueueLimit", 100)
                    });
            });

            // Rejection status code
            limiterOptions.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Custom rejection response
            limiterOptions.OnRejected = async (context, cancellationToken) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter = retryAfter.TotalSeconds.ToString();
                }

                context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.HttpContext.Response.ContentType = "application/json";

                var response = new
                {
                    error = "rate_limit_exceeded",
                    message = "Too many requests. Please try again later.",
                    retryAfter = retryAfter.TotalSeconds
                };

                await context.HttpContext.Response.WriteAsJsonAsync(response, cancellationToken: cancellationToken);
            };

            // Fixed Window Limiter - simple time-based limiting
            limiterOptions.AddFixedWindowLimiter("fixed", fixedOptions =>
            {
                fixedOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:Fixed:PermitLimit", 100);
                fixedOptions.Window = TimeSpan.FromMinutes(1);
                fixedOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                fixedOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:Fixed:QueueLimit", 10);
            });

            // Sliding Window Limiter - smoother rate limiting
            limiterOptions.AddSlidingWindowLimiter("sliding", slidingOptions =>
            {
                slidingOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:Sliding:PermitLimit", 100);
                slidingOptions.Window = TimeSpan.FromMinutes(1);
                slidingOptions.SegmentsPerWindow = 6; // 10-second segments
                slidingOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                slidingOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:Sliding:QueueLimit", 10);
            });

            // Token Bucket Limiter - allows bursts
            limiterOptions.AddTokenBucketLimiter("token", tokenOptions =>
            {
                tokenOptions.TokenLimit = configuration.GetValue<int>("RateLimiting:Token:TokenLimit", 100);
                tokenOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                tokenOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:Token:QueueLimit", 10);
                tokenOptions.ReplenishmentPeriod = TimeSpan.FromSeconds(10);
                tokenOptions.TokensPerPeriod = configuration.GetValue<int>("RateLimiting:Token:TokensPerPeriod", 10);
                tokenOptions.AutoReplenishment = true;
            });

            // Concurrency Limiter - limits concurrent requests
            limiterOptions.AddConcurrencyLimiter("concurrency", concurrencyOptions =>
            {
                concurrencyOptions.PermitLimit = configuration.GetValue<int>("RateLimiting:Concurrency:PermitLimit", 50);
                concurrencyOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                concurrencyOptions.QueueLimit = configuration.GetValue<int>("RateLimiting:Concurrency:QueueLimit", 20);
            });

            // Per-user rate limiter - different limits per authenticated user
            limiterOptions.AddPolicy("per-user", context =>
            {
                var username = context.User?.Identity?.Name;
                
                if (string.IsNullOrEmpty(username))
                {
                    // Anonymous users get stricter limits
                    return RateLimitPartition.GetFixedWindowLimiter("anonymous",
                        _ => new FixedWindowRateLimiterOptions
                        {
                            PermitLimit = 20,
                            Window = TimeSpan.FromMinutes(1)
                        });
                }

                // Authenticated users get higher limits
                return RateLimitPartition.GetFixedWindowLimiter(username,
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 100,
                        Window = TimeSpan.FromMinutes(1)
                    });
            });

            // Per-IP rate limiter
            limiterOptions.AddPolicy("per-ip", context =>
            {
                var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
                
                return RateLimitPartition.GetSlidingWindowLimiter(ipAddress,
                    _ => new SlidingWindowRateLimiterOptions
                    {
                        PermitLimit = 50,
                        Window = TimeSpan.FromMinutes(1),
                        SegmentsPerWindow = 6
                    });
            });

            // API endpoint specific limiter
            limiterOptions.AddPolicy("api-endpoints", context =>
            {
                var endpoint = context.GetEndpoint();
                var routePattern = endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.RouteEndpoint>()?.RoutePattern.RawText ?? "default";
                var userId = context.User?.Identity?.Name ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
                var partitionKey = $"{userId}:{routePattern}";

                return RateLimitPartition.GetTokenBucketLimiter(partitionKey,
                    _ => new TokenBucketRateLimiterOptions
                    {
                        TokenLimit = 50,
                        TokensPerPeriod = 10,
                        ReplenishmentPeriod = TimeSpan.FromSeconds(10),
                        AutoReplenishment = true
                    });
            });
        });

        return services;
    }
}
