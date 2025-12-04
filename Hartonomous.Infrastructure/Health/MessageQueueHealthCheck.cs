using Hartonomous.Infrastructure.Messaging;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hartonomous.Infrastructure.Caching;

/// <summary>
/// Health check for message queue service.
/// </summary>
public class MessageQueueHealthCheck : IHealthCheck
{
    private readonly IMessageQueueService _messageQueueService;

    public MessageQueueHealthCheck(IMessageQueueService messageQueueService)
    {
        _messageQueueService = messageQueueService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Test message queue by checking queue length
            // This is a lightweight operation
            var testQueue = "__health_check__";

            var length = await _messageQueueService.GetQueueLengthAsync(
                testQueue,
                cancellationToken);

            // If we can query queue length without errors, message queue is healthy
            return HealthCheckResult.Healthy($"Message queue is accessible (test queue length: {length})");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Message queue is not accessible",
                ex);
        }
    }
}
