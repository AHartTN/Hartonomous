namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Aggregate statistics for a neural network model
/// </summary>
public record ModelStatistics
{
    public Guid ModelId { get; init; }
    public int LayerCount { get; init; }
    public long TotalParameters { get; init; }
    public int FrozenLayerCount { get; init; }
    public double AverageWeightNorm { get; init; }
    public Dictionary<string, int> LayerCountsByType { get; init; } = new();
    public int? LatestEpoch { get; init; }
}
