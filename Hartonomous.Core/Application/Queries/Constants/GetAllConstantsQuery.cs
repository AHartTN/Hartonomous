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
