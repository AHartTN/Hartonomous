using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get ingestion metrics
/// </summary>
public sealed record GetIngestionMetricsQuery : IQuery<Result<IngestionMetricsDto>>
{
}
