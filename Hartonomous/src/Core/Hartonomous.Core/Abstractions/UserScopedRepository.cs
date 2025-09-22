using Microsoft.EntityFrameworkCore;
using Hartonomous.Core.Data;

namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Generic repository base class with automatic user scoping
/// Provides consistent multi-tenant data access patterns across all repositories
/// Implements common CRUD operations with user isolation
/// </summary>
/// <typeparam name="T">Entity type that implements IUserScopedEntity</typeparam>
public abstract class UserScopedRepository<T> : IUserScopedRepository<T>
    where T : class, IUserScopedEntity
{
    protected readonly HartonomousDbContext _context;
    protected readonly DbSet<T> _dbSet;

    protected UserScopedRepository(HartonomousDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _dbSet = context.Set<T>();
    }

    /// <summary>
    /// Get all entities for the specified user
    /// </summary>
    public virtual async Task<IEnumerable<T>> GetAllAsync(string userId)
    {
        return await GetUserScopedQuery(userId).ToListAsync();
    }

    /// <summary>
    /// Get entity by ID with user scoping
    /// </summary>
    public virtual async Task<T?> GetByIdAsync(Guid id, string userId)
    {
        return await GetUserScopedQuery(userId)
            .FirstOrDefaultAsync(e => e.Id == id);
    }

    /// <summary>
    /// Add new entity with automatic user assignment and timestamps
    /// </summary>
    public virtual async Task<T> AddAsync(T entity, string userId)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("UserId is required", nameof(userId));

        entity.UserId = userId;
        entity.CreatedAt = DateTime.UtcNow;
        entity.UpdatedAt = DateTime.UtcNow;

        _dbSet.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    /// <summary>
    /// Update existing entity with user validation and timestamp update
    /// </summary>
    public virtual async Task<T> UpdateAsync(T entity, string userId)
    {
        if (entity == null) throw new ArgumentNullException(nameof(entity));
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("UserId is required", nameof(userId));

        // Verify user owns the entity
        var existingEntity = await GetByIdAsync(entity.Id, userId);
        if (existingEntity == null)
            throw new UnauthorizedAccessException("Entity not found or access denied");

        entity.UserId = userId;
        entity.UpdatedAt = DateTime.UtcNow;

        _context.Entry(existingEntity).CurrentValues.SetValues(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    /// <summary>
    /// Delete entity with user validation
    /// </summary>
    public virtual async Task<bool> DeleteAsync(Guid id, string userId)
    {
        var entity = await GetByIdAsync(id, userId);
        if (entity == null) return false;

        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
        return true;
    }

    /// <summary>
    /// Check if entity exists for user
    /// </summary>
    public virtual async Task<bool> ExistsAsync(Guid id, string userId)
    {
        return await GetUserScopedQuery(userId)
            .AnyAsync(e => e.Id == id);
    }

    /// <summary>
    /// Get count of entities for user
    /// </summary>
    public virtual async Task<int> CountAsync(string userId)
    {
        return await GetUserScopedQuery(userId).CountAsync();
    }

    /// <summary>
    /// Get user-scoped queryable for the entity
    /// Override in derived classes for additional filtering or includes
    /// </summary>
    protected virtual IQueryable<T> GetUserScopedQuery(string userId)
    {
        return _dbSet.Where(e => e.UserId == userId);
    }

    /// <summary>
    /// Get user-scoped queryable with custom filtering
    /// </summary>
    protected IQueryable<T> GetUserScopedQuery(string userId, Func<IQueryable<T>, IQueryable<T>> customFilter)
    {
        var baseQuery = GetUserScopedQuery(userId);
        return customFilter(baseQuery);
    }

    /// <summary>
    /// Batch operations for performance
    /// </summary>
    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities, string userId)
    {
        if (entities == null) throw new ArgumentNullException(nameof(entities));
        if (string.IsNullOrEmpty(userId)) throw new ArgumentException("UserId is required", nameof(userId));

        var entitiesList = entities.ToList();
        var now = DateTime.UtcNow;

        foreach (var entity in entitiesList)
        {
            entity.UserId = userId;
            entity.CreatedAt = now;
            entity.UpdatedAt = now;
        }

        _dbSet.AddRange(entitiesList);
        await _context.SaveChangesAsync();
        return entitiesList;
    }

    /// <summary>
    /// Find entities matching predicate with user scoping
    /// </summary>
    public virtual async Task<IEnumerable<T>> FindAsync(Func<IQueryable<T>, IQueryable<T>> predicate, string userId)
    {
        var query = GetUserScopedQuery(userId);
        return await predicate(query).ToListAsync();
    }
}

/// <summary>
/// Interface for user-scoped repository operations
/// </summary>
public interface IUserScopedRepository<T> where T : class, IUserScopedEntity
{
    Task<IEnumerable<T>> GetAllAsync(string userId);
    Task<T?> GetByIdAsync(Guid id, string userId);
    Task<T> AddAsync(T entity, string userId);
    Task<T> UpdateAsync(T entity, string userId);
    Task<bool> DeleteAsync(Guid id, string userId);
    Task<bool> ExistsAsync(Guid id, string userId);
    Task<int> CountAsync(string userId);
    Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities, string userId);
    Task<IEnumerable<T>> FindAsync(Func<IQueryable<T>, IQueryable<T>> predicate, string userId);
}