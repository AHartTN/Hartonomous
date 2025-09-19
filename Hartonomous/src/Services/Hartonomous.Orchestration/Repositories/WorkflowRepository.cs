/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow orchestration repository for advanced workflow management.
 * Features workflow execution engine, node state management, debugging capabilities, and performance metrics.
 */

using Dapper;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Hartonomous.Orchestration.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Data;
using System.Text.Json;

namespace Hartonomous.Orchestration.Repositories;

/// <summary>
/// Repository implementation for workflow management using Dapper
/// </summary>
public class WorkflowRepository : IWorkflowRepository
{
    private readonly string _connectionString;
    private readonly ILogger<WorkflowRepository> _logger;

    public WorkflowRepository(IConfiguration configuration, ILogger<WorkflowRepository> logger)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
        _logger = logger;
    }

    public async Task<Guid> CreateWorkflowAsync(CreateWorkflowRequest request, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowDefinitions (WorkflowId, UserId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedAt, UpdatedAt, CreatedBy, Version, Status)
            VALUES (@WorkflowId, @UserId, @Name, @Description, @WorkflowDefinitionJson, @Category, @ParametersJson, @TagsJson, @CreatedAt, @UpdatedAt, @CreatedBy, @Version, @Status);";

        var workflowId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            WorkflowId = workflowId,
            UserId = userId,
            Name = request.Name,
            Description = request.Description,
            WorkflowDefinitionJson = request.WorkflowDefinition,
            Category = request.Category,
            ParametersJson = request.Parameters != null ? JsonSerializer.Serialize(request.Parameters) : null,
            TagsJson = request.Tags != null ? JsonSerializer.Serialize(request.Tags) : null,
            CreatedAt = now,
            UpdatedAt = now,
            CreatedBy = userId,
            Version = 1,
            Status = (int)DTOs.WorkflowStatus.Draft
        });

        _logger.LogInformation("Created workflow {WorkflowId} for user {UserId}", workflowId, userId);
        return workflowId;
    }

    public async Task<WorkflowDefinitionDto?> GetWorkflowByIdAsync(Guid workflowId, string userId)
    {
        const string sql = @"
            SELECT WorkflowId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedAt, UpdatedAt, CreatedBy, Version, Status
            FROM dbo.WorkflowDefinitions
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { WorkflowId = workflowId, UserId = userId });

        return result != null ? MapToWorkflowDefinitionDto(result) : null;
    }

    public async Task<bool> UpdateWorkflowAsync(Guid workflowId, UpdateWorkflowRequest request, string userId)
    {
        var setParts = new List<string>();
        var parameters = new DynamicParameters();
        parameters.Add("WorkflowId", workflowId);
        parameters.Add("UserId", userId);
        parameters.Add("UpdatedAt", DateTime.UtcNow);

        if (!string.IsNullOrEmpty(request.Name))
        {
            setParts.Add("Name = @Name");
            parameters.Add("Name", request.Name);
        }

        if (!string.IsNullOrEmpty(request.Description))
        {
            setParts.Add("Description = @Description");
            parameters.Add("Description", request.Description);
        }

        if (!string.IsNullOrEmpty(request.WorkflowDefinition))
        {
            setParts.Add("WorkflowDefinitionJson = @WorkflowDefinitionJson, Version = Version + 1");
            parameters.Add("WorkflowDefinitionJson", request.WorkflowDefinition);
        }

        if (!string.IsNullOrEmpty(request.Category))
        {
            setParts.Add("Category = @Category");
            parameters.Add("Category", request.Category);
        }

        if (request.Parameters != null)
        {
            setParts.Add("ParametersJson = @ParametersJson");
            parameters.Add("ParametersJson", JsonSerializer.Serialize(request.Parameters));
        }

        if (request.Tags != null)
        {
            setParts.Add("TagsJson = @TagsJson");
            parameters.Add("TagsJson", JsonSerializer.Serialize(request.Tags));
        }

        if (!setParts.Any())
        {
            return true; // Nothing to update
        }

        var sql = $@"
            UPDATE dbo.WorkflowDefinitions
            SET {string.Join(", ", setParts)}, UpdatedAt = @UpdatedAt
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, parameters);

        return rowsAffected > 0;
    }

    public async Task<bool> DeleteWorkflowAsync(Guid workflowId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.WorkflowDefinitions
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { WorkflowId = workflowId, UserId = userId });

        return rowsAffected > 0;
    }

    public async Task<PaginatedResult<WorkflowDefinitionDto>> SearchWorkflowsAsync(WorkflowSearchRequest request, string userId)
    {
        var whereClause = "WHERE UserId = @UserId";
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);

        if (!string.IsNullOrEmpty(request.Query))
        {
            whereClause += " AND (Name LIKE @Query OR Description LIKE @Query)";
            parameters.Add("Query", $"%{request.Query}%");
        }

        if (!string.IsNullOrEmpty(request.Category))
        {
            whereClause += " AND Category = @Category";
            parameters.Add("Category", request.Category);
        }

        if (request.Status.HasValue)
        {
            whereClause += " AND Status = @Status";
            parameters.Add("Status", (int)request.Status.Value);
        }

        if (request.CreatedAfter.HasValue)
        {
            whereClause += " AND CreatedAt >= @CreatedAfter";
            parameters.Add("CreatedAfter", request.CreatedAfter.Value);
        }

        if (request.CreatedBefore.HasValue)
        {
            whereClause += " AND CreatedAt <= @CreatedBefore";
            parameters.Add("CreatedBefore", request.CreatedBefore.Value);
        }

        // Count total records
        var countSql = $"SELECT COUNT(*) FROM dbo.WorkflowDefinitions {whereClause}";

        using var connection = new SqlConnection(_connectionString);
        var totalCount = await connection.QuerySingleAsync<int>(countSql, parameters);

        // Calculate pagination
        var offset = (request.Page - 1) * request.PageSize;
        var totalPages = (int)Math.Ceiling((double)totalCount / request.PageSize);

        // Get paginated results
        var orderBy = $"ORDER BY {request.SortBy} {request.SortDirection}";
        var sql = $@"
            SELECT WorkflowId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedAt, UpdatedAt, CreatedBy, Version, Status
            FROM dbo.WorkflowDefinitions
            {whereClause}
            {orderBy}
            OFFSET @Offset ROWS
            FETCH NEXT @PageSize ROWS ONLY;";

        parameters.Add("Offset", offset);
        parameters.Add("PageSize", request.PageSize);

        var results = await connection.QueryAsync(sql, parameters);
        var workflows = results.Select(MapToWorkflowDefinitionDto).ToList();

        return new PaginatedResult<WorkflowDefinitionDto>(
            workflows,
            totalCount,
            request.Page,
            request.PageSize,
            totalPages
        );
    }

    public async Task<List<WorkflowDefinitionDto>> GetWorkflowsByUserAsync(string userId, int limit = 100)
    {
        const string sql = @"
            SELECT TOP(@Limit) WorkflowId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedAt, UpdatedAt, CreatedBy, Version, Status
            FROM dbo.WorkflowDefinitions
            WHERE UserId = @UserId
            ORDER BY UpdatedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { UserId = userId, Limit = limit });

        return results.Select(MapToWorkflowDefinitionDto).ToList();
    }

    public async Task<bool> UpdateWorkflowStatusAsync(Guid workflowId, DTOs.WorkflowStatus status, string userId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowDefinitions
            SET Status = @Status, UpdatedAt = @UpdatedAt
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            UpdatedAt = DateTime.UtcNow,
            WorkflowId = workflowId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<DTOs.WorkflowValidationResult> ValidateWorkflowAsync(string workflowDefinition)
    {
        try
        {
            // Basic JSON validation
            JsonDocument.Parse(workflowDefinition);

            return new DTOs.WorkflowValidationResult(
                true,
                new List<DTOs.ValidationError>(),
                new List<DTOs.ValidationWarning>()
            );
        }
        catch (JsonException ex)
        {
            return new DTOs.WorkflowValidationResult(
                false,
                new List<DTOs.ValidationError>
                {
                    new DTOs.ValidationError("JSON_PARSE_ERROR", ex.Message)
                },
                new List<DTOs.ValidationWarning>()
            );
        }
    }

    public async Task<Guid> StartWorkflowExecutionAsync(StartWorkflowExecutionRequest request, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowExecutions (ExecutionId, WorkflowId, UserId, ExecutionName, InputJson, ConfigurationJson, Status, StartedAt, StartedBy, Priority)
            VALUES (@ExecutionId, @WorkflowId, @UserId, @ExecutionName, @InputJson, @ConfigurationJson, @Status, @StartedAt, @StartedBy, @Priority);";

        var executionId = Guid.NewGuid();
        var now = DateTime.UtcNow;

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
            StartedAt = now,
            StartedBy = userId,
            Priority = request.Priority
        });

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

        if (result == null)
        {
            return null;
        }

        // Get node executions
        var nodeExecutions = await GetNodeExecutionsByExecutionAsync(executionId);

        return MapToWorkflowExecutionDto(result, nodeExecutions);
    }

    public async Task<bool> UpdateExecutionStatusAsync(Guid executionId, DTOs.WorkflowExecutionStatus status, string? errorMessage, string userId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowExecutions
            SET Status = @Status, ErrorMessage = @ErrorMessage, CompletedAt = @CompletedAt
            WHERE ExecutionId = @ExecutionId AND UserId = @UserId;";

        var completedAt = status == DTOs.WorkflowExecutionStatus.Completed ||
                          status == DTOs.WorkflowExecutionStatus.Failed ||
                          status == DTOs.WorkflowExecutionStatus.Cancelled
            ? DateTime.UtcNow : (DateTime?)null;

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

    public async Task<bool> UpdateExecutionOutputAsync(Guid executionId, Dictionary<string, object> output, string userId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowExecutions
            SET OutputJson = @OutputJson
            WHERE ExecutionId = @ExecutionId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            OutputJson = JsonSerializer.Serialize(output),
            ExecutionId = executionId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<List<WorkflowExecutionDto>> GetExecutionsByWorkflowAsync(Guid workflowId, string userId, int limit = 100)
    {
        const string sql = @"
            SELECT TOP(@Limit) e.ExecutionId, e.WorkflowId, w.Name as WorkflowName, e.ExecutionName, e.InputJson, e.OutputJson,
                   e.Status, e.StartedAt, e.CompletedAt, e.ErrorMessage, e.StartedBy, e.Priority
            FROM dbo.WorkflowExecutions e
            LEFT JOIN dbo.WorkflowDefinitions w ON e.WorkflowId = w.WorkflowId
            WHERE e.WorkflowId = @WorkflowId AND e.UserId = @UserId
            ORDER BY e.StartedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { WorkflowId = workflowId, UserId = userId, Limit = limit });

        var executions = new List<WorkflowExecutionDto>();
        foreach (var result in results)
        {
            var nodeExecutions = await GetNodeExecutionsByExecutionAsync(result.ExecutionId);
            executions.Add(MapToWorkflowExecutionDto(result, nodeExecutions));
        }

        return executions;
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

        var executions = new List<WorkflowExecutionDto>();
        foreach (var result in results)
        {
            var nodeExecutions = await GetNodeExecutionsByExecutionAsync(result.ExecutionId);
            executions.Add(MapToWorkflowExecutionDto(result, nodeExecutions));
        }

        return executions;
    }

    public async Task<bool> CancelExecutionAsync(Guid executionId, string userId)
    {
        return await UpdateExecutionStatusAsync(executionId, DTOs.WorkflowExecutionStatus.Cancelled, "Cancelled by user", userId);
    }

    public async Task<Guid> CreateNodeExecutionAsync(Guid executionId, NodeExecutionDto nodeExecution)
    {
        const string sql = @"
            INSERT INTO dbo.NodeExecutions (NodeExecutionId, ExecutionId, NodeId, NodeType, NodeName, InputJson, Status, StartedAt, RetryCount, MetadataJson)
            VALUES (@NodeExecutionId, @ExecutionId, @NodeId, @NodeType, @NodeName, @InputJson, @Status, @StartedAt, @RetryCount, @MetadataJson);";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            NodeExecutionId = nodeExecution.NodeExecutionId,
            ExecutionId = executionId,
            NodeId = nodeExecution.NodeId,
            NodeType = nodeExecution.NodeType,
            NodeName = nodeExecution.NodeName,
            InputJson = nodeExecution.Input != null ? JsonSerializer.Serialize(nodeExecution.Input) : null,
            Status = (int)nodeExecution.Status,
            StartedAt = nodeExecution.StartedAt,
            RetryCount = nodeExecution.RetryCount,
            MetadataJson = nodeExecution.Metadata != null ? JsonSerializer.Serialize(nodeExecution.Metadata) : null
        });

        return nodeExecution.NodeExecutionId;
    }

    public async Task<bool> UpdateNodeExecutionAsync(Guid nodeExecutionId, DTOs.NodeExecutionStatus status,
        Dictionary<string, object>? output, string? errorMessage)
    {
        const string sql = @"
            UPDATE dbo.NodeExecutions
            SET Status = @Status, OutputJson = @OutputJson, ErrorMessage = @ErrorMessage, CompletedAt = @CompletedAt
            WHERE NodeExecutionId = @NodeExecutionId;";

        var completedAt = status == DTOs.NodeExecutionStatus.Completed ||
                          status == DTOs.NodeExecutionStatus.Failed ||
                          status == DTOs.NodeExecutionStatus.Cancelled
            ? DateTime.UtcNow : (DateTime?)null;

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            OutputJson = output != null ? JsonSerializer.Serialize(output) : null,
            ErrorMessage = errorMessage,
            CompletedAt = completedAt,
            NodeExecutionId = nodeExecutionId
        });

        return rowsAffected > 0;
    }

    public async Task<List<NodeExecutionDto>> GetNodeExecutionsByExecutionAsync(Guid executionId)
    {
        const string sql = @"
            SELECT NodeExecutionId, NodeId, NodeType, NodeName, InputJson, OutputJson, Status, StartedAt, CompletedAt, ErrorMessage, RetryCount, MetadataJson
            FROM dbo.NodeExecutions
            WHERE ExecutionId = @ExecutionId
            ORDER BY StartedAt;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { ExecutionId = executionId });

        return results.Select(MapToNodeExecutionDto).ToList();
    }

    public async Task<bool> SaveWorkflowStateAsync(Guid executionId, WorkflowStateDto state)
    {
        const string sql = @"
            MERGE dbo.WorkflowStates AS target
            USING (SELECT @ExecutionId AS ExecutionId) AS source ON target.ExecutionId = source.ExecutionId
            WHEN MATCHED THEN
                UPDATE SET StateJson = @StateJson, CurrentNode = @CurrentNode, CompletedNodesJson = @CompletedNodesJson,
                          PendingNodesJson = @PendingNodesJson, CreatedAt = @CreatedAt, Version = Version + 1
            WHEN NOT MATCHED THEN
                INSERT (StateId, ExecutionId, StateJson, CurrentNode, CompletedNodesJson, PendingNodesJson, CreatedAt, Version)
                VALUES (NEWID(), @ExecutionId, @StateJson, @CurrentNode, @CompletedNodesJson, @PendingNodesJson, @CreatedAt, 1);";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            ExecutionId = executionId,
            StateJson = JsonSerializer.Serialize(state.State),
            CurrentNode = state.CurrentNode,
            CompletedNodesJson = JsonSerializer.Serialize(state.CompletedNodes),
            PendingNodesJson = JsonSerializer.Serialize(state.PendingNodes),
            CreatedAt = state.LastUpdated
        });

        return rowsAffected > 0;
    }

    public async Task<WorkflowStateDto?> GetWorkflowStateAsync(Guid executionId)
    {
        const string sql = @"
            SELECT ExecutionId, StateJson, CurrentNode, CompletedNodesJson, PendingNodesJson, CreatedAt
            FROM dbo.WorkflowStates
            WHERE ExecutionId = @ExecutionId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { ExecutionId = executionId });

        return result != null ? MapToWorkflowStateDto(result) : null;
    }

    public async Task<List<WorkflowStateDto>> GetWorkflowStateHistoryAsync(Guid executionId, int limit = 10)
    {
        const string sql = @"
            SELECT TOP(@Limit) ExecutionId, StateJson, CurrentNode, CompletedNodesJson, PendingNodesJson, CreatedAt
            FROM dbo.WorkflowStates
            WHERE ExecutionId = @ExecutionId
            ORDER BY CreatedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { ExecutionId = executionId, Limit = limit });

        return results.Select(MapToWorkflowStateDto).ToList();
    }

    public async Task<bool> CreateWorkflowEventAsync(Guid executionId, DebugEvent debugEvent)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowEvents (EventId, ExecutionId, EventType, NodeId, Timestamp, DataJson, Message, Level)
            VALUES (@EventId, @ExecutionId, @EventType, @NodeId, @Timestamp, @DataJson, @Message, @Level);";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
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

        return true;
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
        await connection.ExecuteAsync(sql, new
        {
            BreakpointId = breakpoint.BreakpointId,
            ExecutionId = executionId,
            NodeId = breakpoint.NodeId,
            Condition = breakpoint.Condition,
            IsEnabled = breakpoint.IsEnabled,
            CreatedAt = breakpoint.CreatedAt,
            CreatedBy = userId
        });

        return true;
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

    public async Task<WorkflowExecutionStatsDto> GetWorkflowStatsAsync(Guid workflowId, string userId,
        DateTime? fromDate = null, DateTime? toDate = null)
    {
        var whereClause = "WHERE WorkflowId = @WorkflowId AND UserId = @UserId";
        var parameters = new DynamicParameters();
        parameters.Add("WorkflowId", workflowId);
        parameters.Add("UserId", userId);

        if (fromDate.HasValue)
        {
            whereClause += " AND StartedAt >= @FromDate";
            parameters.Add("FromDate", fromDate.Value);
        }

        if (toDate.HasValue)
        {
            whereClause += " AND StartedAt <= @ToDate";
            parameters.Add("ToDate", toDate.Value);
        }

        var sql = $@"
            SELECT
                COUNT(*) as TotalExecutions,
                SUM(CASE WHEN Status = @CompletedStatus THEN 1 ELSE 0 END) as SuccessfulExecutions,
                SUM(CASE WHEN Status = @FailedStatus THEN 1 ELSE 0 END) as FailedExecutions,
                SUM(CASE WHEN Status IN (@RunningStatus, @PendingStatus, @PausedStatus) THEN 1 ELSE 0 END) as RunningExecutions,
                AVG(CASE WHEN CompletedAt IS NOT NULL THEN DATEDIFF(SECOND, StartedAt, CompletedAt) ELSE NULL END) as AverageExecutionTime,
                MAX(StartedAt) as LastExecution
            FROM dbo.WorkflowExecutions
            {whereClause};";

        parameters.Add("CompletedStatus", (int)DTOs.WorkflowExecutionStatus.Completed);
        parameters.Add("FailedStatus", (int)DTOs.WorkflowExecutionStatus.Failed);
        parameters.Add("RunningStatus", (int)DTOs.WorkflowExecutionStatus.Running);
        parameters.Add("PendingStatus", (int)DTOs.WorkflowExecutionStatus.Pending);
        parameters.Add("PausedStatus", (int)DTOs.WorkflowExecutionStatus.Paused);

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstAsync(sql, parameters);

        var totalExecutions = result.TotalExecutions;
        var successfulExecutions = result.SuccessfulExecutions;
        var successRate = totalExecutions > 0 ? (double)successfulExecutions / totalExecutions * 100 : 0;

        return new WorkflowExecutionStatsDto(
            totalExecutions,
            successfulExecutions,
            result.FailedExecutions,
            result.RunningExecutions,
            result.AverageExecutionTime ?? 0,
            successRate,
            result.LastExecution,
            new List<ExecutionTrendDataPoint>() // In a real implementation, you'd populate trend data
        );
    }

    public async Task<bool> RecordExecutionMetricAsync(Guid executionId, string metricName, double value,
        string? unit = null, Dictionary<string, string>? tags = null)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowExecutionMetrics (MetricsId, ExecutionId, MetricName, MetricType, MetricValue, Unit, Timestamp, TagsJson)
            VALUES (@MetricsId, @ExecutionId, @MetricName, @MetricType, @MetricValue, @Unit, @Timestamp, @TagsJson);";

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            MetricsId = Guid.NewGuid(),
            ExecutionId = executionId,
            MetricName = metricName,
            MetricType = "gauge",
            MetricValue = value,
            Unit = unit,
            Timestamp = DateTime.UtcNow,
            TagsJson = tags != null ? JsonSerializer.Serialize(tags) : null
        });

        return true;
    }

    private static WorkflowDefinitionDto MapToWorkflowDefinitionDto(dynamic row)
    {
        var parameters = string.IsNullOrEmpty(row.ParametersJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.ParametersJson);

        var tags = string.IsNullOrEmpty(row.TagsJson)
            ? null
            : JsonSerializer.Deserialize<List<string>>(row.TagsJson);

        return new WorkflowDefinitionDto(
            row.WorkflowId,
            row.Name,
            row.Description,
            row.WorkflowDefinitionJson,
            row.Category,
            parameters,
            tags,
            row.CreatedAt,
            row.UpdatedAt,
            row.CreatedBy,
            row.Version,
            (DTOs.WorkflowStatus)row.Status
        );
    }

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

    private static NodeExecutionDto MapToNodeExecutionDto(dynamic row)
    {
        var input = string.IsNullOrEmpty(row.InputJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.InputJson);

        var output = string.IsNullOrEmpty(row.OutputJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.OutputJson);

        var metadata = string.IsNullOrEmpty(row.MetadataJson)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.MetadataJson);

        var duration = row.CompletedAt != null
            ? (TimeSpan?)(row.CompletedAt - row.StartedAt)
            : null;

        return new NodeExecutionDto(
            row.NodeExecutionId,
            row.NodeId,
            row.NodeType,
            row.NodeName,
            input,
            output,
            (DTOs.NodeExecutionStatus)row.Status,
            row.StartedAt,
            row.CompletedAt,
            row.ErrorMessage,
            duration,
            row.RetryCount,
            metadata
        );
    }

    private static WorkflowStateDto MapToWorkflowStateDto(dynamic row)
    {
        var state = JsonSerializer.Deserialize<Dictionary<string, object>>(row.StateJson) ?? new Dictionary<string, object>();
        var completedNodes = JsonSerializer.Deserialize<List<string>>(row.CompletedNodesJson ?? "[]") ?? new List<string>();
        var pendingNodes = JsonSerializer.Deserialize<List<string>>(row.PendingNodesJson ?? "[]") ?? new List<string>();

        return new WorkflowStateDto(
            row.ExecutionId,
            state,
            row.CurrentNode ?? string.Empty,
            completedNodes,
            pendingNodes,
            row.CreatedAt
        );
    }
}