/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the core repository abstraction interfaces for AI-native data access patterns.
 * Features generic entity operations, repository factory patterns, and unit of work transaction management.
 */

using System.Linq.Expressions;

namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Generic repository interface for data access operations
/// </summary>
/// <typeparam name="TEntity">The entity type</typeparam>
/// <typeparam name="TKey">The primary key type</typeparam>
public interface IRepository<TEntity, TKey>
    where TEntity : class, IEntity<TKey>
    where TKey : IEquatable<TKey>
{
    /// <summary>
    /// Get entity by primary key
    /// </summary>
    Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get entities by user scope
    /// </summary>
    Task<IEnumerable<TEntity>> GetByUserAsync(string userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get entities with filtering and paging
    /// </summary>
    Task<IEnumerable<TEntity>> GetAsync(
        Expression<Func<TEntity, bool>>? filter = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Create new entity
    /// </summary>
    Task<TKey> CreateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update existing entity
    /// </summary>
    Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    /// <summary>
    /// Delete entity by primary key
    /// </summary>
    Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if entity exists
    /// </summary>
    Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get count of entities
    /// </summary>
    Task<int> CountAsync(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// Entity interface for repository pattern
/// </summary>
/// <typeparam name="TKey">Primary key type</typeparam>
public interface IEntity<TKey> where TKey : IEquatable<TKey>
{
    TKey Id { get; set; }
    string UserId { get; set; }
    DateTime CreatedDate { get; set; }
    DateTime? ModifiedDate { get; set; }
}

/// <summary>
/// Repository factory interface for creating repositories
/// </summary>
public interface IRepositoryFactory
{
    /// <summary>
    /// Create repository for specific entity type
    /// </summary>
    IRepository<TEntity, TKey> CreateRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>;

    /// <summary>
    /// Create repository with custom connection
    /// </summary>
    IRepository<TEntity, TKey> CreateRepository<TEntity, TKey>(string connectionString)
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>;
}

/// <summary>
/// Unit of work interface for transaction management
/// </summary>
public interface IUnitOfWork : IDisposable
{
    /// <summary>
    /// Get repository for entity type
    /// </summary>
    IRepository<TEntity, TKey> GetRepository<TEntity, TKey>()
        where TEntity : class, IEntity<TKey>
        where TKey : IEquatable<TKey>;

    /// <summary>
    /// Save all changes in a transaction
    /// </summary>
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);

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
}