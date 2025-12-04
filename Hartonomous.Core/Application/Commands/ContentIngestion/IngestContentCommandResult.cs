using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.ContentIngestion;

/// <summary>
/// Result type alias for IngestContentCommand response
/// </summary>
public sealed record IngestContentCommandResult
{
    public required Guid IngestionId { get; init; }
    public required string ContentHash { get; init; }
    public required int TotalConstantsCreated { get; init; }
    public required int UniqueConstantsCreated { get; init; }
    public required double DeduplicationRatio { get; init; }
    public required long ProcessingTimeMs { get; init; }
}
