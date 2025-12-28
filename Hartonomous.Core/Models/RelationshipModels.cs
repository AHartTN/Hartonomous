namespace Hartonomous.Core.Models;

/// <summary>
/// Relationship types in the semantic graph.
/// </summary>
public enum RelationshipType : short
{
    /// <summary>General semantic relationship.</summary>
    SemanticLink = 0,
    /// <summary>Neural network weight (sparse/salient only).</summary>
    ModelWeight = 1,
    /// <summary>Knowledge graph edge.</summary>
    KnowledgeEdge = 2,
    /// <summary>Sequence/temporal relationship.</summary>
    TemporalNext = 3,
    /// <summary>Spatial proximity.</summary>
    SpatialNear = 4
}

/// <summary>
/// A relationship between two nodes in the semantic graph.
/// </summary>
public readonly record struct Relationship(
    NodeId From,
    NodeId To,
    double Weight,
    RelationshipType Type,
    NodeId Context);

/// <summary>
/// 128-bit node identifier in the universal substrate.
/// </summary>
public readonly record struct NodeId(long High, long Low)
{
    public static NodeId Empty => default;
    public bool IsEmpty => High == 0 && Low == 0;
}
