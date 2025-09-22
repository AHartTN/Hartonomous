using Microsoft.Extensions.Logging;

namespace Hartonomous.ModelService.Services;

/// <summary>
/// Tracks and reports model ingestion progress
/// Provides real-time updates via events
/// </summary>
public class IngestionProgressService
{
    private readonly ILogger<IngestionProgressService> _logger;
    private readonly Dictionary<Guid, IngestionProgress> _activeIngestions = new();

    public event EventHandler<ProgressEventArgs>? ProgressUpdated;

    public IngestionProgressService(ILogger<IngestionProgressService> logger)
    {
        _logger = logger;
    }

    public void StartIngestion(Guid modelId, string modelName, string userId)
    {
        var progress = new IngestionProgress
        {
            ModelId = modelId,
            ModelName = modelName,
            UserId = userId,
            Status = IngestionStatus.Starting,
            OverallProgress = 0,
            StartTime = DateTime.UtcNow
        };

        _activeIngestions[modelId] = progress;
        NotifyProgress(progress, "Ingestion started");
    }

    public void UpdateProgress(Guid modelId, IngestionStage stage, double stageProgress, string? message = null)
    {
        if (!_activeIngestions.TryGetValue(modelId, out var progress))
            return;

        progress.CurrentStage = stage;
        progress.StageProgress = Math.Max(0, Math.Min(100, stageProgress));
        progress.OverallProgress = CalculateOverallProgress(stage, stageProgress);
        progress.LastUpdate = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(message))
            progress.StatusMessage = message;

        NotifyProgress(progress, message);
    }

    public void CompleteIngestion(Guid modelId, int componentCount, string? message = null)
    {
        if (!_activeIngestions.TryGetValue(modelId, out var progress))
            return;

        progress.Status = IngestionStatus.Completed;
        progress.OverallProgress = 100;
        progress.ComponentCount = componentCount;
        progress.EndTime = DateTime.UtcNow;
        progress.StatusMessage = message ?? "Ingestion completed successfully";

        NotifyProgress(progress, progress.StatusMessage);

        // Keep completed ingestions for a while for status queries
        _ = Task.Delay(TimeSpan.FromMinutes(5)).ContinueWith(_ => _activeIngestions.Remove(modelId));
    }

    public void FailIngestion(Guid modelId, string errorMessage)
    {
        if (!_activeIngestions.TryGetValue(modelId, out var progress))
            return;

        progress.Status = IngestionStatus.Failed;
        progress.StatusMessage = errorMessage;
        progress.EndTime = DateTime.UtcNow;

        NotifyProgress(progress, errorMessage);

        // Keep failed ingestions for a while for debugging
        _ = Task.Delay(TimeSpan.FromMinutes(10)).ContinueWith(_ => _activeIngestions.Remove(modelId));
    }

    public IngestionProgress? GetProgress(Guid modelId)
    {
        return _activeIngestions.TryGetValue(modelId, out var progress) ? progress : null;
    }

    public List<IngestionProgress> GetActiveIngestions(string? userId = null)
    {
        var ingestions = _activeIngestions.Values.ToList();

        if (!string.IsNullOrEmpty(userId))
            ingestions = ingestions.Where(i => i.UserId == userId).ToList();

        return ingestions.OrderByDescending(i => i.StartTime).ToList();
    }

    private double CalculateOverallProgress(IngestionStage stage, double stageProgress)
    {
        var stageWeights = new Dictionary<IngestionStage, (double start, double weight)>
        {
            [IngestionStage.Validation] = (0, 5),
            [IngestionStage.Parsing] = (5, 15),
            [IngestionStage.ComponentExtraction] = (20, 25),
            [IngestionStage.DatabaseStorage] = (45, 20),
            [IngestionStage.EmbeddingGeneration] = (65, 20),
            [IngestionStage.GraphStorage] = (85, 10),
            [IngestionStage.Finalization] = (95, 5)
        };

        if (!stageWeights.TryGetValue(stage, out var weights))
            return 0;

        return weights.start + (stageProgress / 100.0 * weights.weight);
    }

    private void NotifyProgress(IngestionProgress progress, string? message)
    {
        _logger.LogDebug("Ingestion progress: {ModelName} - {Stage} - {Progress:F1}% - {Message}",
            progress.ModelName, progress.CurrentStage, progress.OverallProgress, message);

        ProgressUpdated?.Invoke(this, new ProgressEventArgs(progress));
    }
}

public class IngestionProgress
{
    public Guid ModelId { get; set; }
    public string ModelName { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public IngestionStatus Status { get; set; }
    public IngestionStage CurrentStage { get; set; }
    public double OverallProgress { get; set; }
    public double StageProgress { get; set; }
    public string StatusMessage { get; set; } = string.Empty;
    public int ComponentCount { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime LastUpdate { get; set; }
    public DateTime? EndTime { get; set; }

    public TimeSpan ElapsedTime => (EndTime ?? DateTime.UtcNow) - StartTime;
}

public enum IngestionStatus
{
    Starting,
    InProgress,
    Completed,
    Failed
}

public enum IngestionStage
{
    Validation,
    Parsing,
    ComponentExtraction,
    DatabaseStorage,
    EmbeddingGeneration,
    GraphStorage,
    Finalization
}

public class ProgressEventArgs : EventArgs
{
    public IngestionProgress Progress { get; }

    public ProgressEventArgs(IngestionProgress progress)
    {
        Progress = progress;
    }
}