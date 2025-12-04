using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get content ingestion by ID
/// </summary>
public sealed record GetContentIngestionByIdQuery : IQuery<Result<ContentIngestionDto>>
{
    public required Guid IngestionId { get; init; }
}

/// <summary>
/// Content ingestion DTO
/// </summary>
public sealed record ContentIngestionDto
{
    public required Guid Id { get; init; }
    public required string ContentHash { get; init; }
    public required int TotalConstants { get; init; }
    public required int UniqueConstants { get; init; }
    public required double DeduplicationRatio { get; init; }
    public required long ProcessingTimeMs { get; init; }
    public required DateTime IngestedAt { get; init; }
    public required bool IsSuccessful { get; init; }
    public string? ErrorMessage { get; init; }
}
