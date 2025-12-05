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
