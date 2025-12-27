namespace Hartonomous.Data.Abstractions;

/// <summary>
/// Repository interface for composition persistence (content-addressed storage).
/// </summary>
public interface ICompositionRepository
{
    /// <summary>
    /// Store a composition by its Merkle hash.
    /// </summary>
    ValueTask StoreAsync(byte[] merkleHash, byte[] serializedData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve a composition by its Merkle hash.
    /// </summary>
    ValueTask<CompositionRecord?> GetByHashAsync(byte[] merkleHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a composition exists.
    /// </summary>
    ValueTask<bool> ExistsAsync(byte[] merkleHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Increment the reference count for a composition.
    /// </summary>
    ValueTask IncrementReferenceCountAsync(byte[] merkleHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Decrement the reference count for a composition.
    /// Returns true if the composition was deleted (reference count reached 0).
    /// </summary>
    ValueTask<bool> DecrementReferenceCountAsync(byte[] merkleHash, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the count of stored compositions.
    /// </summary>
    ValueTask<long> CountAsync(CancellationToken cancellationToken = default);
}
