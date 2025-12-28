using Hartonomous.Core.Models;

namespace Hartonomous.Core.Services.Abstractions;

/// <summary>
/// Relationship queries - sparse semantic graph traversal.
/// Navigate edges between compositions in the universal substrate.
/// </summary>
public interface IRelationshipService
{
    /// <summary>
    /// Store a weighted relationship between two nodes.
    /// Sparse: only call for salient (non-zero) weights.
    /// </summary>
    void Store(
        NodeId source, 
        NodeId target, 
        double weight,
        RelationshipType type = RelationshipType.SemanticLink,
        NodeId context = default);

    /// <summary>
    /// Find relationships FROM a node (outgoing edges).
    /// </summary>
    Relationship[] FindFrom(NodeId from, int limit = 100);

    /// <summary>
    /// Find relationships TO a node (incoming edges).
    /// </summary>
    Relationship[] FindTo(NodeId target, int limit = 100);

    /// <summary>
    /// Find relationships by type from a node.
    /// </summary>
    Relationship[] FindByType(NodeId from, RelationshipType type, int limit = 100);

    /// <summary>
    /// Find relationships by weight range (for model analysis).
    /// </summary>
    Relationship[] FindByWeight(double minWeight, double maxWeight, NodeId context = default, int limit = 1000);

    /// <summary>
    /// Get the weight between two specific nodes.
    /// </summary>
    double? GetWeight(NodeId source, NodeId target, NodeId context = default);
}
