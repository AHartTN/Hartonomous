namespace Hartonomous.Core.Application.Commands.ContentIngestion;

public sealed record IngestContentResponse
{
    public required Guid IngestionId { get; init; }
    public required string ContentHash { get; init; }
    public required int TotalConstantsCreated { get; init; }
    public required int UniqueConstantsCreated { get; init; }
    public required double DeduplicationRatio { get; init; }
    public required long ProcessingTimeMs { get; init; }
}
