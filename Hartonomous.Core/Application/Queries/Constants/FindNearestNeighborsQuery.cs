using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to find k-nearest neighbors
/// </summary>
public sealed record FindNearestNeighborsQuery : IQuery<Result<IReadOnlyList<NearestConstantDto>>>
{
    public required string Hash { get; init; }
    public required int K { get; init; }
    public required bool UseGpu { get; init; }
}
