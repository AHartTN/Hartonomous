using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get recent ingestions
/// </summary>
public sealed record GetRecentIngestionsQuery : IQuery<Result<PaginatedResult<ContentIngestionDto>>>
{
    public required int Page { get; init; }
    public required int PageSize { get; init; }
    public required bool SuccessOnly { get; init; }
}
