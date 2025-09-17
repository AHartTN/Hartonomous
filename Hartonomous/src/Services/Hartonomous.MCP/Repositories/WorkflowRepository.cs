using Dapper;
using Hartonomous.Core.Shared.DTOs;
using Hartonomous.Core.Shared.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

/// <summary>
/// Repository implementation for workflow management using Dapper
/// </summary>
public class WorkflowRepository : IWorkflowRepository
{
    private readonly string _connectionString;

    public WorkflowRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
    }

    public Task<WorkflowDefinition?> GetByIdAsync(Guid id, string userId) => GetWorkflowAsync(id, userId);

    public Task<IEnumerable<WorkflowDefinition>> GetAllAsync(string userId) => GetWorkflowsByUserAsync(userId);

    public Task<Guid> CreateAsync(WorkflowDefinition entity, string userId) => CreateWorkflowAsync(entity, userId);

    public Task<bool> DeleteAsync(Guid id, string userId) => DeleteWorkflowAsync(id, userId);

    public async Task<bool> UpdateAsync(WorkflowDefinition entity, string userId)
    {
        const string sql = @"
            UPDATE dbo.WorkflowDefinitions
            SET WorkflowName = @WorkflowName,
                Description = @Description,
                Steps = @Steps,
                Parameters = @Parameters
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            entity.WorkflowId,
            UserId = userId,
            entity.WorkflowName,
            entity.Description,
            Steps = JsonSerializer.Serialize(entity.Steps),
            Parameters = entity.Parameters != null ? JsonSerializer.Serialize(entity.Parameters) : null
        });

        return rowsAffected > 0;
    }

    public async Task<Guid> CreateWorkflowAsync(WorkflowDefinition workflow, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.WorkflowDefinitions (WorkflowId, UserId, WorkflowName, Description, Steps, Parameters, CreatedAt)
            VALUES (@WorkflowId, @UserId, @WorkflowName, @Description, @Steps, @Parameters, @CreatedAt);";

        var workflowId = workflow.WorkflowId != Guid.Empty ? workflow.WorkflowId : Guid.NewGuid();

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            WorkflowId = workflowId,
            UserId = userId,
            WorkflowName = workflow.WorkflowName,
            Description = workflow.Description,
            Steps = JsonSerializer.Serialize(workflow.Steps),
            Parameters = workflow.Parameters != null ? JsonSerializer.Serialize(workflow.Parameters) : null,
            CreatedAt = DateTime.UtcNow
        });

        return workflowId;
    }

    public async Task<WorkflowDefinition?> GetWorkflowAsync(Guid workflowId, string userId)
    {
        const string sql = @"
            SELECT WorkflowId, WorkflowName, Description, Steps, Parameters
            FROM dbo.WorkflowDefinitions
            WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { WorkflowId = workflowId, UserId = userId });

        return result != null ? MapToWorkflowDefinition(result) : null;
    }

    public async Task<IEnumerable<WorkflowDefinition>> GetWorkflowsByUserAsync(string userId)
    {
        const string sql = @"
            SELECT WorkflowId, WorkflowName, Description, Steps, Parameters
            FROM dbo.WorkflowDefinitions
            WHERE UserId = @UserId
            ORDER BY CreatedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { UserId = userId });

        return results.Select(MapToWorkflowDefinition);
    }

    public async Task<Guid> StartWorkflowExecutionAsync(Guid workflowId, Guid projectId, string userId, Dictionary<string, object>? parameters = null)
    {
        using var connection = new SqlConnection(_connectionString);
        using var transaction = connection.BeginTransaction();

        try
        {
            // Verify workflow exists and belongs to user
            const string checkWorkflowSql = @"
                SELECT COUNT(1) FROM dbo.WorkflowDefinitions
                WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

            var workflowExists = await connection.QuerySingleAsync<int>(checkWorkflowSql,
                new { WorkflowId = workflowId, UserId = userId }, transaction);

            if (workflowExists == 0)
                throw new InvalidOperationException("Workflow not found or access denied");

            // Create workflow execution
            var executionId = Guid.NewGuid();
            const string createExecutionSql = @"
                INSERT INTO dbo.WorkflowExecutions (ExecutionId, WorkflowId, ProjectId, UserId, Status, StartedAt)
                VALUES (@ExecutionId, @WorkflowId, @ProjectId, @UserId, @Status, @StartedAt);";

            await connection.ExecuteAsync(createExecutionSql, new
            {
                ExecutionId = executionId,
                WorkflowId = workflowId,
                ProjectId = projectId,
                UserId = userId,
                Status = (int)WorkflowExecutionStatus.Pending,
                StartedAt = DateTime.UtcNow
            }, transaction);

            // Get workflow steps and create step executions
            const string getStepsSql = @"
                SELECT Steps FROM dbo.WorkflowDefinitions
                WHERE WorkflowId = @WorkflowId AND UserId = @UserId;";

            var stepsJson = await connection.QuerySingleAsync<string>(getStepsSql,
                new { WorkflowId = workflowId, UserId = userId }, transaction);

            var steps = JsonSerializer.Deserialize<WorkflowStep[]>(stepsJson) ?? Array.Empty<WorkflowStep>();

            foreach (var step in steps)
            {
                const string createStepExecutionSql = @"
                    INSERT INTO dbo.StepExecutions (StepExecutionId, ExecutionId, StepId, StepName, AgentType, Status, Input)
                    VALUES (@StepExecutionId, @ExecutionId, @StepId, @StepName, @AgentType, @Status, @Input);";

                await connection.ExecuteAsync(createStepExecutionSql, new
                {
                    StepExecutionId = Guid.NewGuid(),
                    ExecutionId = executionId,
                    StepId = step.StepId,
                    StepName = step.StepName,
                    AgentType = step.AgentType,
                    Status = (int)StepExecutionStatus.Pending,
                    Input = JsonSerializer.Serialize(step.Input)
                }, transaction);
            }

            transaction.Commit();
            return executionId;
        }
        catch
        {
            transaction.Rollback();
            throw;
        }
    }

    public async Task<WorkflowExecution?> GetWorkflowExecutionAsync(Guid executionId, string userId)
    {
        const string executionSql = @"
            SELECT ExecutionId, WorkflowId, ProjectId, Status, StartedAt, CompletedAt, ErrorMessage
            FROM dbo.WorkflowExecutions
            WHERE ExecutionId = @ExecutionId AND UserId = @UserId;";

        const string stepsSql = @"
            SELECT StepExecutionId, StepId, StepName, AgentType, AssignedAgentId, Status, Input, Output, StartedAt, CompletedAt, ErrorMessage
            FROM dbo.StepExecutions
            WHERE ExecutionId = @ExecutionId
            ORDER BY StepName;";

        using var connection = new SqlConnection(_connectionString);
        var execution = await connection.QueryFirstOrDefaultAsync(executionSql,
            new { ExecutionId = executionId, UserId = userId });

        if (execution == null)
            return null;

        var steps = await connection.QueryAsync(stepsSql, new { ExecutionId = executionId });
        var stepExecutions = steps.Select(MapToStepExecution);

        return new WorkflowExecution(
            execution.ExecutionId,
            execution.WorkflowId,
            execution.ProjectId,
            userId,
            (WorkflowExecutionStatus)execution.Status,
            execution.StartedAt,
            execution.CompletedAt,
            stepExecutions,
            execution.ErrorMessage
        );
    }

    public async Task<bool> UpdateWorkflowExecutionStatusAsync(Guid executionId, WorkflowExecutionStatus status, string userId, string? errorMessage = null)
    {
        const string sql = @"
            UPDATE dbo.WorkflowExecutions
            SET Status = @Status, CompletedAt = @CompletedAt, ErrorMessage = @ErrorMessage
            WHERE ExecutionId = @ExecutionId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            CompletedAt = status == WorkflowExecutionStatus.Completed || status == WorkflowExecutionStatus.Failed || status == WorkflowExecutionStatus.Cancelled
                ? DateTime.UtcNow : (DateTime?)null,
            ErrorMessage = errorMessage,
            ExecutionId = executionId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<bool> UpdateStepExecutionAsync(Guid stepExecutionId, StepExecutionStatus status, string userId, object? output = null, string? errorMessage = null)
    {
        const string sql = @"
            UPDATE se
            SET Status = @Status,
                Output = @Output,
                ErrorMessage = @ErrorMessage,
                StartedAt = CASE WHEN @Status = 1 AND StartedAt IS NULL THEN @StartedAt ELSE StartedAt END,
                CompletedAt = CASE WHEN @Status IN (2, 3, 4) THEN @CompletedAt ELSE CompletedAt END
            FROM dbo.StepExecutions se
            INNER JOIN dbo.WorkflowExecutions we ON se.ExecutionId = we.ExecutionId
            WHERE se.StepExecutionId = @StepExecutionId AND we.UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            Output = output != null ? JsonSerializer.Serialize(output) : null,
            ErrorMessage = errorMessage,
            StartedAt = DateTime.UtcNow,
            CompletedAt = DateTime.UtcNow,
            StepExecutionId = stepExecutionId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<StepExecution>> GetPendingStepExecutionsAsync(string userId)
    {
        const string sql = @"
            SELECT se.StepExecutionId, se.StepId, se.StepName, se.AgentType, se.AssignedAgentId, se.Status, se.Input, se.Output, se.StartedAt, se.CompletedAt, se.ErrorMessage
            FROM dbo.StepExecutions se
            INNER JOIN dbo.WorkflowExecutions we ON se.ExecutionId = we.ExecutionId
            WHERE se.Status = @PendingStatus AND we.UserId = @UserId
            ORDER BY we.StartedAt;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new
        {
            PendingStatus = (int)StepExecutionStatus.Pending,
            UserId = userId
        });

        return results.Select(MapToStepExecution);
    }

    public async Task<bool> AssignStepToAgentAsync(Guid stepExecutionId, Guid agentId, string userId)
    {
        const string sql = @"
            UPDATE se
            SET AssignedAgentId = @AgentId
            FROM dbo.StepExecutions se
            INNER JOIN dbo.WorkflowExecutions we ON se.ExecutionId = we.ExecutionId
            WHERE se.StepExecutionId = @StepExecutionId AND we.UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            AgentId = agentId,
            StepExecutionId = stepExecutionId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsByProjectAsync(Guid projectId, string userId)
    {
        const string sql = @"
            SELECT ExecutionId, WorkflowId, ProjectId, Status, StartedAt, CompletedAt, ErrorMessage
            FROM dbo.WorkflowExecutions
            WHERE ProjectId = @ProjectId AND UserId = @UserId
            ORDER BY StartedAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { ProjectId = projectId, UserId = userId });

        var executions = new List<WorkflowExecution>();
        foreach (var execution in results)
        {
            // For this method, we'll return executions without step details for performance
            // If step details are needed, call GetWorkflowExecutionAsync for specific execution
            executions.Add(new WorkflowExecution(
                execution.ExecutionId,
                execution.WorkflowId,
                execution.ProjectId,
                userId,
                (WorkflowExecutionStatus)execution.Status,
                execution.StartedAt,
                execution.CompletedAt,
                Array.Empty<StepExecution>(),
                execution.ErrorMessage
            ));
        }

        return executions;
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

    private static WorkflowDefinition MapToWorkflowDefinition(dynamic row)
    {
        var steps = JsonSerializer.Deserialize<WorkflowStep[]>(row.Steps) ?? Array.Empty<WorkflowStep>();
        var parameters = string.IsNullOrEmpty(row.Parameters)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.Parameters);

        return new WorkflowDefinition(
            row.WorkflowId,
            row.WorkflowName,
            row.Description,
            steps,
            parameters
        );
    }

    private static StepExecution MapToStepExecution(dynamic row)
    {
        var input = string.IsNullOrEmpty(row.Input)
            ? null
            : JsonSerializer.Deserialize<object>(row.Input);

        var output = string.IsNullOrEmpty(row.Output)
            ? null
            : JsonSerializer.Deserialize<object>(row.Output);

        return new StepExecution(
            row.StepExecutionId,
            row.StepId,
            row.AssignedAgentId,
            (StepExecutionStatus)row.Status,
            input,
            output,
            row.StartedAt,
            row.CompletedAt,
            row.ErrorMessage
        );
    }
}