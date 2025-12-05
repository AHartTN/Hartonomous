namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Aggregate statistics for hierarchical content
/// </summary>
public record HierarchyStatistics
{
    public Guid ContentIngestionId { get; init; }
    public int TotalNodes { get; init; }
    public int MaxDepth { get; init; }
    public int LeafNodeCount { get; init; }
    public Dictionary<int, int> NodeCountByLevel { get; init; } = new();
    public Dictionary<string, int> NodeCountByLabel { get; init; } = new();
    public double AverageChildrenPerNode { get; init; }
    public double AverageAtomCount { get; init; }
}
