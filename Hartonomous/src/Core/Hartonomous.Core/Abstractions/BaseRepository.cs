/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the abstract base repository implementing AI-native data access patterns.
 * Features advanced retry logic, connection management, and generic data operations optimized for SQL Server 2025.
 */

using Dapper;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using System.Data;
using System.Linq.Expressions;
using System.Text.Json;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Data;

namespace Hartonomous.Core.Abstractions;

/// <summary>
/// Abstract base repository implementing generic data access patterns
/// </summary>
/// <typeparam name="TEntity">Entity type</typeparam>
/// <typeparam name="TKey">Primary key type</typeparam>
public abstract class BaseRepository<TEntity, TKey> : IRepository<TEntity, TKey>
    where TEntity : class, IEntityBase<TKey>
    where TKey : IEquatable<TKey>
{
    protected readonly SqlServerOptions _sqlOptions;
    protected readonly string _connectionString;
    protected readonly string _tableName;
    protected readonly string _primaryKeyColumn;
    protected readonly HartonomousDbContext _context;

    protected BaseRepository(IOptions<SqlServerOptions> sqlOptions, HartonomousDbContext context)
    {
        _sqlOptions = sqlOptions.Value;
        _connectionString = _sqlOptions.ConnectionString;
        _tableName = GetTableName();
        _primaryKeyColumn = GetPrimaryKeyColumn();
        _context = context;
    }

    /// <summary>
    /// Get the database table name for this entity
    /// </summary>
    protected abstract string GetTableName();

    /// <summary>
    /// Get the primary key column name
    /// </summary>
    protected virtual string GetPrimaryKeyColumn() => "Id";

    /// <summary>
    /// Get all column names for SELECT statements
    /// </summary>
    protected abstract string GetSelectColumns();

    /// <summary>
    /// Get column names and parameters for INSERT statements
    /// </summary>
    protected abstract (string Columns, string Parameters) GetInsertColumnsAndParameters();

    /// <summary>
    /// Get SET clause for UPDATE statements
    /// </summary>
    protected abstract string GetUpdateSetClause();

    /// <summary>
    /// Map database row to entity
    /// </summary>
    protected abstract TEntity MapToEntity(dynamic row);

    /// <summary>
    /// Get parameters for entity operations
    /// </summary>
    protected abstract object GetParameters(TEntity entity);

    /// <summary>
    /// Get parameters as array for EF Core SqlQueryRaw operations
    /// </summary>
    protected abstract object[] GetParametersArray(TEntity entity);

    /// <summary>
    /// Create database connection
    /// </summary>
    protected virtual IDbConnection CreateConnection()
    {
        return new SqlConnection(_connectionString);
    }

    /// <summary>
    /// Execute with proper error handling and retry logic
    /// </summary>
    protected virtual async Task<T> ExecuteWithRetryAsync<T>(Func<IDbConnection, Task<T>> operation)
    {
        var retryCount = 0;
        var maxRetries = _sqlOptions.EnableRetryOnFailure ? _sqlOptions.MaxRetryCount : 1;

        while (retryCount < maxRetries)
        {
            try
            {
                using var connection = CreateConnection();
                connection.Open();
                return await operation(connection);
            }
            catch (SqlException ex) when (retryCount < maxRetries - 1 && IsTransientError(ex))
            {
                retryCount++;
                var delay = TimeSpan.FromMilliseconds(Math.Pow(2, retryCount) * 100);
                await Task.Delay(delay);
            }
        }

        // Final attempt without retry
        using var finalConnection = CreateConnection();
        finalConnection.Open();
        return await operation(finalConnection);
    }

    /// <summary>
    /// Check if SQL exception is transient and retryable
    /// </summary>
    protected virtual bool IsTransientError(SqlException ex)
    {
        // Common transient error codes
        var transientErrorCodes = new[] { 2, 53, 121, 233, 997, 1204, 1205, 1222, 8645, 8651 };
        return transientErrorCodes.Contains(ex.Number);
    }

    #region IRepository Implementation

    public virtual async Task<TEntity?> GetByIdAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT {GetSelectColumns()}
            FROM {_tableName}
            WHERE {_primaryKeyColumn} = @Id";

        return await ExecuteWithRetryAsync(async connection =>
        {
            var results = await _context.Database.SqlQueryRaw<TEntity>(sql, id).ToListAsync();
            return results.FirstOrDefault();
        });
    }

    public virtual async Task<IEnumerable<TEntity>> GetByUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT {GetSelectColumns()}
            FROM {_tableName}
            WHERE UserId = @UserId
            ORDER BY CreatedDate DESC";

        return await ExecuteWithRetryAsync(async connection =>
        {
            return await _context.Database.SqlQueryRaw<TEntity>(sql, userId).ToListAsync();
        });
    }

    public virtual async Task<IEnumerable<TEntity>> GetAsync(
        Expression<Func<TEntity, bool>>? filter = null,
        int skip = 0,
        int take = 100,
        CancellationToken cancellationToken = default)
    {
        var sql = $@"
            SELECT {GetSelectColumns()}
            FROM {_tableName}";

        var whereClause = BuildWhereClause(filter);
        if (!string.IsNullOrEmpty(whereClause))
            sql += $" WHERE {whereClause}";

        sql += $@"
            ORDER BY CreatedDate DESC
            OFFSET @Skip ROWS
            FETCH NEXT @Take ROWS ONLY";

        return await ExecuteWithRetryAsync(async connection =>
        {
            return await _context.Database.SqlQueryRaw<TEntity>(sql, skip, take).ToListAsync();
        });
    }

    public virtual async Task<TKey> CreateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        entity.CreatedDate = DateTime.UtcNow;
        entity.ModifiedDate = null;

        var (columns, parameters) = GetInsertColumnsAndParameters();
        var sql = $@"
            INSERT INTO {_tableName} ({columns})
            VALUES ({parameters});
            SELECT CAST(SCOPE_IDENTITY() AS {GetKeyTypeSql()});";

        return await ExecuteWithRetryAsync(async connection =>
        {
            var results = await _context.Database.SqlQueryRaw<TKey>(sql, GetParametersArray(entity)).ToListAsync();
            var id = results.FirstOrDefault();
            entity.Id = id;
            return id;
        });
    }

    public virtual async Task<bool> UpdateAsync(TEntity entity, CancellationToken cancellationToken = default)
    {
        entity.ModifiedDate = DateTime.UtcNow;

        var sql = $@"
            UPDATE {_tableName}
            SET {GetUpdateSetClause()}
            WHERE {_primaryKeyColumn} = @Id";

        return await ExecuteWithRetryAsync(async connection =>
        {
            var rowsAffected = await _context.Database.ExecuteSqlRawAsync(sql, GetParametersArray(entity));
            return rowsAffected > 0;
        });
    }

    public virtual async Task<bool> DeleteAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var sql = $"DELETE FROM {_tableName} WHERE {_primaryKeyColumn} = @Id";

        return await ExecuteWithRetryAsync(async connection =>
        {
            var rowsAffected = await _context.Database.ExecuteSqlRawAsync(sql, id);
            return rowsAffected > 0;
        });
    }

    public virtual async Task<bool> ExistsAsync(TKey id, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT COUNT(1) FROM {_tableName} WHERE {_primaryKeyColumn} = @Id";

        return await ExecuteWithRetryAsync(async connection =>
        {
            var results = await _context.Database.SqlQueryRaw<int>(sql, id).ToListAsync();
            return results.FirstOrDefault() > 0;
        });
    }

    public virtual async Task<int> CountAsync(Expression<Func<TEntity, bool>>? filter = null, CancellationToken cancellationToken = default)
    {
        var sql = $"SELECT COUNT(1) FROM {_tableName}";

        var whereClause = BuildWhereClause(filter);
        if (!string.IsNullOrEmpty(whereClause))
            sql += $" WHERE {whereClause}";

        return await ExecuteWithRetryAsync(async connection =>
        {
            var results = await _context.Database.SqlQueryRaw<int>(sql).ToListAsync();
            return results.FirstOrDefault();
        });
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Build WHERE clause from expression (basic implementation)
    /// </summary>
    protected virtual string BuildWhereClause(Expression<Func<TEntity, bool>>? filter)
    {
        // Basic implementation - override for complex filtering
        return string.Empty;
    }

    /// <summary>
    /// Get SQL type for primary key
    /// </summary>
    protected virtual string GetKeyTypeSql()
    {
        return typeof(TKey) switch
        {
            Type t when t == typeof(int) => "INT",
            Type t when t == typeof(long) => "BIGINT",
            Type t when t == typeof(Guid) => "UNIQUEIDENTIFIER",
            Type t when t == typeof(string) => "NVARCHAR(MAX)",
            _ => "NVARCHAR(MAX)"
        };
    }

    /// <summary>
    /// Serialize object to JSON for storage
    /// </summary>
    protected virtual string SerializeToJson(object obj)
    {
        return JsonSerializer.Serialize(obj, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    /// <summary>
    /// Deserialize JSON to object
    /// </summary>
    protected virtual T? DeserializeFromJson<T>(string json)
    {
        if (string.IsNullOrEmpty(json))
            return default;

        return JsonSerializer.Deserialize<T>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    #endregion
}