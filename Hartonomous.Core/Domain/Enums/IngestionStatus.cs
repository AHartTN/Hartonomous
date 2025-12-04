namespace Hartonomous.Core.Domain.Enums;

/// <summary>
/// Represents the status of a content ingestion job.
/// </summary>
public enum IngestionStatus
{
    /// <summary>
    /// Ingestion job has been created but not yet started.
    /// </summary>
    Pending = 0,

    /// <summary>
    /// Ingestion job is currently being processed.
    /// </summary>
    Processing = 1,

    /// <summary>
    /// Ingestion job completed successfully.
    /// </summary>
    Completed = 2,

    /// <summary>
    /// Ingestion job failed with errors.
    /// </summary>
    Failed = 3,

    /// <summary>
    /// Ingestion job was cancelled.
    /// </summary>
    Cancelled = 4
}
