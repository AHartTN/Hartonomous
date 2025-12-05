namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Aggregate statistics for content boundaries
/// </summary>
public record ContentBoundaryStatistics
{
    public long TotalCount { get; init; }
    public double AverageArea { get; init; }
    public double AverageDensity { get; init; }
    public double AverageAtomCount { get; init; }
    public double MinArea { get; init; }
    public double MaxArea { get; init; }
    public Dictionary<string, int> CountByMethod { get; init; } = new();
}
