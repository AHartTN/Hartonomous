using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get failed ingestions
/// </summary>
public sealed record GetFailedIngestionsQuery : IQuery<Result<PaginatedResult<ContentIngestionDto>>>
{
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
