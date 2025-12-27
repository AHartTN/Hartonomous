namespace Hartonomous.Data.Abstractions;

/// <summary>
/// Unit of work pattern for transactional data operations.
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    /// <summary>
    /// The atom repository.
    /// </summary>
    IAtomRepository Atoms { get; }

    /// <summary>
    /// The composition repository.
    /// </summary>
    ICompositionRepository Compositions { get; }

    /// <summary>
    /// Commit all changes.
    /// </summary>
    ValueTask CommitAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback all changes.
    /// </summary>
    ValueTask RollbackAsync(CancellationToken cancellationToken = default);
}
