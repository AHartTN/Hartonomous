using Hartonomous.Core.Application.Common;
using MediatR;

namespace Hartonomous.Core.Application.Commands.Landmarks;

/// <summary>
/// Command to run DBSCAN clustering and detect landmarks.
/// </summary>
public sealed class DetectLandmarksCommand : IRequest<Result<int>>
{
    public int MinClusterSize { get; set; } = 100;
    public double EpsilonDistance { get; set; } = 0.1;
    public double MaxLandmarkRadius { get; set; } = 1.0;
    public bool UpdateExisting { get; set; } = true;
}
