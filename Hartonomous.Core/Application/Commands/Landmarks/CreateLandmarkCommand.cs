using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Commands.Landmarks;

/// <summary>
/// Command to create a new spatial landmark
/// </summary>
public sealed record CreateLandmarkCommand : ICommand<Result<CreateLandmarkResponse>>
{
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required double CenterX { get; init; }
    public required double CenterY { get; init; }
    public required double CenterZ { get; init; }
    public required double Radius { get; init; }
}
