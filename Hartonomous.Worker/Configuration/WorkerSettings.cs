namespace Hartonomous.Worker.Configuration;

/// <summary>
/// Configuration settings for background workers.
/// </summary>
public class WorkerSettings
{
    public const string SectionName = "Workers";

    /// <summary>
    /// Content processing worker settings.
    /// </summary>
    public ContentProcessingSettings ContentProcessing { get; set; } = new();

    /// <summary>
    /// BPE learning scheduler settings.
    /// </summary>
    public BPELearningSettings BPELearning { get; set; } = new();

    /// <summary>
    /// Constant indexing worker settings.
    /// </summary>
    public ConstantIndexingSettings ConstantIndexing { get; set; } = new();

    /// <summary>
    /// Landmark detection worker settings.
    /// </summary>
    public LandmarkDetectionSettings LandmarkDetection { get; set; } = new();
}

public class ContentProcessingSettings
{
    /// <summary>
    /// Maximum number of ingestion jobs to process in parallel.
    /// </summary>
    public int MaxParallelism { get; set; } = 3;

    /// <summary>
    /// How often to check for new ingestion jobs (in seconds).
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// Batch size for processing constants during decomposition.
    /// </summary>
    public int ConstantBatchSize { get; set; } = 1000;
}

public class BPELearningSettings
{
    /// <summary>
    /// How often to run BPE vocabulary updates (cron expression or TimeSpan).
    /// Default: Daily at 2 AM.
    /// </summary>
    public string Schedule { get; set; } = "0 2 * * *";

    /// <summary>
    /// Minimum number of new constants before triggering vocabulary update.
    /// </summary>
    public int MinConstantsThreshold { get; set; } = 10000;

    /// <summary>
    /// Maximum vocabulary size.
    /// </summary>
    public int MaxVocabularySize { get; set; } = 50000;

    /// <summary>
    /// Number of merge operations per learning iteration.
    /// </summary>
    public int MergeIterations { get; set; } = 1000;
}

public class ConstantIndexingSettings
{
    /// <summary>
    /// How often to check for new constants needing indexing (in seconds).
    /// </summary>
    public int PollingIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Batch size for indexing operations.
    /// </summary>
    public int BatchSize { get; set; } = 5000;

    /// <summary>
    /// Whether to run full index optimization on schedule.
    /// </summary>
    public bool EnableOptimization { get; set; } = true;

    /// <summary>
    /// Index optimization schedule (cron expression). Default: Weekly on Sunday at 3 AM.
    /// </summary>
    public string OptimizationSchedule { get; set; } = "0 3 * * 0";
}

public class LandmarkDetectionSettings
{
    /// <summary>
    /// How often to run landmark statistics update (cron expression). Default: Every 6 hours.
    /// </summary>
    public string Schedule { get; set; } = "0 */6 * * *";

    /// <summary>
    /// List of Hilbert precision levels at which to define and maintain landmarks.
    /// Example: [10, 15, 20] would create landmarks at three different granularities.
    /// </summary>
    public int[] DetectionLevels { get; set; } = { 10, 15, 20 };
}
