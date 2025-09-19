namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Unit of work interface for transaction management
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Get repository for specific entity type
    /// </summary>
    IRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>;

    /// <summary>
    /// Begin new transaction
    /// </summary>
    Task BeginTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Commit current transaction
    /// </summary>
    Task CommitTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Rollback current transaction
    /// </summary>
    Task RollbackTransactionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Save all changes
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Repository factory interface
/// </summary>
public interface IRepositoryFactory
{
    /// <summary>
    /// Create repository for entity type
    /// </summary>
    IRepository<TEntity, TKey> CreateRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>;

    /// <summary>
    /// Create repository with custom connection string
    /// </summary>
    IRepository<TEntity, TKey> CreateRepository<TEntity, TKey>(string connectionString)
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>;
}

/// <summary>
/// Base entity interface
/// </summary>
public interface IEntity<TKey> where TKey : IEquatable<TKey>
{
    TKey Id { get; set; }
}