using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to find constants near a coordinate
/// </summary>
public sealed record GetConstantsNearCoordinateQuery : IQuery<Result<IReadOnlyList<ConstantDto>>>
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
    public required double Radius { get; init; }
    public required int Limit { get; init; }
}
