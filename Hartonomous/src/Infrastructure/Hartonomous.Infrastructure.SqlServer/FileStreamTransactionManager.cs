using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.SqlServer;

/// <summary>
/// Manages transactions for FILESTREAM operations with proper isolation and rollback capabilities
/// Ensures data consistency for multi-GB model file operations
/// </summary>
public class FileStreamTransactionManager : IDisposable
{
    private readonly SqlConnection _connection;
    private readonly ILogger<FileStreamTransactionManager> _logger;
    private SqlTransaction? _transaction;
    private bool _disposed;

    public FileStreamTransactionManager(
        string connectionString,
        ILogger<FileStreamTransactionManager> logger)
    {
        _connection = new SqlConnection(connectionString);
        _logger = logger;
    }

    /// <summary>
    /// Begin a new transaction with appropriate isolation level for FILESTREAM operations
    /// </summary>
    public async Task<FileStreamTransactionContext> BeginTransactionAsync(
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        try
        {
            if (_connection.State != ConnectionState.Open)
            {
                await _connection.OpenAsync();
            }

            _transaction = await _connection.BeginTransactionAsync(isolationLevel);

            _logger.LogDebug("FILESTREAM transaction started with isolation level: {IsolationLevel}",
                isolationLevel);

            return new FileStreamTransactionContext(_transaction, _connection, _logger);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to begin FILESTREAM transaction");
            throw;
        }
    }

    /// <summary>
    /// Execute a FILESTREAM operation within a transactional context
    /// Provides automatic rollback on failure
    /// </summary>
    public async Task<T> ExecuteInTransactionAsync<T>(
        Func<FileStreamTransactionContext, Task<T>> operation,
        IsolationLevel isolationLevel = IsolationLevel.ReadCommitted)
    {
        using var context = await BeginTransactionAsync(isolationLevel);

        try
        {
            var result = await operation(context);
            await context.CommitAsync();
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "FILESTREAM transaction operation failed, rolling back");
            await context.RollbackAsync();
            throw;
        }
    }

    /// <summary>
    /// Execute multiple FILESTREAM operations in a single transaction
    /// Ensures atomicity across all operations
    /// </summary>
    public async Task ExecuteMultipleInTransactionAsync(
        params Func<FileStreamTransactionContext, Task>[] operations)
    {
        using var context = await BeginTransactionAsync(IsolationLevel.ReadCommitted);

        try
        {
            foreach (var operation in operations)
            {
                await operation(context);
            }

            await context.CommitAsync();
            _logger.LogInformation("Multiple FILESTREAM operations completed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Multiple FILESTREAM operations failed, rolling back all changes");
            await context.RollbackAsync();
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _transaction?.Dispose();
            _connection?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Transactional context for FILESTREAM operations
/// Provides access to connection, transaction, and transaction context
/// </summary>
public class FileStreamTransactionContext : IDisposable
{
    private readonly SqlTransaction _transaction;
    private readonly SqlConnection _connection;
    private readonly ILogger _logger;
    private bool _disposed;
    private bool _committed;
    private bool _rolledBack;

    internal FileStreamTransactionContext(
        SqlTransaction transaction,
        SqlConnection connection,
        ILogger logger)
    {
        _transaction = transaction;
        _connection = connection;
        _logger = logger;
    }

    public SqlConnection Connection => _connection;
    public SqlTransaction Transaction => _transaction;

    /// <summary>
    /// Get the transaction context required for SqlFileStream operations
    /// </summary>
    public byte[] GetTransactionContext()
    {
        try
        {
            using var command = new SqlCommand("SELECT GET_FILESTREAM_TRANSACTION_CONTEXT()", _connection, _transaction);
            var result = command.ExecuteScalar();

            if (result == null || result == DBNull.Value)
            {
                throw new InvalidOperationException("Failed to get FILESTREAM transaction context");
            }

            return (byte[])result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get FILESTREAM transaction context");
            throw;
        }
    }

    /// <summary>
    /// Execute a SQL command within this transaction context
    /// </summary>
    public async Task<T> ExecuteScalarAsync<T>(string sql, params SqlParameter[] parameters)
    {
        using var command = new SqlCommand(sql, _connection, _transaction);
        command.Parameters.AddRange(parameters);

        var result = await command.ExecuteScalarAsync();
        return (T)result;
    }

    /// <summary>
    /// Execute a SQL command within this transaction context
    /// </summary>
    public async Task<int> ExecuteNonQueryAsync(string sql, params SqlParameter[] parameters)
    {
        using var command = new SqlCommand(sql, _connection, _transaction);
        command.Parameters.AddRange(parameters);

        return await command.ExecuteNonQueryAsync();
    }

    /// <summary>
    /// Execute a SQL reader within this transaction context
    /// </summary>
    public async Task<SqlDataReader> ExecuteReaderAsync(string sql, params SqlParameter[] parameters)
    {
        var command = new SqlCommand(sql, _connection, _transaction);
        command.Parameters.AddRange(parameters);

        return await command.ExecuteReaderAsync();
    }

    /// <summary>
    /// Get FILESTREAM path for a specific record
    /// </summary>
    public async Task<string> GetFileStreamPathAsync(string tableName, string guidColumn, Guid recordId)
    {
        var sql = $@"
            SELECT {tableName}.{guidColumn}.PathName()
            FROM {tableName}
            WHERE {guidColumn.Replace("Data", "Id")} = @RecordId";

        using var command = new SqlCommand(sql, _connection, _transaction);
        command.Parameters.Add("@RecordId", SqlDbType.UniqueIdentifier).Value = recordId;

        var result = await command.ExecuteScalarAsync();

        if (result == null || result == DBNull.Value)
        {
            throw new FileNotFoundException($"FILESTREAM path not found for record: {recordId}");
        }

        return (string)result;
    }

    /// <summary>
    /// Commit the transaction
    /// </summary>
    public async Task CommitAsync()
    {
        if (_committed || _rolledBack)
        {
            throw new InvalidOperationException("Transaction has already been completed");
        }

        try
        {
            await _transaction.CommitAsync();
            _committed = true;
            _logger.LogDebug("FILESTREAM transaction committed successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to commit FILESTREAM transaction");
            throw;
        }
    }

    /// <summary>
    /// Rollback the transaction
    /// </summary>
    public async Task RollbackAsync()
    {
        if (_committed || _rolledBack)
        {
            return; // Already completed
        }

        try
        {
            await _transaction.RollbackAsync();
            _rolledBack = true;
            _logger.LogDebug("FILESTREAM transaction rolled back successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to rollback FILESTREAM transaction");
            throw;
        }
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (!_committed && !_rolledBack)
            {
                try
                {
                    _transaction.Rollback();
                    _logger.LogWarning("FILESTREAM transaction was disposed without explicit commit/rollback - rolled back");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to rollback transaction during dispose");
                }
            }

            _transaction?.Dispose();
            _disposed = true;
        }
    }
}

/// <summary>
/// Extension methods for FILESTREAM transaction operations
/// </summary>
public static class FileStreamTransactionExtensions
{
    /// <summary>
    /// Write data to FILESTREAM using SqlFileStream within a transaction
    /// </summary>
    public static async Task<long> WriteToFileStreamAsync(
        this FileStreamTransactionContext context,
        string fileStreamPath,
        Stream sourceData,
        ILogger? logger = null)
    {
        try
        {
            var transactionContext = context.GetTransactionContext();

            using var fileStream = new SqlFileStream(fileStreamPath, transactionContext, FileAccess.Write);
            await sourceData.CopyToAsync(fileStream);
            await fileStream.FlushAsync();

            logger?.LogDebug("Successfully wrote {Bytes} bytes to FILESTREAM path: {Path}",
                fileStream.Length, fileStreamPath);

            return fileStream.Length;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to write to FILESTREAM path: {Path}", fileStreamPath);
            throw;
        }
    }

    /// <summary>
    /// Read data from FILESTREAM using SqlFileStream within a transaction
    /// </summary>
    public static async Task<Stream> ReadFromFileStreamAsync(
        this FileStreamTransactionContext context,
        string fileStreamPath,
        ILogger? logger = null)
    {
        try
        {
            var transactionContext = context.GetTransactionContext();

            var fileStream = new SqlFileStream(fileStreamPath, transactionContext, FileAccess.Read);

            logger?.LogDebug("Successfully opened FILESTREAM for reading: {Path}, Size: {Size} bytes",
                fileStreamPath, fileStream.Length);

            return fileStream;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to read from FILESTREAM path: {Path}", fileStreamPath);
            throw;
        }
    }

    /// <summary>
    /// Validate FILESTREAM data integrity within a transaction
    /// </summary>
    public static async Task<bool> ValidateFileStreamIntegrityAsync(
        this FileStreamTransactionContext context,
        Guid modelId,
        string expectedHash,
        ILogger? logger = null)
    {
        try
        {
            var fileStreamPath = await context.GetFileStreamPathAsync("ModelFiles", "ModelData", modelId);

            using var fileStream = await context.ReadFromFileStreamAsync(fileStreamPath, logger);
            using var sha256 = System.Security.Cryptography.SHA256.Create();

            var computedHash = await sha256.ComputeHashAsync(fileStream);
            var computedHashString = Convert.ToHexString(computedHash);

            var isValid = string.Equals(expectedHash, computedHashString, StringComparison.OrdinalIgnoreCase);

            logger?.LogDebug("FILESTREAM integrity validation for {ModelId}: {Result} (Expected: {Expected}, Computed: {Computed})",
                modelId, isValid ? "PASSED" : "FAILED", expectedHash, computedHashString);

            return isValid;
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Failed to validate FILESTREAM integrity for model {ModelId}", modelId);
            return false;
        }
    }
}