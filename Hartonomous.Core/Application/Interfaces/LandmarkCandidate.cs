using Hartonomous.Core.Domain.ValueObjects;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Landmark candidate detected from spatial clustering.
/// </summary>
public sealed record LandmarkCandidate
{
    public required int ClusterId { get; init; }
    public required SpatialCoordinate Centroid { get; init; }
    public required int MemberCount { get; init; }
    public required string SuggestedName { get; init; }
}
