using Dapper;
using Hartonomous.MCP.DTOs;
using Hartonomous.MCP.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

/// <summary>
/// Repository implementation for MCP message management using Dapper
/// </summary>
public class MessageRepository : IMessageRepository
{
    private readonly string _connectionString;

    public MessageRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
    }

    public async Task<Guid> StoreMessageAsync(McpMessage message, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.McpMessages (MessageId, UserId, FromAgentId, ToAgentId, MessageType, Payload, Metadata, Timestamp)
            VALUES (@MessageId, @UserId, @FromAgentId, @ToAgentId, @MessageType, @Payload, @Metadata, @Timestamp);";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            MessageId = message.MessageId,
            UserId = userId,
            FromAgentId = message.FromAgentId,
            ToAgentId = message.ToAgentId,
            MessageType = message.MessageType,
            Payload = JsonSerializer.Serialize(message.Payload),
            Metadata = message.Metadata != null ? JsonSerializer.Serialize(message.Metadata) : null,
            Timestamp = message.Timestamp
        });

        return message.MessageId;
    }

    public async Task<McpMessage?> GetMessageAsync(Guid messageId, string userId)
    {
        const string sql = @"
            SELECT MessageId, FromAgentId, ToAgentId, MessageType, Payload, Metadata, Timestamp, ProcessedAt
            FROM dbo.McpMessages
            WHERE MessageId = @MessageId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { MessageId = messageId, UserId = userId });

        return result != null ? MapToMcpMessage(result) : null;
    }

    public async Task<IEnumerable<McpMessage>> GetMessagesForAgentAsync(Guid agentId, string userId, int limit = 100)
    {
        const string sql = @"
            SELECT TOP (@Limit) MessageId, FromAgentId, ToAgentId, MessageType, Payload, Metadata, Timestamp, ProcessedAt
            FROM dbo.McpMessages
            WHERE (FromAgentId = @AgentId OR ToAgentId = @AgentId) AND UserId = @UserId
            ORDER BY Timestamp DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { AgentId = agentId, UserId = userId, Limit = limit });

        return results.Select(MapToMcpMessage);
    }

    public async Task<IEnumerable<McpMessage>> GetConversationAsync(Guid fromAgentId, Guid toAgentId, string userId, int limit = 100)
    {
        const string sql = @"
            SELECT TOP (@Limit) MessageId, FromAgentId, ToAgentId, MessageType, Payload, Metadata, Timestamp, ProcessedAt
            FROM dbo.McpMessages
            WHERE ((FromAgentId = @FromAgentId AND ToAgentId = @ToAgentId) OR
                   (FromAgentId = @ToAgentId AND ToAgentId = @FromAgentId))
                  AND UserId = @UserId
            ORDER BY Timestamp DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new
        {
            FromAgentId = fromAgentId,
            ToAgentId = toAgentId,
            UserId = userId,
            Limit = limit
        });

        return results.Select(MapToMcpMessage);
    }

    public async Task<bool> MarkMessageProcessedAsync(Guid messageId, string userId)
    {
        const string sql = @"
            UPDATE dbo.McpMessages
            SET ProcessedAt = @ProcessedAt
            WHERE MessageId = @MessageId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            ProcessedAt = DateTime.UtcNow,
            MessageId = messageId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<McpMessage>> GetUnprocessedMessagesAsync(Guid agentId, string userId)
    {
        const string sql = @"
            SELECT MessageId, FromAgentId, ToAgentId, MessageType, Payload, Metadata, Timestamp, ProcessedAt
            FROM dbo.McpMessages
            WHERE ToAgentId = @AgentId AND UserId = @UserId AND ProcessedAt IS NULL
            ORDER BY Timestamp ASC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { AgentId = agentId, UserId = userId });

        return results.Select(MapToMcpMessage);
    }

    public async Task<Guid> StoreTaskAssignmentAsync(TaskAssignment task, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.TaskAssignments (TaskId, UserId, AgentId, TaskType, TaskData, Priority, DueDate, Metadata, CreatedAt)
            VALUES (@TaskId, @UserId, @AgentId, @TaskType, @TaskData, @Priority, @DueDate, @Metadata, @CreatedAt);";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            TaskId = task.TaskId,
            UserId = userId,
            AgentId = task.AgentId,
            TaskType = task.TaskType,
            TaskData = JsonSerializer.Serialize(task.TaskData),
            Priority = task.Priority,
            DueDate = task.DueDate,
            Metadata = task.Metadata != null ? JsonSerializer.Serialize(task.Metadata) : null,
            CreatedAt = DateTime.UtcNow
        });

        return task.TaskId;
    }

    public async Task<TaskAssignment?> GetTaskAssignmentAsync(Guid taskId, string userId)
    {
        const string sql = @"
            SELECT TaskId, AgentId, TaskType, TaskData, Priority, DueDate, Metadata
            FROM dbo.TaskAssignments
            WHERE TaskId = @TaskId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { TaskId = taskId, UserId = userId });

        return result != null ? MapToTaskAssignment(result) : null;
    }

    public async Task<IEnumerable<TaskAssignment>> GetPendingTasksForAgentAsync(Guid agentId, string userId)
    {
        const string sql = @"
            SELECT t.TaskId, t.AgentId, t.TaskType, t.TaskData, t.Priority, t.DueDate, t.Metadata
            FROM dbo.TaskAssignments t
            LEFT JOIN dbo.TaskResults r ON t.TaskId = r.TaskId
            WHERE t.AgentId = @AgentId AND t.UserId = @UserId AND r.TaskId IS NULL
            ORDER BY t.Priority DESC, t.CreatedAt ASC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { AgentId = agentId, UserId = userId });

        return results.Select(MapToTaskAssignment);
    }

    public async Task<bool> StoreTaskResultAsync(TaskResult result, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.TaskResults (TaskId, AgentId, UserId, Status, Result, ErrorMessage, Metrics, CompletedAt)
            VALUES (@TaskId, @AgentId, @UserId, @Status, @Result, @ErrorMessage, @Metrics, @CompletedAt);";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            TaskId = result.TaskId,
            AgentId = result.AgentId,
            UserId = userId,
            Status = (int)result.Status,
            Result = result.Result != null ? JsonSerializer.Serialize(result.Result) : null,
            ErrorMessage = result.ErrorMessage,
            Metrics = result.Metrics != null ? JsonSerializer.Serialize(result.Metrics) : null,
            CompletedAt = DateTime.UtcNow
        });

        return rowsAffected > 0;
    }

    public async Task<TaskResult?> GetTaskResultAsync(Guid taskId, string userId)
    {
        const string sql = @"
            SELECT TaskId, AgentId, Status, Result, ErrorMessage, Metrics
            FROM dbo.TaskResults
            WHERE TaskId = @TaskId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { TaskId = taskId, UserId = userId });

        return result != null ? MapToTaskResult(result) : null;
    }

    private static McpMessage MapToMcpMessage(dynamic row)
    {
        var payload = JsonSerializer.Deserialize<object>(row.Payload);
        var metadata = string.IsNullOrEmpty(row.Metadata)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.Metadata);

        return new McpMessage(
            row.MessageId,
            row.FromAgentId,
            row.ToAgentId,
            row.MessageType,
            payload!,
            metadata,
            row.Timestamp
        );
    }

    private static TaskAssignment MapToTaskAssignment(dynamic row)
    {
        var taskData = JsonSerializer.Deserialize<object>(row.TaskData);
        var metadata = string.IsNullOrEmpty(row.Metadata)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.Metadata);

        return new TaskAssignment(
            row.TaskId,
            row.AgentId,
            row.TaskType,
            taskData!,
            row.Priority,
            row.DueDate,
            metadata
        );
    }

    private static TaskResult MapToTaskResult(dynamic row)
    {
        var result = string.IsNullOrEmpty(row.Result)
            ? null
            : JsonSerializer.Deserialize<object>(row.Result);

        var metrics = string.IsNullOrEmpty(row.Metrics)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.Metrics);

        return new TaskResult(
            row.TaskId,
            row.AgentId,
            (TaskResultStatus)row.Status,
            result,
            row.ErrorMessage,
            metrics
        );
    }
}