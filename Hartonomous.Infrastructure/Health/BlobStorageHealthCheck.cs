using Hartonomous.Infrastructure.Storage;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hartonomous.Infrastructure.Health;

/// <summary>
/// Health check for blob storage service.
/// </summary>
public class BlobStorageHealthCheck : IHealthCheck
{
    private readonly IBlobStorageService _blobStorageService;

    public BlobStorageHealthCheck(IBlobStorageService blobStorageService)
    {
        _blobStorageService = blobStorageService;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Test blob storage by checking existence of a test blob
            // This is a lightweight operation that doesn't require actual upload
            var testContainer = "__health_check__";
            var testBlob = "test.txt";

            var exists = await _blobStorageService.BlobExistsAsync(
                testContainer,
                testBlob,
                cancellationToken);

            // If we can check existence without errors, blob storage is healthy
            return HealthCheckResult.Healthy("Blob storage is accessible");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy(
                "Blob storage is not accessible",
                ex);
        }
    }
}
