/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the generic repository interface providing centralized multi-tenant data access.
 * Features comprehensive CRUD operations, filtering, paging, and user-scoped security patterns.
 */

using System.Linq.Expressions;

namespace Hartonomous.Core.Interfaces;

/// <summary>
/// Generic repository pattern interface for centralized data access
/// Provides common CRUD operations with multi-tenant support
/// </summary>
public interface IRepository<T> where T : class
{
    // Basic CRUD operations
    Task<T?> GetByIdAsync(Guid id, string userId);
    Task<IEnumerable<T>> GetAllAsync(string userId);
    Task<IEnumerable<T>> FindAsync(Expression<Func<T, bool>> predicate, string userId);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate, string userId);

    // Paging and filtering
    Task<(IEnumerable<T> Items, int TotalCount)> GetPagedAsync(
        int page,
        int pageSize,
        string userId,
        Expression<Func<T, bool>>? filter = null,
        Expression<Func<T, object>>? orderBy = null,
        bool descending = false);

    // Add/Update/Delete
    Task<T> AddAsync(T entity);
    Task<IEnumerable<T>> AddRangeAsync(IEnumerable<T> entities);
    Task<T> UpdateAsync(T entity);
    Task DeleteAsync(T entity);
    Task DeleteAsync(Guid id, string userId);
    Task DeleteRangeAsync(IEnumerable<T> entities);

    // Count operations
    Task<int> CountAsync(string userId);
    Task<int> CountAsync(Expression<Func<T, bool>> predicate, string userId);
    Task<bool> ExistsAsync(Guid id, string userId);
    Task<bool> ExistsAsync(Expression<Func<T, bool>> predicate, string userId);

    // Raw SQL operations
    Task<IEnumerable<T>> FromSqlAsync(string sql, params object[] parameters);
    Task<int> ExecuteSqlAsync(string sql, params object[] parameters);

    // Legacy methods for backward compatibility
    Task<Guid> CreateAsync(T entity, string userId);
    Task<bool> UpdateAsync(T entity, string userId);
    Task<bool> DeleteAsync(Guid id, string userId);
}