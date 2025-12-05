namespace Hartonomous.Core.Application.Queries.Constants;

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
