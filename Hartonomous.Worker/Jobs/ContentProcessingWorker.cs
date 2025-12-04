using Hartonomous.Core.Application.Commands.ContentIngestion;
using Hartonomous.Core.Application.Queries.ContentIngestion;
using Hartonomous.Core.Domain.Enums;
using Hartonomous.Worker.Configuration;
using MediatR;
using Microsoft.Extensions.Options;

namespace Hartonomous.Worker.Jobs;

/// <summary>
/// Background worker that processes content ingestion jobs asynchronously.
/// Polls for pending ingestions, decomposes content into constants, and updates ingestion status.
/// </summary>
public class ContentProcessingWorker : BackgroundService
{
    private readonly ILogger<ContentProcessingWorker> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly ContentProcessingSettings _settings;

    public ContentProcessingWorker(
        ILogger<ContentProcessingWorker> logger,
        IServiceProvider serviceProvider,
        IOptions<WorkerSettings> options)
    {
        _logger = logger;
        _serviceProvider = serviceProvider;
        _settings = options.Value.ContentProcessing;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("ContentProcessingWorker started. Polling interval: {Interval}s, Max parallelism: {MaxParallelism}",
            _settings.PollingIntervalSeconds, _settings.MaxParallelism);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingIngestionsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ingestions");
            }

            await Task.Delay(TimeSpan.FromSeconds(_settings.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("ContentProcessingWorker stopped");
    }

    private async Task ProcessPendingIngestionsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        // Query for pending ingestions
        var query = new GetIngestionsByStatusQuery
        {
            Status = IngestionStatus.Pending,
            PageNumber = 1,
            PageSize = _settings.MaxParallelism
        };

        var result = await mediator.Send(query, cancellationToken);
        if (!result.IsSuccess || result.Value == null || !result.Value.Items.Any())
        {
            return;
        }

        _logger.LogInformation("Found {Count} pending ingestions to process", result.Value.Items.Count());

        // Process ingestions in parallel (with configured max parallelism)
        var tasks = result.Value.Items.Select(ingestion => 
            ProcessIngestionAsync(ingestion.Id, cancellationToken));

        await Task.WhenAll(tasks);
    }

    private async Task ProcessIngestionAsync(Guid ingestionId, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

        try
        {
            _logger.LogInformation("Processing ingestion {IngestionId}", ingestionId);

            // Update status to Processing
            var updateStatusCommand = new UpdateIngestionStatusCommand
            {
                IngestionId = ingestionId,
                Status = IngestionStatus.Processing
            };
            await mediator.Send(updateStatusCommand, cancellationToken);

            // Process the content (this would call the actual decomposition logic)
            var processCommand = new ProcessIngestionCommand
            {
                IngestionId = ingestionId,
                BatchSize = _settings.ConstantBatchSize
            };

            var result = await mediator.Send(processCommand, cancellationToken);

            if (result.IsSuccess)
            {
                // Update status to Completed
                var completeCommand = new UpdateIngestionStatusCommand
                {
                    IngestionId = ingestionId,
                    Status = IngestionStatus.Completed
                };
                await mediator.Send(completeCommand, cancellationToken);

                _logger.LogInformation("Successfully processed ingestion {IngestionId}", ingestionId);
            }
            else
            {
                throw new InvalidOperationException($"Processing failed: {string.Join(", ", result.Errors)}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process ingestion {IngestionId}", ingestionId);

            // Update status to Failed
            try
            {
                var failCommand = new UpdateIngestionStatusCommand
                {
                    IngestionId = ingestionId,
                    Status = IngestionStatus.Failed
                };
                await mediator.Send(failCommand, cancellationToken);
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to update ingestion status to Failed for {IngestionId}", ingestionId);
            }
        }
    }
}
