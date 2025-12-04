using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get ingestions by date range
/// </summary>
public sealed record GetIngestionsByDateRangeQuery : IQuery<Result<PaginatedResult<ContentIngestionDto>>>
{
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
