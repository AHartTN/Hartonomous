using Hartonomous.Data.Context;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Worker.Jobs;

/// <summary>
/// Background job to refresh hot_atoms materialized view every 5 minutes
/// Keeps frequently accessed constants cache up-to-date
/// </summary>
public class MaterializedViewRefreshJob : BackgroundService
{
    private readonly ILogger<MaterializedViewRefreshJob> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly TimeSpan _refreshInterval = TimeSpan.FromMinutes(5);

    public MaterializedViewRefreshJob(
        ILogger<MaterializedViewRefreshJob> logger,
        IServiceProvider serviceProvider)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MaterializedViewRefreshJob started. Refresh interval: {Interval} minutes", _refreshInterval.TotalMinutes);

        // Wait 1 minute before first refresh to allow app startup
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await RefreshMaterializedViewAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error refreshing materialized view");
            }

            // Wait for next refresh interval
            await Task.Delay(_refreshInterval, stoppingToken);
        }

        _logger.LogInformation("MaterializedViewRefreshJob stopped");
    }

    private async Task RefreshMaterializedViewAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var startTime = DateTime.UtcNow;
        _logger.LogInformation("Starting materialized view refresh...");

        try
        {
            await dbContext.RefreshHotAtomsMaterializedViewAsync(cancellationToken);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "Materialized view refreshed successfully in {Duration:F2}ms",
                duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            var duration = DateTime.UtcNow - startTime;
            _logger.LogError(
                ex,
                "Failed to refresh materialized view after {Duration:F2}ms",
                duration.TotalMilliseconds);
            throw;
        }
    }
}
