using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Landmarks;

/// <summary>
/// Query to detect landmark candidates
/// </summary>
public sealed record DetectLandmarkCandidatesQuery : IQuery<Result<IReadOnlyList<LandmarkCandidateDto>>>
{
    public required double Epsilon { get; init; }
    public required int MinSamples { get; init; }
    public required int MinClusterSize { get; init; }
}

/// <summary>
/// Landmark candidate DTO
/// </summary>
public sealed record LandmarkCandidateDto
{
    public required int ClusterId { get; init; }
    public required double CenterX { get; init; }
    public required double CenterY { get; init; }
    public required double CenterZ { get; init; }
    public required int ConstantCount { get; init; }
    public required double Density { get; init; }
    public string? SuggestedName { get; init; }
}
