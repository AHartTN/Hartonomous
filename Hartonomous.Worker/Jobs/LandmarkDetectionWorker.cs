using Hartonomous.Core.Application.Commands.Landmarks;
using Hartonomous.Core.Application.Queries.Constants;
using Hartonomous.Worker.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Hartonomous.Worker.Jobs;

/// <summary>
/// Background service that performs periodic DBSCAN clustering to detect landmarks.
/// Analyzes spatial distribution of constants and creates/updates landmark entities.
/// </summary>
public class LandmarkDetectionWorker : BackgroundService
{
    private readonly ILogger<LandmarkDetectionWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly LandmarkDetectionSettings _settings;
    private CrontabSchedule? _schedule;

    public LandmarkDetectionWorker(
        ILogger<LandmarkDetectionWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<WorkerSettings> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _settings = options.Value.LandmarkDetection;

        try
        {
            _schedule = CrontabSchedule.Parse(_settings.Schedule);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Invalid cron schedule: {Schedule}", _settings.Schedule);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_schedule == null)
        {
            _logger.LogError("LandmarkDetectionWorker cannot start: invalid schedule configuration");
            return;
        }

        _logger.LogInformation("LandmarkDetectionWorker started. Schedule: {Schedule}, Min cluster size: {MinSize}",
            _settings.Schedule, _settings.MinClusterSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = _schedule.GetNextOccurrence(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Next landmark detection run scheduled for {NextRun} UTC ({Delay} from now)",
                    nextRun, delay);

                await Task.Delay(delay, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await DetectLandmarksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during landmark detection execution");
            }
        }

        _logger.LogInformation("LandmarkDetectionWorker stopped");
    }

    private async Task DetectLandmarksAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        _logger.LogInformation("Starting landmark detection via DBSCAN clustering");

        try
        {
            // Get all constants for clustering (in production, this might be batched or use spatial filtering)
            var constantsQuery = new GetAllConstantsQuery
            {
                IncludeLocation = true
            };

            var constantsResult = await mediator.Send(constantsQuery, cancellationToken);
            if (!constantsResult.IsSuccess || constantsResult.Value == null || !constantsResult.Value.Any())
            {
                _logger.LogInformation("No constants available for landmark detection");
                return;
            }

            _logger.LogInformation("Analyzing {Count} constants for landmarks", constantsResult.Value.Count());

            // Run DBSCAN clustering command
            var clusterCommand = new DetectLandmarksCommand
            {
                MinClusterSize = _settings.MinClusterSize,
                EpsilonDistance = _settings.EpsilonDistance,
                MaxLandmarkRadius = _settings.MaxLandmarkRadius,
                UpdateExisting = _settings.UpdateExistingLandmarks
            };

            var result = await mediator.Send(clusterCommand, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("Landmark detection completed. Landmarks detected: {Count}", result.Value);
            }
            else
            {
                _logger.LogWarning("Landmark detection completed with errors: {Errors}",
                    string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute landmark detection");
            throw;
        }
    }
}
