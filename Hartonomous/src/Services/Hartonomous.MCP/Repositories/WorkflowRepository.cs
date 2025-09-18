using Dapper;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

public class WorkflowRepository : BaseRepository<Workflow, Guid>, IWorkflowRepository
{
    public WorkflowRepository(IOptions<SqlServerOptions> sqlOptions) : base(sqlOptions)
    {
    }

    protected override string GetTableName() => "dbo.WorkflowDefinitions";

    protected override string GetSelectColumns() =>
        "WorkflowId as Id, UserId, WorkflowName as Name, Description, Steps as Definition, @WorkflowStatus as Status, Parameters, CreatedAt as CreatedDate";

    protected override (string Columns, string Parameters) GetInsertColumnsAndParameters() =>
        ("WorkflowId, UserId, WorkflowName, Description, Steps, Parameters, CreatedAt",
         "@Id, @UserId, @Name, @Description, @Definition, @Parameters, @CreatedDate");

    protected override string GetUpdateSetClause() =>
        "WorkflowName = @Name, Description = @Description, Steps = @Definition, Parameters = @Parameters";

    protected override Workflow MapToEntity(dynamic row)
    {
        return new Workflow
        {
            Id = row.Id,
            UserId = row.UserId,
            Name = row.Name,
            Description = row.Description,
            Definition = row.Definition,
            Status = WorkflowStatus.Active,
            Parameters = DeserializeFromJson<Dictionary<string, object>>(row.Parameters) ?? new Dictionary<string, object>(),
            CreatedDate = row.CreatedDate,
            ModifiedDate = null
        };
    }

    protected override object GetParameters(Workflow entity)
    {
        return new
        {
            Id = entity.Id,
            UserId = entity.UserId,
            Name = entity.Name,
            Description = entity.Description,
            Definition = entity.Definition,
            Status = (int)entity.Status,
            Parameters = SerializeToJson(entity.Parameters),
            CreatedDate = entity.CreatedDate,
            ModifiedDate = entity.ModifiedDate
        };
    }

    public async Task<Guid> CreateWorkflowAsync(WorkflowDefinition workflow, string userId)
    {
        var entity = new Workflow
        {
            Id = workflow.WorkflowId != Guid.Empty ? workflow.WorkflowId : Guid.NewGuid(),
            UserId = userId,
            Name = workflow.WorkflowName,
            Description = workflow.Description,
            Definition = SerializeToJson(workflow.Steps),
            Status = WorkflowStatus.Active,
            Parameters = workflow.Parameters ?? new Dictionary<string, object>(),
            CreatedDate = DateTime.UtcNow
        };

        return await CreateAsync(entity);
    }

    public async Task<WorkflowDefinition?> GetWorkflowAsync(Guid workflowId, string userId)
    {
        var entity = await GetByIdAsync(workflowId);
        if (entity?.UserId != userId) return null;

        return ConvertToWorkflowDefinition(entity);
    }

    public async Task<IEnumerable<WorkflowDefinition>> GetWorkflowsByUserAsync(string userId)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Select(ConvertToWorkflowDefinition);
    }

    public async Task<bool> DeleteWorkflowAsync(Guid workflowId, string userId)
    {
        return await DeleteAsync(workflowId);
    }

    public async Task<Guid> StartWorkflowExecutionAsync(Guid workflowId, Guid projectId, string userId, Dictionary<string, object>? parameters = null)
    {
        // For now, just return a new execution ID
        // Full implementation would create workflow execution records
        return Guid.NewGuid();
    }

    public async Task<WorkflowExecution?> GetWorkflowExecutionAsync(Guid executionId, string userId)
    {
        // Basic implementation - would need separate execution entities
        return new WorkflowExecution(
            executionId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            userId,
            WorkflowExecutionStatus.Pending,
            DateTime.UtcNow,
            null,
            Array.Empty<StepExecution>(),
            null
        );
    }

    public async Task<bool> UpdateWorkflowExecutionStatusAsync(Guid executionId, WorkflowExecutionStatus status, string userId, string? errorMessage = null)
    {
        // Basic implementation
        return true;
    }

    public async Task<bool> UpdateStepExecutionAsync(Guid stepExecutionId, StepExecutionStatus status, string userId, object? output = null, string? errorMessage = null)
    {
        // Basic implementation
        return true;
    }

    public async Task<IEnumerable<StepExecution>> GetPendingStepExecutionsAsync(string userId)
    {
        // Basic implementation
        return Array.Empty<StepExecution>();
    }

    public async Task<bool> AssignStepToAgentAsync(Guid stepExecutionId, Guid agentId, string userId)
    {
        // Basic implementation
        return true;
    }

    public async Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsByProjectAsync(Guid projectId, string userId)
    {
        // Basic implementation
        return Array.Empty<WorkflowExecution>();
    }

    private WorkflowDefinition ConvertToWorkflowDefinition(Workflow entity)
    {
        var steps = DeserializeFromJson<WorkflowStep[]>(entity.Definition) ?? Array.Empty<WorkflowStep>();

        return new WorkflowDefinition(
            entity.Id,
            entity.Name,
            entity.Description,
            steps,
            entity.Parameters
        );
    }

    // IRepository<WorkflowDefinition> bridge implementations
    async Task<WorkflowDefinition?> IRepository<WorkflowDefinition>.GetByIdAsync(Guid id, string userId)
    {
        return await GetWorkflowAsync(id, userId);
    }

    async Task<IEnumerable<WorkflowDefinition>> IRepository<WorkflowDefinition>.GetAllAsync(string userId)
    {
        return await GetWorkflowsByUserAsync(userId);
    }

    async Task<Guid> IRepository<WorkflowDefinition>.CreateAsync(WorkflowDefinition entity, string userId)
    {
        return await CreateWorkflowAsync(entity, userId);
    }

    async Task<bool> IRepository<WorkflowDefinition>.UpdateAsync(WorkflowDefinition entity, string userId)
    {
        var workflow = new Workflow
        {
            Id = entity.WorkflowId,
            UserId = userId,
            Name = entity.WorkflowName,
            Description = entity.Description,
            Definition = SerializeToJson(entity.Steps),
            Parameters = entity.Parameters ?? new Dictionary<string, object>(),
            ModifiedDate = DateTime.UtcNow
        };
        return await UpdateAsync(workflow);
    }

    async Task<bool> IRepository<WorkflowDefinition>.DeleteAsync(Guid id, string userId)
    {
        return await DeleteWorkflowAsync(id, userId);
    }
}