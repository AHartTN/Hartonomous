using Hartonomous.Core.Application.Common;

namespace Hartonomous.Core.Application.Queries.Constants;

/// <summary>
/// Query to get constant statistics
/// </summary>
public sealed record GetConstantStatisticsQuery : IQuery<Result<ConstantStatisticsDto>>
{
}

/// <summary>
/// Constant statistics DTO
/// </summary>
public sealed record ConstantStatisticsDto
{
    public required long TotalConstants { get; init; }
    public required long ActiveConstants { get; init; }
    public required long ArchivedConstants { get; init; }
    public required long TotalIngestions { get; init; }
    public required double AverageDeduplicationRatio { get; init; }
    public required long TotalStorageSaved { get; init; }
}
