using Hartonomous.Core.Application.Commands.BPETokens;
using Hartonomous.Core.Application.Queries.Constants;
using Hartonomous.Worker.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using NCrontab;

namespace Hartonomous.Worker.Jobs;

/// <summary>
/// Background service that periodically updates BPE vocabulary based on constant sequences.
/// Runs on a configurable schedule (default: daily at 2 AM).
/// </summary>
public class BPELearningScheduler : BackgroundService
{
    private readonly ILogger<BPELearningScheduler> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly BPELearningSettings _settings;
    private CrontabSchedule? _schedule;

    public BPELearningScheduler(
        ILogger<BPELearningScheduler> logger,
        IServiceProvider serviceProvider,
        IOptions<WorkerSettings> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _settings = options.Value.BPELearning;

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
            _logger.LogError("BPELearningScheduler cannot start: invalid schedule configuration");
            return;
        }

        _logger.LogInformation("BPELearningScheduler started. Schedule: {Schedule}", _settings.Schedule);

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = _schedule.GetNextOccurrence(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Next BPE learning run scheduled for {NextRun} UTC ({Delay} from now)",
                    nextRun, delay);

                await Task.Delay(delay, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await RunBPELearningAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during BPE learning execution");
            }
        }

        _logger.LogInformation("BPELearningScheduler stopped");
    }

    private async Task RunBPELearningAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        _logger.LogInformation("Starting BPE vocabulary learning iteration");

        try
        {
            // Check if we have enough new constants
            var countQuery = new GetConstantCountQuery();
            var countResult = await mediator.Send(countQuery, cancellationToken);

            if (!countResult.IsSuccess || countResult.Value < _settings.MinConstantsThreshold)
            {
                _logger.LogInformation("Insufficient constants for learning. Current: {Count}, Threshold: {Threshold}",
                    countResult.Value, _settings.MinConstantsThreshold);
                return;
            }

            // Trigger BPE learning command
            var learnCommand = new LearnBPEVocabularyCommand
            {
                MaxVocabSize = _settings.MaxVocabularySize,
                MinFrequency = 5,
                SampleSize = _settings.MinConstantsThreshold,
                UseGpu = true
            };

            var result = await mediator.Send(learnCommand, cancellationToken);

            if (result.IsSuccess)
            {
                _logger.LogInformation("BPE learning completed successfully. Tokens learned: {TokenCount}",
                    result.Value);
            }
            else
            {
                _logger.LogWarning("BPE learning completed with errors: {Errors}",
                    string.Join(", ", result.Errors));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute BPE learning");
            throw;
        }
    }
}
