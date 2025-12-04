using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get ingestion statistics
/// </summary>
public sealed record GetIngestionStatisticsQuery : IQuery<Result<IngestionStatisticsDto>>
{
    public required string TimeRange { get; init; }
}

/// <summary>
/// Ingestion statistics DTO
/// </summary>
public sealed record IngestionStatisticsDto
{
    public required long TotalIngestions { get; init; }
    public required long SuccessfulIngestions { get; init; }
    public required long FailedIngestions { get; init; }
    public required long TotalConstantsCreated { get; init; }
    public required double AverageDeduplicationRatio { get; init; }
    public required double AverageProcessingTimeMs { get; init; }
}
