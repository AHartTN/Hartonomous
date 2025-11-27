namespace Hartonomous.CodeAtomizer.Core.Models;

/// <summary>
/// Represents a single atom in the Hartonomous system.
/// Content-addressable unit of data (?64 bytes preferred, but flexible for AST nodes).
/// </summary>
public sealed record Atom
{
    /// <summary>
    /// SHA-256 hash of AtomicValue (content addressing)
    /// </summary>
    public required byte[] ContentHash { get; init; }

    /// <summary>
    /// The actual atomic value (raw bytes)
    /// </summary>
    public required byte[] AtomicValue { get; init; }

    /// <summary>
    /// Human-readable text representation
    /// </summary>
    public required string CanonicalText { get; init; }

    /// <summary>
    /// 3D spatial position (semantic meaning)
    /// </summary>
    public required SpatialPosition SpatialKey { get; init; }

    /// <summary>
    /// Modality: "code", "text", "numeric", etc.
    /// </summary>
    public required string Modality { get; init; }

    /// <summary>
    /// Subtype: "function", "class", "identifier", "keyword", etc.
    /// </summary>
    public string? Subtype { get; init; }

    /// <summary>
    /// JSONB metadata
    /// </summary>
    public required string Metadata { get; init; }
}

/// <summary>
/// 3D spatial position in semantic space
/// </summary>
public sealed record SpatialPosition(double X, double Y, double Z);

/// <summary>
/// Hierarchical composition relationship
/// </summary>
public sealed record AtomComposition
{
    /// <summary>
    /// Parent atom hash (e.g., function body)
    /// </summary>
    public required byte[] ParentAtomHash { get; init; }

    /// <summary>
    /// Child atom hash (e.g., statement within function)
    /// </summary>
    public required byte[] ComponentAtomHash { get; init; }

    /// <summary>
    /// Sequential order within parent
    /// </summary>
    public required int SequenceIndex { get; init; }

    /// <summary>
    /// Relative position within parent structure
    /// </summary>
    public SpatialPosition? Position { get; init; }
}

/// <summary>
/// Semantic relationship (knowledge graph edge)
/// </summary>
public sealed record AtomRelation
{
    /// <summary>
    /// Source atom (subject)
    /// </summary>
    public required byte[] SourceAtomHash { get; init; }

    /// <summary>
    /// Target atom (object)
    /// </summary>
    public required byte[] TargetAtomHash { get; init; }

    /// <summary>
    /// Relation type: "calls", "inherits", "references", "defines", etc.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    /// Synaptic weight (0.0 to 1.0)
    /// </summary>
    public double Weight { get; init; } = 1.0;

    /// <summary>
    /// Spatial distance in semantic space
    /// </summary>
    public double? SpatialDistance { get; init; }

    /// <summary>
    /// JSONB metadata
    /// </summary>
    public string? Metadata { get; init; }
}
