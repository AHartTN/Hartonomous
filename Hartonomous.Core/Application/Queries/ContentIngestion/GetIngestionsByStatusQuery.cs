using Hartonomous.Core.Application.Common;
using Hartonomous.Core.Domain.Enums;
using MediatR;

namespace Hartonomous.Core.Application.Queries.ContentIngestion;

/// <summary>
/// Query to get ingestions by status.
/// </summary>
public sealed class GetIngestionsByStatusQuery : IRequest<Result<PaginatedResult<IngestionDto>>>
{
    public IngestionStatus Status { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
