using Hartonomous.Core.Models;
using Hartonomous.Core.Native;
using Hartonomous.Core.Primitives;
using Hartonomous.Core.Services.Abstractions;

namespace Hartonomous.Core.Services;

/// <summary>
/// Relationship queries - sparse semantic graph traversal.
/// Navigate edges between compositions in the universal substrate.
/// </summary>
public sealed class RelationshipService : IRelationshipService
{
    private readonly IDatabaseService _db;

    public RelationshipService(IDatabaseService db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    /// <summary>
    /// Store a weighted relationship between two nodes.
    /// Sparse: only call for salient (non-zero) weights.
    /// </summary>
    public void Store(
        NodeId source, 
        NodeId target, 
        double weight,
        RelationshipType type = RelationshipType.SemanticLink,
        NodeId context = default)
    {
        _db.Initialize();

        var status = NativeInterop.StoreRelationship(
            source.High, source.Low,
            target.High, target.Low,
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
    public Relationship[] FindTo(NodeId target, int limit = 100)
    {
        _db.Initialize();

        var results = new NativeInterop.NativeRelationship[limit];
        var status = NativeInterop.FindTo(target.High, target.Low, results, limit, out var count);

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
    public double? GetWeight(NodeId source, NodeId target, NodeId context = default)
    {
        _db.Initialize();

        var status = NativeInterop.GetWeight(
            source.High, source.Low,
            target.High, target.Low,
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
