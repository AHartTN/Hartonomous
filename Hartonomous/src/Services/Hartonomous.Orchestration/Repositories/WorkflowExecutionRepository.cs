/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow execution repository - a focused, purpose-built class.
 * Features execution lifecycle management with clean separation of concerns.
 */

using Dapper;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Hartonomous.Orchestration.Repositories;

/// <summary>
/// Purpose-built repository for workflow execution operations
/// </summary>
public class WorkflowExecutionRepository
{
    private readonly string _connectionString;
    private readonly ILogger<WorkflowExecutionRepository> _logger;

    public WorkflowExecutionRepository(IOptions<SqlServerOptions> sqlOptions, ILogger<WorkflowExecutionRepository> logger)
    {
        _connectionString = sqlOptions.Value.ConnectionString;
        _logger = logger;
    }

    public async Task<Guid> StartWorkflowExecutionAsync(StartWorkflowExecutionRequest request, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowExecutions (ExecutionId, WorkflowId, UserId, ExecutionName, InputJson, ConfigurationJson, Status, StartedAt, StartedBy, Priority)
            VALUES (@ExecutionId, @WorkflowId, @UserId, @ExecutionName, @InputJson, @ConfigurationJson, @Status, @StartedAt, @StartedBy, @Priority);";

        var executionId = Guid.NewGuid();

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            ExecutionId = executionId,
            WorkflowId = request.WorkflowId,
            UserId = userId,
            ExecutionName = request.ExecutionName,
            InputJson = request.Input != null ? JsonSerializer.Serialize(request.Input) : null,
            ConfigurationJson = request.Configuration != null ? JsonSerializer.Serialize(request.Configuration) : null,
            Status = (int)DTOs.WorkflowExecutionStatus.Pending,
            StartedAt = DateTime.UtcNow,
            StartedBy = userId,
            Priority = request.Priority
        });

        _logger.LogInformation("Started workflow execution {ExecutionId}", executionId);
        return executionId;
    }

    public async Task<WorkflowExecutionDto?> GetExecutionByIdAsync(Guid executionId, string userId)
    {
        const string sql = @"
            SELECT e.ExecutionId, e.WorkflowId, w.Name as WorkflowName, e.ExecutionName, e.InputJson, e.OutputJson,
                   e.Status, e.StartedAt, e.CompletedAt, e.ErrorMessage, e.StartedBy, e.Priority
            FROM dbo.WorkflowExecutions e
            LEFT JOIN dbo.WorkflowDefinitions w ON e.WorkflowId = w.WorkflowId
            WHERE e.ExecutionId = @ExecutionId AND e.UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { ExecutionId = executionId, UserId = userId });

        return result != null ? MapToWorkflowExecutionDto(result, new List<NodeExecutionDto>()) : null;
    }

    public async Task<bool> UpdateExecutionStatusAsync(Guid executionId, DTOs.WorkflowExecutionStatus status, string? errorMessage, string userId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowExecutions
            SET Status = @Status, ErrorMessage = @ErrorMessage, CompletedAt = @CompletedAt
            WHERE ExecutionId = @ExecutionId AND UserId = @UserId;";

        var completedAt = IsTerminalStatus(status) ? DateTime.UtcNow : (DateTime?)null;

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            ErrorMessage = errorMessage,
            CompletedAt = completedAt,
            ExecutionId = executionId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<List<WorkflowExecutionDto>> GetActiveExecutionsAsync(string userId)
    {
        const string sql = @"
            SELECT e.ExecutionId, e.WorkflowId, w.Name as WorkflowName, e.ExecutionName, e.InputJson, e.OutputJson,
                   e.Status, e.StartedAt, e.CompletedAt, e.ErrorMessage, e.StartedBy, e.Priority
            FROM dbo.WorkflowExecutions e
            LEFT JOIN dbo.WorkflowDefinitions w ON e.WorkflowId = w.WorkflowId
            WHERE e.UserId = @UserId AND e.Status IN (@Running, @Paused, @Pending)
            ORDER BY e.StartedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new
        {
            UserId = userId,
            Running = (int)DTOs.WorkflowExecutionStatus.Running,
            Paused = (int)DTOs.WorkflowExecutionStatus.Paused,
            Pending = (int)DTOs.WorkflowExecutionStatus.Pending
        });

        return results.Select(r => MapToWorkflowExecutionDto(r, new List<NodeExecutionDto>())).ToList();
    }

    private static bool IsTerminalStatus(DTOs.WorkflowExecutionStatus status) =>
        status == DTOs.WorkflowExecutionStatus.Completed ||
        status == DTOs.WorkflowExecutionStatus.Failed ||
        status == DTOs.WorkflowExecutionStatus.Cancelled;

    private static WorkflowExecutionDto MapToWorkflowExecutionDto(dynamic row, List<NodeExecutionDto> nodeExecutions)
    {
        var input = string.IsNullOrEmpty(row.InputJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.InputJson);

        var output = string.IsNullOrEmpty(row.OutputJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.OutputJson);

        var duration = row.CompletedAt != null
            ? (TimeSpan?)(row.CompletedAt - row.StartedAt)
            : null;

        return new WorkflowExecutionDto(
            row.ExecutionId,
            row.WorkflowId,
            row.WorkflowName ?? string.Empty,
            row.ExecutionName,
            input,
            output,
            (DTOs.WorkflowExecutionStatus)row.Status,
            row.StartedAt,
            row.CompletedAt,
            row.ErrorMessage,
            row.StartedBy,
            row.Priority,
            duration,
            nodeExecutions
        );
    }
}