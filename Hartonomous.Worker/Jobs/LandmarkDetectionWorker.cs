using Hartonomous.Core.Application.Interfaces;
using Hartonomous.Core.Domain.Entities;
using Hartonomous.Core.Domain.Utilities;
using Hartonomous.Core.Domain.ValueObjects;
using Hartonomous.Worker.Configuration;
using MediatR;
using Microsoft.Extensions.Options;
using NCrontab;
using System.Collections.Concurrent;
using Hartonomous.Data.Context;
using Microsoft.EntityFrameworkCore;

namespace Hartonomous.Worker.Jobs;

/// <summary>
/// Background service that periodically maintains statistics for Deterministic Hilbert Landmarks.
/// It counts constants within predefined Hilbert tiles and updates/creates Landmark entities.
/// Replaces heuristic DBSCAN clustering with a fixed, hierarchical grid approach.
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

        _logger.LogInformation("LandmarkDetectionWorker started. Schedule: {Schedule}, Levels: {Levels}",
            _settings.Schedule, string.Join(", ", _settings.DetectionLevels));

        while (!stoppingToken.IsCancellationRequested)
        {
            var nextRun = _schedule.GetNextOccurrence(DateTime.UtcNow);
            var delay = nextRun - DateTime.UtcNow;

            if (delay > TimeSpan.Zero)
            {
                _logger.LogInformation("Next landmark statistics update scheduled for {NextRun} UTC ({Delay} from now)",
                    nextRun, delay);

                await Task.Delay(delay, stoppingToken);
            }

            if (stoppingToken.IsCancellationRequested)
                break;

            try
            {
                await UpdateLandmarkStatisticsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during landmark statistics update execution");
            }
        }

        _logger.LogInformation("LandmarkDetectionWorker stopped");
    }

    private async Task UpdateLandmarkStatisticsAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>(); // Direct access for efficiency
        var landmarkRepo = scope.ServiceProvider.GetRequiredService<ILandmarkRepository>();
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();

        _logger.LogInformation("Starting deterministic Hilbert landmark statistics update");

        try
        {
            // Group constants by their Hilbert tiles for each configured detection level
            var tileCounts = new ConcurrentDictionary<string, long>(); // Key: "High-Low-Level"

            await foreach (var constant in dbContext.Constants
                                .Where(c => c.Coordinate != null && c.Status == ConstantStatus.Active)
                                .AsNoTracking() // Read-only for efficiency
                                .AsAsyncEnumerable()
                                .WithCancellation(cancellationToken))
            {
                if (constant.Coordinate == null) continue;

                foreach (var level in _settings.DetectionLevels)
                {
                    var (tileHigh, tileLow) = HilbertCurve4D.GetHilbertTileId(
                        constant.Coordinate.HilbertHigh,
                        constant.Coordinate.HilbertLow,
                        level,
                        constant.Coordinate.Precision); // Use constant's original precision

                    var tileKey = $"{tileHigh}-{tileLow}-{level}";
                    tileCounts.AddOrUpdate(tileKey, 1, (key, count) => count + 1);
                }
            }

            _logger.LogInformation("Processed {ConstantCount} constants across {TileCount} unique tiles",
                dbContext.Constants.Count(), tileCounts.Count);

            // Update or create Landmark entities
            var createdCount = 0;
            var updatedCount = 0;

            foreach (var entry in tileCounts)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var parts = entry.Key.Split('-');
                var hilbertPrefixHigh = ulong.Parse(parts[0]);
                var hilbertPrefixLow = ulong.Parse(parts[1]);
                var level = int.Parse(parts[2]);
                var constantCount = entry.Value;

                var landmarkName = $"H:{hilbertPrefixHigh:X}-{hilbertPrefixLow:X}_L{level}";

                var existingLandmark = await landmarkRepo.GetByNameAsync(landmarkName, cancellationToken);

                if (existingLandmark == null)
                {
                    var newLandmark = Landmark.Create(hilbertPrefixHigh, hilbertPrefixLow, level);
                    newLandmark.UpdateStatistics(constantCount);
                    await landmarkRepo.AddAsync(newLandmark, cancellationToken);
                    createdCount++;
                }
                else
                {
                    existingLandmark.UpdateStatistics(constantCount);
                    await landmarkRepo.UpdateAsync(existingLandmark, cancellationToken);
                    updatedCount++;
                }
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            
            _logger.LogInformation("Landmark statistics update completed. Created: {Created}, Updated: {Updated} landmarks.", createdCount, updatedCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute deterministic Hilbert landmark statistics update");
            throw;
        }
    }
}