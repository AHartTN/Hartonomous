using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Landmarks;

/// <summary>
/// Query to get all landmarks
/// </summary>
public sealed record GetAllLandmarksQuery : IQuery<Result<IReadOnlyList<LandmarkDto>>>
{
    public required bool IncludeInactive { get; init; }
}
