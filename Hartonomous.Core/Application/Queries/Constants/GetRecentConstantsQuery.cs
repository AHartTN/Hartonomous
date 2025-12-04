using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get recently created constants.
/// </summary>
public sealed class GetRecentConstantsQuery : IRequest<Result<IEnumerable<RecentConstantDto>>>
{
    public int Hours { get; set; } = 24;
    public int PageSize { get; set; } = 100;
}

public sealed class RecentConstantDto
{
    public Guid Id { get; set; }
    public string Hash { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public DateTime CreatedAt { get; set; }
}
