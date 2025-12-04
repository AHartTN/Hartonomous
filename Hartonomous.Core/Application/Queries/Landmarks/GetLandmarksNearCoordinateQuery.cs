using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Landmarks;

/// <summary>
/// Query to get landmarks near a coordinate
/// </summary>
public sealed record GetLandmarksNearCoordinateQuery : IQuery<Result<IReadOnlyList<LandmarkDto>>>
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
    public required double Radius { get; init; }
}
