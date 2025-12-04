using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get nearby constants using k-nearest neighbors
/// </summary>
public sealed record GetNearbyConstantsQuery : IQuery<Result<List<ConstantDto>>>
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
    public int K { get; init; } = 10;
}
