namespace Hartonomous.Core.Application.Commands.Landmarks;

public sealed record CreateLandmarkResponse
{
    public required Guid LandmarkId { get; init; }
    public required string Name { get; init; }
}
