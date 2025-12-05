namespace Hartonomous.Core.Application.Commands.ContentIngestion;

/// <summary>
/// Response for repository ingestion
/// </summary>
public sealed record IngestRepositoryResponse
{
    public required Guid BatchId { get; init; }
    public required int TotalFilesProcessed { get; init; }
    public required int TotalFilesSkipped { get; init; }
    public required long TotalBytesIngested { get; init; }
    public required int TotalConstantsCreated { get; init; }
    public required int UniqueConstantsCreated { get; init; }
    public required double DeduplicationRatio { get; init; }
    public required long ProcessingTimeMs { get; init; }
    public List<string>? Errors { get; init; }
}
