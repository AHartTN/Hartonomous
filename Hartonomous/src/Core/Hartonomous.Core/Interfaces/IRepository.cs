/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the generic repository interface for Entity Framework Core data access.
 */

using System.Linq.Expressions;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Generic repository interface for Entity Framework Core data access
/// Provides multi-tenant data operations with user-scoped filtering
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public interface IRepository<T> where T : class
{
    // Basic CRUD operations
    Task<T?> GetByIdAsync(Guid id, string userId);
    Task<IEnumerable<T>> GetAllAsync(string userId);
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, string userId);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string userId);

    // Paged queries
    Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string userId,
        Expression<Func<T, bool>>? filter = null,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false);

    // Create operations
    Task<T> AddAsync(T entity);
    Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities);

    // Update operations
    Task<T> UpdateAsync(T entity);

    // Delete operations
    Task DeleteAsync(T entity);
    Task DeleteAsync(Guid id, string userId);
    Task DeleteRangeAsync(IEnumerable<T> entities);

    // Count and existence checks
    Task<int> CountAsync(string userId);
    Task<int> CountAsync(Expression<Func<T, bool>> predicate, string userId);
    Task<bool> ExistsAsync(Guid id, string userId);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, string userId);

    // Raw SQL operations
    Task<IEnumerable<T>> FromSqlAsync(string sql, params object[] parameters);
    Task<int> ExecuteSqlAsync(string sql, params object[] parameters);

    // Legacy compatibility methods
    Task<Guid> CreateAsync(T entity, string userId);
    Task<bool> UpdateAsync(T entity, string userId);
    Task<bool> DeleteByIdAsync(Guid id, string userId);
}