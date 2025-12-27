using System.Collections.Concurrent;
using Hartonomous.Core.Primitives;
using Hartonomous.Data.Abstractions;

namespace Hartonomous.Infrastructure.Memory;

/// <summary>
/// In-memory implementation of atom repository for testing and development.
/// </summary>
public sealed class InMemoryAtomRepository : IAtomRepository
{
    private readonly ConcurrentDictionary<int, HilbertIndex128> _byCodepoint = new();
    private readonly ConcurrentDictionary<HilbertIndex128, int> _byHilbert = new();

    public ValueTask StoreAsync(int codepoint, HilbertIndex128 hilbertIndex, CancellationToken cancellationToken = default)
    {
        _byCodepoint[codepoint] = hilbertIndex;
        _byHilbert[hilbertIndex] = codepoint;
        return ValueTask.CompletedTask;
    }

    public ValueTask<HilbertIndex128?> GetByCodepointAsync(int codepoint, CancellationToken cancellationToken = default)
    {
        var result = _byCodepoint.TryGetValue(codepoint, out var index) ? index : (HilbertIndex128?)null;
        return ValueTask.FromResult(result);
    }

    public ValueTask<int?> GetByHilbertIndexAsync(HilbertIndex128 hilbertIndex, CancellationToken cancellationToken = default)
    {
        var result = _byHilbert.TryGetValue(hilbertIndex, out var codepoint) ? codepoint : (int?)null;
        return ValueTask.FromResult(result);
    }

    public ValueTask<bool> ExistsAsync(int codepoint, CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult(_byCodepoint.ContainsKey(codepoint));
    }

    public ValueTask<long> CountAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult((long)_byCodepoint.Count);
    }
}
