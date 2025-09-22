using System.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Extensions.Http;

namespace Hartonomous.Infrastructure.SqlServer;

/// <summary>
/// Comprehensive error handling and retry policies for FILESTREAM operations
/// Handles transient failures, disk space issues, and transaction conflicts
/// </summary>
public class FileStreamErrorHandler
{
    private readonly ILogger<FileStreamErrorHandler> _logger;
    private readonly FileStreamRetryPolicy _retryPolicy;

    public FileStreamErrorHandler(ILogger<FileStreamErrorHandler> logger)
    {
        _logger = logger;
        _retryPolicy = new FileStreamRetryPolicy(logger);
    }

    /// <summary>
    /// Execute FILESTREAM operation with comprehensive error handling and retry
    /// </summary>
    public async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> operation,
        string operationName,
        Guid? modelId = null,
        int maxRetries = 3)
    {
        return await _retryPolicy.GetRetryPolicy<T>(maxRetries).ExecuteAsync(async () =>
        {
            try
            {
                _logger.LogDebug("Executing FILESTREAM operation: {Operation} for model {ModelId}",
                    operationName, modelId);

                return await operation();
            }
            catch (Exception ex)
            {
                var errorInfo = AnalyzeException(ex, operationName, modelId);
                _logger.LogError(ex, "FILESTREAM operation failed: {Operation}, Error: {ErrorType}, Model: {ModelId}",
                    operationName, errorInfo.ErrorType, modelId);

                if (!errorInfo.IsRetryable)
                {
                    throw new FileStreamOperationException(
                        $"Non-retryable error in {operationName}: {errorInfo.ErrorType}",
                        ex, errorInfo.ErrorType, modelId);
                }

                throw; // Re-throw for retry policy to handle
            }
        });
    }

    /// <summary>
    /// Analyze exception to determine error type and retry strategy
    /// </summary>
    public FileStreamErrorInfo AnalyzeException(Exception exception, string operation, Guid? modelId)
    {
        return exception switch
        {
            SqlException sqlEx => AnalyzeSqlException(sqlEx, operation, modelId),
            IOException ioEx => AnalyzeIOException(ioEx, operation, modelId),
            UnauthorizedAccessException accessEx => AnalyzeAccessException(accessEx, operation, modelId),
            InvalidOperationException invalidEx => AnalyzeInvalidOperationException(invalidEx, operation, modelId),
            OutOfMemoryException memEx => new FileStreamErrorInfo(
                FileStreamErrorType.OutOfMemory, false, "Insufficient memory for operation", memEx),
            _ => new FileStreamErrorInfo(
                FileStreamErrorType.Unknown, false, "Unknown error occurred", exception)
        };
    }

    private FileStreamErrorInfo AnalyzeSqlException(SqlException sqlEx, string operation, Guid? modelId)
    {
        return sqlEx.Number switch
        {
            // Deadlock victim
            1205 => new FileStreamErrorInfo(
                FileStreamErrorType.DeadlockVictim, true, "Transaction was deadlock victim", sqlEx),

            // Timeout expired
            -2 => new FileStreamErrorInfo(
                FileStreamErrorType.Timeout, true, "SQL Server timeout expired", sqlEx),

            // Connection issues
            2 or 53 or 40 => new FileStreamErrorInfo(
                FileStreamErrorType.ConnectionFailure, true, "SQL Server connection failure", sqlEx),

            // Lock timeout
            1222 => new FileStreamErrorInfo(
                FileStreamErrorType.LockTimeout, true, "Lock request timeout", sqlEx),

            // FILESTREAM specific errors
            25016 => new FileStreamErrorInfo(
                FileStreamErrorType.FileStreamNotEnabled, false, "FILESTREAM not enabled", sqlEx),

            25017 => new FileStreamErrorInfo(
                FileStreamErrorType.FileStreamPathInvalid, false, "Invalid FILESTREAM path", sqlEx),

            // Transaction log full
            9002 => new FileStreamErrorInfo(
                FileStreamErrorType.TransactionLogFull, true, "Transaction log is full", sqlEx),

            // Disk space issues
            1105 => new FileStreamErrorInfo(
                FileStreamErrorType.DiskSpaceFull, false, "Insufficient disk space", sqlEx),

            _ => new FileStreamErrorInfo(
                FileStreamErrorType.SqlServerError, false, $"SQL Server error {sqlEx.Number}: {sqlEx.Message}", sqlEx)
        };
    }

    private FileStreamErrorInfo AnalyzeIOException(IOException ioEx, string operation, Guid? modelId)
    {
        var message = ioEx.Message.ToLowerInvariant();

        if (message.Contains("disk") && message.Contains("space"))
        {
            return new FileStreamErrorInfo(
                FileStreamErrorType.DiskSpaceFull, false, "Insufficient disk space", ioEx);
        }

        if (message.Contains("access") && message.Contains("denied"))
        {
            return new FileStreamErrorInfo(
                FileStreamErrorType.AccessDenied, false, "File system access denied", ioEx);
        }

        if (message.Contains("timeout") || message.Contains("network"))
        {
            return new FileStreamErrorInfo(
                FileStreamErrorType.NetworkError, true, "Network or I/O timeout", ioEx);
        }

        return new FileStreamErrorInfo(
            FileStreamErrorType.IOError, true, "General I/O error", ioEx);
    }

    private FileStreamErrorInfo AnalyzeAccessException(UnauthorizedAccessException accessEx, string operation, Guid? modelId)
    {
        return new FileStreamErrorInfo(
            FileStreamErrorType.AccessDenied, false, "Unauthorized access to FILESTREAM", accessEx);
    }

    private FileStreamErrorInfo AnalyzeInvalidOperationException(InvalidOperationException invalidEx, string operation, Guid? modelId)
    {
        var message = invalidEx.Message.ToLowerInvariant();

        if (message.Contains("transaction") && message.Contains("context"))
        {
            return new FileStreamErrorInfo(
                FileStreamErrorType.TransactionContextInvalid, true, "Invalid transaction context", invalidEx);
        }

        if (message.Contains("filestream") && message.Contains("path"))
        {
            return new FileStreamErrorInfo(
                FileStreamErrorType.FileStreamPathInvalid, false, "Invalid FILESTREAM path", invalidEx);
        }

        return new FileStreamErrorInfo(
            FileStreamErrorType.InvalidOperation, false, "Invalid operation", invalidEx);
    }

    /// <summary>
    /// Handle specific cleanup operations after failed FILESTREAM operation
    /// </summary>
    public async Task HandleFailureCleanupAsync(
        FileStreamErrorInfo errorInfo,
        Guid? modelId,
        string connectionString)
    {
        try
        {
            switch (errorInfo.ErrorType)
            {
                case FileStreamErrorType.DiskSpaceFull:
                    await CleanupTemporaryFilesAsync(connectionString);
                    break;

                case FileStreamErrorType.TransactionLogFull:
                    await TriggerLogBackupAsync(connectionString);
                    break;

                case FileStreamErrorType.DeadlockVictim:
                case FileStreamErrorType.LockTimeout:
                    await CleanupStaleSessionsAsync(connectionString);
                    break;

                case FileStreamErrorType.FileStreamPathInvalid:
                    if (modelId.HasValue)
                    {
                        await MarkModelAsFailedAsync(modelId.Value, errorInfo.Description, connectionString);
                    }
                    break;
            }

            _logger.LogInformation("Completed failure cleanup for error type: {ErrorType}", errorInfo.ErrorType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to perform cleanup for error type: {ErrorType}", errorInfo.ErrorType);
        }
    }

    private async Task CleanupTemporaryFilesAsync(string connectionString)
    {
        _logger.LogInformation("Cleaning up temporary FILESTREAM files due to disk space issues");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();

        var cleanupQuery = @"
            DELETE FROM ModelProcessingCache
            WHERE ExpiresAt < GETUTCDATE()
               OR (AccessCount < 2 AND CreatedAt < DATEADD(HOUR, -1, GETUTCDATE()));

            DELETE FROM ModelStreamingSessions
            WHERE ExpiresAt < GETUTCDATE()
               OR Status = 'Failed'
               OR LastActivityAt < DATEADD(HOUR, -2, GETUTCDATE());";

        using var command = new Microsoft.Data.SqlClient.SqlCommand(cleanupQuery, connection);
        var rowsDeleted = await command.ExecuteNonQueryAsync();

        _logger.LogInformation("Cleaned up {RowsDeleted} temporary FILESTREAM records", rowsDeleted);
    }

    private async Task TriggerLogBackupAsync(string connectionString)
    {
        _logger.LogWarning("Transaction log is full - requesting log backup");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();

        // In production, this would trigger an alert to DBAs
        // For now, we'll just log the issue
        var logSizeQuery = @"
            SELECT
                name,
                size * 8 / 1024 AS SizeMB,
                CAST(FILEPROPERTY(name, 'SpaceUsed') AS int) * 8 / 1024 AS UsedMB
            FROM sys.database_files
            WHERE type_desc = 'LOG'";

        using var command = new Microsoft.Data.SqlClient.SqlCommand(logSizeQuery, connection);
        using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var logName = reader.GetString("name");
            var sizeMB = reader.GetInt32("SizeMB");
            var usedMB = reader.GetInt32("UsedMB");

            _logger.LogWarning("Transaction log {LogName}: {UsedMB}MB used of {SizeMB}MB ({Percentage:F1}%)",
                logName, usedMB, sizeMB, (double)usedMB / sizeMB * 100);
        }
    }

    private async Task CleanupStaleSessionsAsync(string connectionString)
    {
        _logger.LogInformation("Cleaning up stale FILESTREAM sessions due to locking issues");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();

        var cleanupQuery = @"
            UPDATE ModelStreamingSessions
            SET Status = 'Failed', ExpiresAt = GETUTCDATE()
            WHERE Status = 'Active'
              AND LastActivityAt < DATEADD(MINUTE, -30, GETUTCDATE());";

        using var command = new Microsoft.Data.SqlClient.SqlCommand(cleanupQuery, connection);
        var rowsUpdated = await command.ExecuteNonQueryAsync();

        _logger.LogInformation("Marked {RowsUpdated} stale sessions as failed", rowsUpdated);
    }

    private async Task MarkModelAsFailedAsync(Guid modelId, string errorMessage, string connectionString)
    {
        _logger.LogInformation("Marking model {ModelId} as failed due to non-recoverable error", modelId);

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();

        var updateQuery = @"
            UPDATE ModelFiles
            SET Status = 'Failed',
                CompletedAt = GETUTCDATE()
            WHERE ModelId = @ModelId";

        using var command = new Microsoft.Data.SqlClient.SqlCommand(updateQuery, connection);
        command.Parameters.AddWithValue("@ModelId", modelId);

        await command.ExecuteNonQueryAsync();
    }
}

/// <summary>
/// Retry policy configuration for FILESTREAM operations
/// </summary>
public class FileStreamRetryPolicy
{
    private readonly ILogger _logger;

    public FileStreamRetryPolicy(ILogger logger)
    {
        _logger = logger;
    }

    public IAsyncPolicy<T> GetRetryPolicy<T>(int maxRetries = 3)
    {
        return Policy
            .Handle<SqlException>(ex => IsRetryableSqlException(ex))
            .Or<IOException>(ex => IsRetryableIOException(ex))
            .Or<InvalidOperationException>(ex => IsRetryableInvalidOperation(ex))
            .WaitAndRetryAsync(
                retryCount: maxRetries,
                sleepDurationProvider: retryAttempt => TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)), // Exponential backoff
                onRetry: (outcome, timespan, retryCount, context) =>
                {
                    _logger.LogWarning("FILESTREAM operation retry {RetryCount}/{MaxRetries} after {Delay}ms due to: {Exception}",
                        retryCount, maxRetries, timespan.TotalMilliseconds, outcome.Exception?.Message);
                });
    }

    private static bool IsRetryableSqlException(SqlException ex)
    {
        return ex.Number switch
        {
            1205 => true, // Deadlock victim
            -2 => true,   // Timeout
            2 or 53 or 40 => true, // Connection issues
            1222 => true, // Lock timeout
            9002 => true, // Transaction log full
            _ => false
        };
    }

    private static bool IsRetryableIOException(IOException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("timeout") || message.Contains("network");
    }

    private static bool IsRetryableInvalidOperation(InvalidOperationException ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("transaction") && message.Contains("context");
    }
}

/// <summary>
/// Information about a FILESTREAM operation error
/// </summary>
public record FileStreamErrorInfo(
    FileStreamErrorType ErrorType,
    bool IsRetryable,
    string Description,
    Exception OriginalException);

/// <summary>
/// Types of FILESTREAM operation errors
/// </summary>
public enum FileStreamErrorType
{
    Unknown,
    SqlServerError,
    DeadlockVictim,
    Timeout,
    ConnectionFailure,
    LockTimeout,
    FileStreamNotEnabled,
    FileStreamPathInvalid,
    TransactionLogFull,
    DiskSpaceFull,
    AccessDenied,
    NetworkError,
    IOError,
    TransactionContextInvalid,
    InvalidOperation,
    OutOfMemory
}

/// <summary>
/// Exception thrown by FILESTREAM operations with enhanced error information
/// </summary>
public class FileStreamOperationException : Exception
{
    public FileStreamErrorType ErrorType { get; }
    public Guid? ModelId { get; }

    public FileStreamOperationException(
        string message,
        Exception innerException,
        FileStreamErrorType errorType,
        Guid? modelId = null)
        : base(message, innerException)
    {
        ErrorType = errorType;
        ModelId = modelId;
    }
}