/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow event repository - a focused, purpose-built class.
 * Features debugging, events, and breakpoint management with clean separation of concerns.
 */

using Dapper;
using Hartonomous.Core.Configuration;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Hartonomous.Orchestration.Repositories;

/// <summary>
/// Purpose-built repository for workflow events, debugging, and breakpoints
/// </summary>
public class WorkflowEventRepository
{
    private readonly string _connectionString;
    private readonly ILogger<WorkflowEventRepository> _logger;

    public WorkflowEventRepository(IOptions<SqlServerOptions> sqlOptions, ILogger<WorkflowEventRepository> logger)
    {
        _connectionString = sqlOptions.Value.ConnectionString;
        _logger = logger;
    }

    public async Task<bool> CreateWorkflowEventAsync(Guid executionId, DebugEvent debugEvent)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowEvents (EventId, ExecutionId, EventType, NodeId, Timestamp, DataJson, Message, Level)
            VALUES (@EventId, @ExecutionId, @EventType, @NodeId, @Timestamp, @DataJson, @Message, @Level);";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            EventId = debugEvent.EventId,
            ExecutionId = executionId,
            EventType = debugEvent.EventType,
            NodeId = debugEvent.NodeId,
            Timestamp = debugEvent.Timestamp,
            DataJson = JsonSerializer.Serialize(debugEvent.Data),
            Message = debugEvent.Message,
            Level = "Info"
        });

        return rowsAffected > 0;
    }

    public async Task<List<DebugEvent>> GetWorkflowEventsAsync(Guid executionId, DateTime? since = null)
    {
        var whereClause = "WHERE ExecutionId = @ExecutionId";
        var parameters = new DynamicParameters();
        parameters.Add("ExecutionId", executionId);

        if (since.HasValue)
        {
            whereClause += " AND Timestamp >= @Since";
            parameters.Add("Since", since.Value);
        }

        var sql = $@"
            SELECT EventId, EventType, NodeId, Timestamp, DataJson, Message
            FROM dbo.WorkflowEvents
            {whereClause}
            ORDER BY Timestamp DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, parameters);

        return results.Select(r => new DebugEvent(
            r.EventId,
            r.EventType,
            r.NodeId,
            r.Timestamp,
            JsonSerializer.Deserialize<Dictionary<string, object>>(r.DataJson ?? "{}") ?? new Dictionary<string, object>(),
            r.Message
        )).ToList();
    }

    public async Task<bool> CreateBreakpointAsync(Guid executionId, BreakpointDto breakpoint, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowBreakpoints (BreakpointId, ExecutionId, NodeId, Condition, IsEnabled, CreatedAt, CreatedBy)
            VALUES (@BreakpointId, @ExecutionId, @NodeId, @Condition, @IsEnabled, @CreatedAt, @CreatedBy);";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            BreakpointId = breakpoint.BreakpointId,
            ExecutionId = executionId,
            NodeId = breakpoint.NodeId,
            Condition = breakpoint.Condition,
            IsEnabled = breakpoint.IsEnabled,
            CreatedAt = breakpoint.CreatedAt,
            CreatedBy = userId
        });

        return rowsAffected > 0;
    }

    public async Task<bool> RemoveBreakpointAsync(Guid breakpointId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.WorkflowBreakpoints
            WHERE BreakpointId = @BreakpointId AND CreatedBy = @CreatedBy;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { BreakpointId = breakpointId, CreatedBy = userId });

        return rowsAffected > 0;
    }

    public async Task<List<BreakpointDto>> GetBreakpointsByExecutionAsync(Guid executionId, string userId)
    {
        const string sql = @"
            SELECT BreakpointId, NodeId, Condition, IsEnabled, CreatedAt
            FROM dbo.WorkflowBreakpoints
            WHERE ExecutionId = @ExecutionId AND CreatedBy = @CreatedBy;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { ExecutionId = executionId, CreatedBy = userId });

        return results.Select(r => new BreakpointDto(
            r.BreakpointId,
            r.NodeId,
            r.Condition,
            r.IsEnabled,
            r.CreatedAt
        )).ToList();
    }

    public async Task<List<DebugEvent>> GetRecentEventsAsync(string userId, int limit = 100)
    {
        const string sql = @"
            SELECT TOP(@Limit) e.EventId, e.EventType, e.NodeId, e.Timestamp, e.DataJson, e.Message
            FROM dbo.WorkflowEvents e
            INNER JOIN dbo.WorkflowExecutions ex ON e.ExecutionId = ex.ExecutionId
            WHERE ex.UserId = @UserId
            ORDER BY e.Timestamp DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { UserId = userId, Limit = limit });

        return results.Select(r => new DebugEvent(
            r.EventId,
            r.EventType,
            r.NodeId,
            r.Timestamp,
            JsonSerializer.Deserialize<Dictionary<string, object>>(r.DataJson ?? "{}") ?? new Dictionary<string, object>(),
            r.Message
        )).ToList();
    }
}