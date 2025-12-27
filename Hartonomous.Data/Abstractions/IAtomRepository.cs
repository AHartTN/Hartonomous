using Hartonomous.Core.Primitives;

namespace Hartonomous.Data.Abstractions;

/// <summary>
/// Repository interface for atom persistence.
/// </summary>
public interface IAtomRepository
{
    /// <summary>
    /// Store an atom by its codepoint.
    /// </summary>
    ValueTask StoreAsync(int codepoint, HilbertIndex128 hilbertIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieve the Hilbert index for a codepoint.
    /// </summary>
    ValueTask<HilbertIndex128?> GetByCodepointAsync(int codepoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Find the codepoint for a Hilbert index.
    /// </summary>
    ValueTask<int?> GetByHilbertIndexAsync(HilbertIndex128 hilbertIndex, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if a codepoint exists in the repository.
    /// </summary>
    ValueTask<bool> ExistsAsync(int codepoint, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get the count of stored atoms.
    /// </summary>
    ValueTask<long> CountAsync(CancellationToken cancellationToken = default);
}
