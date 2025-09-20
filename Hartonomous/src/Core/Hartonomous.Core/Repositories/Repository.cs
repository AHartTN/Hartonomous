/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the generic Entity Framework repository implementation for multi-tenant data operations.
 * Features dynamic user-scoped filtering, expression-based queries, and centralized database context management.
 */

using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;
using System.Reflection;
using Hartonomous.Core.Data;
using Hartonomous.Core.Interfaces;

namespace Hartonomous.Core.Repositories;

/// <summary>
/// Generic repository implementation using Entity Framework
/// Provides centralized data access with multi-tenant support
/// </summary>
public class Repository<T> : IRepository<T> where T : class
{
    protected readonly HartonomousDbContext _context;
    protected readonly DbSet<T> _dbSet;
    private readonly PropertyInfo? _userIdProperty;

    public Repository(HartonomousDbContext context)
    {
        _context = context;
        _dbSet = context.Set<T>();
        _userIdProperty = typeof(T).GetProperty("UserId");
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, string userId)
    {
        var query = _dbSet.AsQueryable();

        // Apply user filter if entity has UserId property
        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        // Apply ID filter
        var idProperty = GetIdProperty();
        if (idProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var idAccess = Expression.Property(parameter, idProperty);
            var idConstant = Expression.Constant(id);
            var idEquals = Expression.Equal(idAccess, idConstant);
            var idLambda = Expression.Lambda<Func<T, bool>>(idEquals, parameter);
            query = query.Where(idLambda);
        }

        return await query.FirstOrDefaultAsync();
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync(string userId)
    {
        var query = _dbSet.AsQueryable();

        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        return await query.ToListAsync();
    }

    public virtual async Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, string userId)
    {
        var query = _dbSet.Where(predicate);

        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        return await query.ToListAsync();
    }

    public virtual async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string userId)
    {
        var query = _dbSet.Where(predicate);

        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        return await query.FirstOrDefaultAsync();
    }

    public virtual async Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string userId,
        Expression<Func<T, bool>>? filter = null,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false)
    {
        var query = _dbSet.AsQueryable();

        // Apply user filter
        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        // Apply additional filter
        if (filter != null)
        {
            query = query.Where(filter);
        }

        // Get total count before paging
        var totalCount = await query.CountAsync();

        // Apply ordering
        if (orderBy != null)
        {
            query = descending ? query.OrderByDescending(orderBy) : query.OrderBy(orderBy);
        }

        // Apply paging
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public virtual async Task<T> AddAsync(T entity)
    {
        _dbSet.Add(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public virtual async Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities)
    {
        _dbSet.AddRange(entities);
        await _context.SaveChangesAsync();
        return entities;
    }

    public virtual async Task<T> UpdateAsync(T entity)
    {
        _dbSet.Update(entity);
        await _context.SaveChangesAsync();
        return entity;
    }

    public virtual async Task DeleteAsync(T entity)
    {
        _dbSet.Remove(entity);
        await _context.SaveChangesAsync();
    }

    public virtual async Task DeleteAsync(Guid id, string userId)
    {
        var entity = await GetByIdAsync(id, userId);
        if (entity != null)
        {
            _dbSet.Remove(entity);
            await _context.SaveChangesAsync();
        }
    }

    public virtual async Task DeleteRangeAsync(IEnumerable<T> entities)
    {
        _dbSet.RemoveRange(entities);
        await _context.SaveChangesAsync();
    }

    public virtual async Task<int> CountAsync(string userId)
    {
        var query = _dbSet.AsQueryable();

        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        return await query.CountAsync();
    }

    public virtual async Task<int> CountAsync(Expression<Func<T, bool>> predicate, string userId)
    {
        var query = _dbSet.Where(predicate);

        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        return await query.CountAsync();
    }

    public virtual async Task<bool> ExistsAsync(Guid id, string userId)
    {
        return await GetByIdAsync(id, userId) != null;
    }

    public virtual async Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, string userId)
    {
        var query = _dbSet.Where(predicate);

        if (_userIdProperty != null)
        {
            var parameter = Expression.Parameter(typeof(T), "x");
            var userIdAccess = Expression.Property(parameter, _userIdProperty);
            var userIdConstant = Expression.Constant(userId);
            var userIdEquals = Expression.Equal(userIdAccess, userIdConstant);
            var userIdLambda = Expression.Lambda<Func<T, bool>>(userIdEquals, parameter);
            query = query.Where(userIdLambda);
        }

        return await query.AnyAsync();
    }

    public virtual async Task<IEnumerable<T>> FromSqlAsync(string sql, params object[] parameters)
    {
        return await _dbSet.FromSqlRaw(sql, parameters).ToListAsync();
    }

    public virtual async Task<int> ExecuteSqlAsync(string sql, params object[] parameters)
    {
        return await _context.Database.ExecuteSqlRawAsync(sql, parameters);
    }

    // Legacy methods for backward compatibility
    public virtual async Task<Guid> CreateAsync(T entity, string userId)
    {
        if (_userIdProperty != null)
        {
            _userIdProperty.SetValue(entity, userId);
        }

        await AddAsync(entity);

        // Try to get the ID from the entity
        var idProperty = GetIdProperty();
        if (idProperty != null && idProperty.GetValue(entity) is Guid id)
        {
            return id;
        }

        return Guid.Empty;
    }

    public virtual async Task<bool> UpdateAsync(T entity, string userId)
    {
        try
        {
            if (_userIdProperty != null)
            {
                _userIdProperty.SetValue(entity, userId);
            }

            await UpdateAsync(entity);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public virtual async Task<bool> DeleteByIdAsync(Guid id, string userId)
    {
        try
        {
            await DeleteAsync(id, userId);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private PropertyInfo? GetIdProperty()
    {
        // Look for common ID property patterns
        var properties = typeof(T).GetProperties();
        return properties.FirstOrDefault(p =>
            p.Name.EndsWith("Id") && p.PropertyType == typeof(Guid)) ??
            properties.FirstOrDefault(p =>
                p.Name.Equals("Id", StringComparison.OrdinalIgnoreCase) && p.PropertyType == typeof(Guid));
    }
}