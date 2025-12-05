using Hartonomous.Core.Domain.Entities;

namespace Hartonomous.Core.Application.Interfaces;

/// <summary>
/// Tree structure representation of hierarchical content
/// </summary>
public record HierarchicalContentTree
{
    public HierarchicalContent Root { get; init; } = null!;
    public Dictionary<Guid, List<HierarchicalContent>> ChildrenByParent { get; init; } = new();
    public int MaxDepth { get; init; }
    public int TotalNodes { get; init; }
}
