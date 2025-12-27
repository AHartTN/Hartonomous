using Hartonomous.Core.Native;
using Hartonomous.Core.Primitives;

namespace Hartonomous.Core.Services;

/// <summary>
/// Relationship queries - sparse semantic graph traversal.
/// Navigate edges between compositions in the universal substrate.
/// </summary>
public sealed class RelationshipService
{
    private readonly DatabaseService _db;

    public RelationshipService(DatabaseService? db = null)
    {
        _db = db ?? DatabaseService.Instance;
    }

    /// <summary>
    /// Store a weighted relationship between two nodes.
    /// Sparse: only call for salient (non-zero) weights.
    /// </summary>
    public void Store(
        NodeId from, 
        NodeId to, 
        double weight,
        RelationshipType type = RelationshipType.SemanticLink,
        NodeId context = default)
    {
        _db.Initialize();

        var status = NativeInterop.StoreRelationship(
            from.High, from.Low,
            to.High, to.Low,
            weight,
            (short)type,
            context.High, context.Low);

        if (status < 0)
            throw new InvalidOperationException($"StoreRelationship failed: error {status}");
    }

    /// <summary>
    /// Find relationships FROM a node (outgoing edges).
    /// </summary>
    public Relationship[] FindFrom(NodeId from, int limit = 100)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeRelationship[limit];
        var status = NativeInterop.FindFrom(from.High, from.Low, results, limit, out var count);

        if (status < 0)
            throw new InvalidOperationException($"FindFrom failed: error {status}");

        return ConvertRelationships(results, count);
    }

    /// <summary>
    /// Find relationships TO a node (incoming edges).
    /// </summary>
    public Relationship[] FindTo(NodeId to, int limit = 100)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeRelationship[limit];
        var status = NativeInterop.FindTo(to.High, to.Low, results, limit, out var count);

        if (status < 0)
            throw new InvalidOperationException($"FindTo failed: error {status}");

        return ConvertRelationships(results, count);
    }

    /// <summary>
    /// Find relationships by type from a node.
    /// </summary>
    public Relationship[] FindByType(NodeId from, RelationshipType type, int limit = 100)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeRelationship[limit];
        var status = NativeInterop.FindByType(from.High, from.Low, (short)type, results, limit, out var count);

        if (status < 0)
            throw new InvalidOperationException($"FindByType failed: error {status}");

        return ConvertRelationships(results, count);
    }

    /// <summary>
    /// Find relationships by weight range (for model analysis).
    /// </summary>
    public Relationship[] FindByWeight(double minWeight, double maxWeight, NodeId context = default, int limit = 1000)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeRelationship[limit];
        var status = NativeInterop.FindByWeight(minWeight, maxWeight, context.High, context.Low, results, limit, out var count);

        if (status < 0)
            throw new InvalidOperationException($"FindByWeight failed: error {status}");

        return ConvertRelationships(results, count);
    }

    /// <summary>
    /// Get the weight between two specific nodes.
    /// </summary>
    public double? GetWeight(NodeId from, NodeId to, NodeId context = default)
    {
        _db.Initialize();

        var status = NativeInterop.GetWeight(
            from.High, from.Low,
            to.High, to.Low,
            context.High, context.Low,
            out var weight);

        return status switch
        {
            0 => weight,
            1 => null, // Not found
            _ => throw new InvalidOperationException($"GetWeight failed: error {status}")
        };
    }

    private static Relationship[] ConvertRelationships(NativeInterop.NativeRelationship[] native, int count)
    {
        var result = new Relationship[count];
        for (int i = 0; i < count; i++)
        {
            result[i] = new Relationship(
                new NodeId(native[i].FromHigh, native[i].FromLow),
                new NodeId(native[i].ToHigh, native[i].ToLow),
                native[i].Weight,
                (RelationshipType)native[i].RelType,
                new NodeId(native[i].ContextHigh, native[i].ContextLow));
        }
        return result;
    }
}

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
