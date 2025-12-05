using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Landmarks;

/// <summary>
/// Query to get a landmark by name
/// </summary>
public sealed record GetLandmarkByNameQuery : IQuery<Result<LandmarkDto?>>
{
    public required string Name { get; init; }
}
