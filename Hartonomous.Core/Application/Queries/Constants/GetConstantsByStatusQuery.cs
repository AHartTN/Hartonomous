using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get constants by status
/// </summary>
public sealed record GetConstantsByStatusQuery : IQuery<Result<PaginatedResult<ConstantDto>>>
{
    public required int Status { get; init; }
    public required int Page { get; init; }
    public required int PageSize { get; init; }
}
