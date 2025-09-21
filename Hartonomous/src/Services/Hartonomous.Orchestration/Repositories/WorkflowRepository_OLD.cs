/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the refactored workflow orchestration repository following clean architecture patterns.
 * Features centralized business logic, thin client design, and efficient data access patterns.
 */

using Dapper;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Hartonomous.Orchestration.Models;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace Hartonomous.Orchestration.Repositories;

/// <summary>
/// Refactored workflow repository using composition with purpose-built repositories
/// </summary>
public class WorkflowRepository : BaseRepository<Workflow, Guid>, IWorkflowRepository
{
    private readonly ILogger<WorkflowRepository> _logger;
    private readonly WorkflowExecutionRepository _executionRepository;
    private readonly WorkflowMetricsRepository _metricsRepository;
    private readonly WorkflowEventRepository _eventRepository;

    public WorkflowRepository(
        IOptions<SqlServerOptions> sqlOptions,
        ILogger<WorkflowRepository> logger,
        WorkflowExecutionRepository executionRepository,
        WorkflowMetricsRepository metricsRepository,
        WorkflowEventRepository eventRepository)
        : base(sqlOptions)
    {
        _logger = logger;
        _executionRepository = executionRepository;
        _metricsRepository = metricsRepository;
        _eventRepository = eventRepository;
    }

    protected override string GetTableName() => "dbo.WorkflowDefinitions";

    protected override string GetSelectColumns() =>
        "Id, WorkflowId, UserId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedDate, ModifiedDate, Version, Status";

    protected override (string Columns, string Parameters) GetInsertColumnsAndParameters() =>
        ("Id, WorkflowId, UserId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedDate, ModifiedDate, Version, Status",
         "@Id, @WorkflowId, @UserId, @Name, @Description, @WorkflowDefinitionJson, @Category, @ParametersJson, @TagsJson, @CreatedDate, @ModifiedDate, @Version, @Status");

    protected override string GetUpdateSetClause() =>
        "Name = @Name, Description = @Description, WorkflowDefinitionJson = @WorkflowDefinitionJson, Category = @Category, ParametersJson = @ParametersJson, TagsJson = @TagsJson, ModifiedDate = @ModifiedDate, Version = @Version, Status = @Status";

    protected override Workflow MapToEntity(dynamic row)
    {
        return new Workflow
        {
            Id = row.Id,
            UserId = row.UserId,
            Name = row.Name,
            Description = row.Description ?? string.Empty,
            Definition = row.WorkflowDefinitionJson ?? string.Empty,
            Status = (Core.Enums.WorkflowStatus)row.Status,
            Parameters = DeserializeFromJson<Dictionary<string, object>>(row.ParametersJson) ?? new(),
            CreatedDate = row.CreatedDate,
            ModifiedDate = row.ModifiedDate
        };
    }

    protected override object GetParameters(Workflow entity)
    {
        return new
        {
            Id = entity.Id,
            WorkflowId = entity.Id, // Using same ID for WorkflowId
            UserId = entity.UserId,
            Name = entity.Name,
            Description = entity.Description,
            WorkflowDefinitionJson = entity.Definition,
            Category = "General", // Default category
            ParametersJson = SerializeToJson(entity.Parameters),
            TagsJson = "[]", // Default empty tags
            CreatedDate = entity.CreatedDate,
            ModifiedDate = entity.ModifiedDate,
            Version = 1, // Default version
            Status = (int)entity.Status
        };
    }

    public async Task<Guid> CreateWorkflowAsync(CreateWorkflowRequest request, string userId)
    {
        var workflow = new Workflow
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Name = request.Name,
            Description = request.Description ?? string.Empty,
            Definition = request.WorkflowDefinition,
            Status = Core.Enums.WorkflowStatus.Draft,
            Parameters = request.Parameters ?? new(),
            CreatedDate = DateTime.UtcNow
        };

        try
        {
            await CreateAsync(workflow);
            _logger.LogInformation("Created workflow {WorkflowId} for user {UserId}", workflow.Id, userId);
            return workflow.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workflow for user {UserId}", userId);
            throw;
        }
    }

    public async Task<WorkflowDefinitionDto?> GetWorkflowByIdAsync(Guid workflowId, string userId)
    {
        var workflow = await GetByIdAsync(workflowId);
        if (workflow?.UserId != userId) return null;

        return MapToWorkflowDefinitionDto(workflow);
    }

    public async Task<bool> UpdateWorkflowAsync(Guid workflowId, UpdateWorkflowRequest request, string userId)
    {
        var workflow = await GetByIdAsync(workflowId);
        if (workflow?.UserId != userId) return false;

        // Update fields only if provided
        if (!string.IsNullOrEmpty(request.Name)) workflow.Name = request.Name;
        if (!string.IsNullOrEmpty(request.Description)) workflow.Description = request.Description;
        if (!string.IsNullOrEmpty(request.WorkflowDefinition)) workflow.Definition = request.WorkflowDefinition;
        if (request.Parameters != null) workflow.Parameters = request.Parameters;

        return await UpdateAsync(workflow);
    }

    public async Task<bool> DeleteWorkflowAsync(Guid workflowId, string userId)
    {
        var workflow = await GetByIdAsync(workflowId);
        if (workflow?.UserId != userId) return false;

        return await DeleteAsync(workflowId);
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
            whereClause += " AND CreatedDate >= @CreatedAfter";
            parameters.Add("CreatedAfter", request.CreatedAfter.Value);
        }

        if (request.CreatedBefore.HasValue)
        {
            whereClause += " AND CreatedDate <= @CreatedBefore";
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
            SELECT Id, WorkflowId, UserId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedDate, ModifiedDate, Version, Status
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
            SELECT TOP(@Limit) Id, WorkflowId, UserId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, CreatedDate, ModifiedDate, Version, Status
            FROM dbo.WorkflowDefinitions
            WHERE UserId = @UserId
            ORDER BY ModifiedDate DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { UserId = userId, Limit = limit });

        return results.Select(MapToWorkflowDefinitionDto).ToList();
    }

    public async Task<bool> UpdateWorkflowStatusAsync(Guid workflowId, DTOs.WorkflowStatus status, string userId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowDefinitions
            SET Status = @Status, ModifiedDate = @ModifiedDate
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            ModifiedDate = DateTime.UtcNow,
            WorkflowId = workflowId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<DTOs.WorkflowValidationResult> ValidateWorkflowAsync(string workflowDefinition)
    {
        if (string.IsNullOrWhiteSpace(workflowDefinition))
        {
            return new DTOs.WorkflowValidationResult(
                false,
                new List<DTOs.ValidationError>
                {
                    new DTOs.ValidationError("EMPTY_DEFINITION", "Workflow definition cannot be null or empty")
                },
                new List<DTOs.ValidationWarning>()
            );
        }

        var errors = new List<DTOs.ValidationError>();
        var warnings = new List<DTOs.ValidationWarning>();

        try
        {
            // Basic JSON validation
            using var jsonDoc = JsonDocument.Parse(workflowDefinition);
            var root = jsonDoc.RootElement;

            // Check for required properties
            if (!root.TryGetProperty("nodes", out _))
            {
                errors.Add(new DTOs.ValidationError("MISSING_NODES", "Workflow definition must contain 'nodes' property"));
            }

            if (!root.TryGetProperty("connections", out _))
            {
                warnings.Add(new DTOs.ValidationWarning("MISSING_CONNECTIONS", "Workflow definition does not contain 'connections' property"));
            }

            // Additional validation logic could be added here
            // For example: validate node types, check for circular dependencies, etc.

            return new DTOs.WorkflowValidationResult(
                !errors.Any(),
                errors,
                warnings
            );
        }
        catch (JsonException ex)
        {
            _logger.LogWarning("JSON validation failed for workflow definition: {Error}", ex.Message);
            return new DTOs.WorkflowValidationResult(
                false,
                new List<DTOs.ValidationError>
                {
                    new DTOs.ValidationError("JSON_PARSE_ERROR", ex.Message)
                },
                warnings
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error validating workflow definition");
            return new DTOs.WorkflowValidationResult(
                false,
                new List<DTOs.ValidationError>
                {
                    new DTOs.ValidationError("VALIDATION_ERROR", "An unexpected error occurred during validation")
                },
                warnings
            );
        }
    }

    public async Task<Guid> StartWorkflowExecutionAsync(StartWorkflowExecutionRequest request, string userId)
    {
        if (request == null)
            throw new ArgumentNullException(nameof(request));
        if (request.WorkflowId == Guid.Empty)
            throw new ArgumentException("Workflow ID cannot be empty", nameof(request.WorkflowId));
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty", nameof(userId));
        if (string.IsNullOrWhiteSpace(request.ExecutionName))
            throw new ArgumentException("Execution name cannot be null or empty", nameof(request.ExecutionName));

        try
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

            _logger.LogInformation("Started workflow execution {ExecutionId} for workflow {WorkflowId} by user {UserId}", executionId, request.WorkflowId, userId);
            return executionId;
        }
        catch (SqlException ex)
        {
            _logger.LogError(ex, "Failed to start workflow execution for workflow {WorkflowId} by user {UserId}", request.WorkflowId, userId);
            throw new InvalidOperationException($"Failed to start workflow execution: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error starting workflow execution for workflow {WorkflowId} by user {UserId}", request.WorkflowId, userId);
            throw;
        }
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
        var rowsAffected = await connection.ExecuteAsync(sql, new
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

        return rowsAffected > 0;
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
            row.CreatedDate,
            row.ModifiedDate,
            row.UserId,
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