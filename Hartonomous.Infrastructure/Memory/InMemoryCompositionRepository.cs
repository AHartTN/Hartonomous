using System.Collections.Concurrent;
using Hartonomous.Data.Abstractions;

namespace Hartonomous.Infrastructure.Memory;

/// <summary>
/// In-memory implementation of composition repository for testing and development.
/// </summary>
public sealed class InMemoryCompositionRepository : ICompositionRepository
{
    private readonly ConcurrentDictionary<string, (byte[] Data, DateTimeOffset CreatedAt, long RefCount)> _store = new();

    public ValueTask StoreAsync(byte[] merkleHash, byte[] serializedData, CancellationToken cancellationToken = default)
    {
        var key = Convert.ToHexString(merkleHash);
        _store.AddOrUpdate(
            key,
            _ => (serializedData, DateTimeOffset.UtcNow, 1),
            (_, existing) => (serializedData, existing.CreatedAt, existing.RefCount + 1));
        return ValueTask.CompletedTask;
    }

    public ValueTask<CompositionRecord?> GetByHashAsync(byte[] merkleHash, CancellationToken cancellationToken = default)
    {
        var key = Convert.ToHexString(merkleHash);
        if (_store.TryGetValue(key, out var value))
        {
            return ValueTask.FromResult<CompositionRecord?>(
                new CompositionRecord(merkleHash, value.Data, value.CreatedAt, value.RefCount));
        }
        return ValueTask.FromResult<CompositionRecord?>(null);
    }

    public ValueTask<bool> ExistsAsync(byte[] merkleHash, CancellationToken cancellationToken = default)
    {
        var key = Convert.ToHexString(merkleHash);
        return ValueTask.FromResult(_store.ContainsKey(key));
    }

    public ValueTask IncrementReferenceCountAsync(byte[] merkleHash, CancellationToken cancellationToken = default)
    {
        var key = Convert.ToHexString(merkleHash);
        _store.AddOrUpdate(
            key,
            _ => throw new InvalidOperationException("Cannot increment reference count for non-existent composition."),
            (_, existing) => (existing.Data, existing.CreatedAt, existing.RefCount + 1));
        return ValueTask.CompletedTask;
    }

    public ValueTask<bool> DecrementReferenceCountAsync(byte[] merkleHash, CancellationToken cancellationToken = default)
    {
        var key = Convert.ToHexString(merkleHash);

        while (true)
        {
            if (!_store.TryGetValue(key, out var existing))
            {
                return ValueTask.FromResult(false);
            }

            var newRefCount = existing.RefCount - 1;
            if (newRefCount <= 0)
            {
                if (_store.TryRemove(key, out _))
                {
                    return ValueTask.FromResult(true);
                }
                continue; // Retry if concurrent modification
            }

            var updated = (existing.Data, existing.CreatedAt, newRefCount);
            if (_store.TryUpdate(key, updated, existing))
            {
                return ValueTask.FromResult(false);
            }
            // Retry if concurrent modification
        }
    }

    public ValueTask<long> CountAsync(CancellationToken cancellationToken = default)
    {
        return ValueTask.FromResult((long)_store.Count);
    }
}
