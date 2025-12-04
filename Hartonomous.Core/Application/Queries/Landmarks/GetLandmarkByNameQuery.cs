using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Landmarks;

/// <summary>
/// Query to get a landmark by name
/// </summary>
public sealed record GetLandmarkByNameQuery : IQuery<Result<LandmarkDto?>>
{
    public required string Name { get; init; }
}

public sealed record LandmarkDto
{
    public required Guid Id { get; init; }
    public required string Name { get; init; }
    public string? Description { get; init; }
    public required SpatialCoordinateDto Center { get; init; }
    public required double Radius { get; init; }
    public required int ConstantCount { get; init; }
    public required double Density { get; init; }
    public required bool IsActive { get; init; }
    public required DateTime CreatedAt { get; init; }
}

public sealed record SpatialCoordinateDto
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
}
