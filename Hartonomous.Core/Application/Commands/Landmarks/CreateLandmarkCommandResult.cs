using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.Landmarks;

/// <summary>
/// Result type alias for CreateLandmarkCommand response
/// </summary>
public sealed record CreateLandmarkCommandResult
{
    public required Guid LandmarkId { get; init; }
    public required string Name { get; init; }
    public required double CenterX { get; init; }
    public required double CenterY { get; init; }
    public required double CenterZ { get; init; }
    public required double Radius { get; init; }
}
