namespace Hartonomous.Core.Application.Queries;

/// <summary>
/// Shared DTO for spatial coordinates across all queries
/// </summary>
public sealed record SpatialCoordinateDto
{
    public required double X { get; init; }
    public required double Y { get; init; }
    public required double Z { get; init; }
}
