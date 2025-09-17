using Dapper;
using Hartonomous.Core.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Core.Repositories;

/// <summary>
/// Base repository implementation providing common functionality
/// </summary>
public abstract class BaseRepository<T> : IRepository<T> where T : class
{
    protected readonly string _connectionString;
    protected readonly ILogger _logger;
    protected abstract string TableName { get; }
    protected abstract string IdColumn { get; }

    protected BaseRepository(IConfiguration configuration, ILogger logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
        _logger = logger;
    }

    public virtual async Task<T?> GetByIdAsync(Guid id, string userId)
    {
        const string sql = @"
            SELECT * FROM {0}
            WHERE {1} = @Id AND UserId = @UserId;";

        var query = string.Format(sql, TableName, IdColumn);

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(query, new { Id = id, UserId = userId });

        return result != null ? MapFromDynamic(result) : null;
    }

    public virtual async Task<IEnumerable<T>> GetAllAsync(string userId)
    {
        const string sql = @"
            SELECT * FROM {0}
            WHERE UserId = @UserId
            ORDER BY CreatedAt DESC;";

        var query = string.Format(sql, TableName);

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(query, new { UserId = userId });

        return results.Select(MapFromDynamic);
    }

    public abstract Task<Guid> CreateAsync(T entity, string userId);
    public abstract Task<bool> UpdateAsync(T entity, string userId);

    public virtual async Task<bool> DeleteAsync(Guid id, string userId)
    {
        const string sql = @"
            DELETE FROM {0}
            WHERE {1} = @Id AND UserId = @UserId;";

        var query = string.Format(sql, TableName, IdColumn);

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(query, new { Id = id, UserId = userId });

        return rowsAffected > 0;
    }

    protected abstract T MapFromDynamic(dynamic row);

    protected virtual void LogError(Exception ex, string operation, params object[] args)
    {
        _logger.LogError(ex, $"Error in {operation} for {typeof(T).Name}", args);
    }

    protected virtual void LogInformation(string message, params object[] args)
    {
        _logger.LogInformation(message, args);
    }

    protected virtual void LogDebug(string message, params object[] args)
    {
        _logger.LogDebug(message, args);
    }
}