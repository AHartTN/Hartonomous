namespace Hartonomous.Data.Abstractions;

/// <summary>
/// Represents a stored composition with its metadata.
/// </summary>
public readonly record struct CompositionRecord(
    byte[] MerkleHash,
    byte[] SerializedData,
    DateTimeOffset CreatedAt,
    long ReferenceCount);
