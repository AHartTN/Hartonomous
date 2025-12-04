using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get all constants (with optional location data).
/// </summary>
public sealed class GetAllConstantsQuery : IRequest<Result<IEnumerable<AllConstantDto>>>
{
    public bool IncludeLocation { get; set; } = true;
    public int? Limit { get; set; }
}

public sealed class AllConstantDto
{
    public Guid Id { get; set; }
    public string Hash { get; set; } = string.Empty;
    public double X { get; set; }
    public double Y { get; set; }
    public double Z { get; set; }
    public DateTime CreatedAt { get; set; }
}
