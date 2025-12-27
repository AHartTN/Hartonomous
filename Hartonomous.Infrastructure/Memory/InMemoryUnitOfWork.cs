using Hartonomous.Data.Abstractions;

namespace Hartonomous.Infrastructure.Memory;

/// <summary>
/// In-memory implementation of unit of work for testing and development.
/// Note: This implementation does not provide true transactional semantics.
/// </summary>
public sealed class InMemoryUnitOfWork : IUnitOfWork
{
    private bool _disposed;

    public InMemoryUnitOfWork(InMemoryAtomRepository atoms, InMemoryCompositionRepository compositions)
    {
        Atoms = atoms;
        Compositions = compositions;
    }

    public IAtomRepository Atoms { get; }
    public ICompositionRepository Compositions { get; }

    public ValueTask CommitAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // In-memory implementation commits immediately, no-op here
        return ValueTask.CompletedTask;
    }

    public ValueTask RollbackAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        // In-memory implementation cannot rollback, no-op here
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _disposed = true;
        return ValueTask.CompletedTask;
    }
}
