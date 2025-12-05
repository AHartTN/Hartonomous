using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get ingestion statistics
/// </summary>
public sealed record GetIngestionStatisticsQuery : IQuery<Result<IngestionStatisticsDto>>
{
    public required string TimeRange { get; init; }
}
