using Hartonomous.Core.Application.Commands.Constants;
using Hartonomous.Core.Application.Queries.Constants;
using Hartonomous.Worker.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Hartonomous.Worker.Jobs;

/// <summary>
/// Background worker that maintains spatial indexes for constant entities.
/// Continuously monitors for new constants and updates indexes, with periodic optimization.
/// </summary>
public class ConstantIndexingWorker : BackgroundService
{
    private readonly ILogger<ConstantIndexingWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConstantIndexingSettings _settings;
    private CrontabSchedule? _optimizationSchedule;
    private DateTime? _lastOptimization;

    public ConstantIndexingWorker(
        ILogger<ConstantIndexingWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<WorkerSettings> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _settings = options.Value.ConstantIndexing;

        if (_settings.EnableOptimization)
        {
            try
            {
                _optimizationSchedule = CrontabSchedule.Parse(_settings.OptimizationSchedule);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Invalid optimization schedule: {Schedule}", _settings.OptimizationSchedule);
            }
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ConstantIndexingWorker started. Polling interval: {Interval}s, Batch size: {BatchSize}",
            _settings.PollingIntervalSeconds, _settings.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await IndexNewConstantsAsync(stoppingToken);

                if (_settings.EnableOptimization && ShouldRunOptimization())
                {
                    await OptimizeIndexesAsync(stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during constant indexing");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ConstantIndexingWorker stopped");
    }

    private async Task IndexNewConstantsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Query for unindexed constants (this would require an IsIndexed flag or similar in the domain)
        // For now, we'll use a placeholder query
        var query = new GetRecentConstantsQuery
        {
            Hours = 1,
            PageSize = _settings.BatchSize
        };

        var result = await mediator.Send(query, cancellationToken);
        if (!result.IsSuccess || result.Value == null || !result.Value.Any())
        {
            return;
        }

        _logger.LogInformation("Found {Count} constants to index", result.Value.Count());

        // Update spatial indexes
        var indexCommand = new UpdateConstantIndexesCommand
        {
            ConstantIds = result.Value.Select(c => c.Id).ToList(),
            BatchSize = _settings.BatchSize
        };

        var indexResult = await mediator.Send(indexCommand, cancellationToken);

        if (indexResult.IsSuccess)
        {
            _logger.LogInformation("Successfully indexed {Count} constants", result.Value.Count());
        }
        else
        {
            _logger.LogWarning("Indexing completed with errors: {Errors}",
                string.Join(", ", indexResult.Errors));
        }
    }

    private bool ShouldRunOptimization()
    {
        if (_optimizationSchedule == null)
            return false;

        var nextRun = _optimizationSchedule.GetNextOccurrence(_lastOptimization ?? DateTime.UtcNow.AddDays(-1));
        return DateTime.UtcNow >= nextRun;
    }

    private async Task OptimizeIndexesAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        _logger.LogInformation("Starting spatial index optimization");

        try
        {
            var optimizeCommand = new OptimizeSpatialIndexesCommand();
            var result = await mediator.Send(optimizeCommand, cancellationToken);

            if (result.IsSuccess)
            {
                _lastOptimization = DateTime.UtcNow;
                _logger.LogInformation("Spatial index optimization completed successfully");
            }
            else
            {
                _logger.LogWarning("Index optimization completed with errors: {Errors}",
                    string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to optimize spatial indexes");
        }
    }
}
