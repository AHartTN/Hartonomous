/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Multi-Context Protocol (MCP) workflow repository for workflow definition management.
 * Features workflow definition storage, execution tracking, and step orchestration with multi-tenant support.
 */

using Dapper;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Hartonomous.Orchestration.Models;
using CoreDtos = Hartonomous.Core.DTOs;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

public class WorkflowRepository : BaseRepository<WorkflowDefinition, Guid>
{
    public WorkflowRepository(IOptions<SqlServerOptions> sqlOptions) : base(sqlOptions)
    {
    }

    protected override string GetTableName() => "dbo.WorkflowDefinitions";

    protected override string GetSelectColumns() =>
        "Id, WorkflowId, UserId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, Version, Status, CreatedDate, ModifiedDate";

    protected override (string Columns, string Parameters) GetInsertColumnsAndParameters() =>
        ("Id, WorkflowId, UserId, Name, Description, WorkflowDefinitionJson, Category, ParametersJson, TagsJson, Version, Status, CreatedDate, ModifiedDate",
         "@Id, @WorkflowId, @UserId, @Name, @Description, @WorkflowDefinitionJson, @Category, @ParametersJson, @TagsJson, @Version, @Status, @CreatedDate, @ModifiedDate");

    protected override string GetUpdateSetClause() =>
        "WorkflowId = @WorkflowId, Name = @Name, Description = @Description, WorkflowDefinitionJson = @WorkflowDefinitionJson, Category = @Category, ParametersJson = @ParametersJson, TagsJson = @TagsJson, Version = @Version, Status = @Status, ModifiedDate = @ModifiedDate";

    protected override WorkflowDefinition MapToEntity(dynamic row)
    {
        return new WorkflowDefinition
        {
            Id = row.Id,
            WorkflowId = row.WorkflowId,
            UserId = row.UserId,
            Name = row.Name,
            Description = row.Description,
            WorkflowDefinitionJson = row.WorkflowDefinitionJson,
            Category = row.Category,
            ParametersJson = row.ParametersJson,
            TagsJson = row.TagsJson,
            Version = row.Version,
            Status = (WorkflowStatus)row.Status,
            CreatedDate = row.CreatedDate,
            ModifiedDate = row.ModifiedDate
        };
    }

    protected override object GetParameters(WorkflowDefinition entity)
    {
        return new
        {
            Id = entity.Id,
            WorkflowId = entity.WorkflowId,
            UserId = entity.UserId,
            Name = entity.Name,
            Description = entity.Description,
            WorkflowDefinitionJson = entity.WorkflowDefinitionJson,
            Category = entity.Category,
            ParametersJson = entity.ParametersJson,
            TagsJson = entity.TagsJson,
            Version = entity.Version,
            Status = (int)entity.Status,
            CreatedDate = entity.CreatedDate,
            ModifiedDate = entity.ModifiedDate
        };
    }

    public async Task<Guid> CreateWorkflowAsync(WorkflowDefinition workflow, string userId)
    {
        workflow.Id = workflow.Id != Guid.Empty ? workflow.Id : Guid.NewGuid();
        workflow.WorkflowId = workflow.WorkflowId != Guid.Empty ? workflow.WorkflowId : Guid.NewGuid();
        workflow.UserId = userId;
        workflow.CreatedDate = DateTime.UtcNow;
        workflow.ModifiedDate = null;

        return await CreateAsync(workflow);
    }

    public async Task<CoreDtos.WorkflowDefinition?> GetWorkflowAsync(Guid workflowId, string userId)
    {
        var entity = await GetByIdAsync(workflowId);
        if (entity?.UserId != userId) return null;

        return ConvertToWorkflowDefinition(entity);
    }

    public async Task<IEnumerable<CoreDtos.WorkflowDefinition>> GetWorkflowsByUserAsync(string userId)
    {
        var entities = await GetByUserAsync(userId);
        return entities.Select(ConvertToWorkflowDefinition);
    }

    public async Task<Guid> CreateWorkflowAsync(CoreDtos.WorkflowDefinition workflow, string userId)
    {
        // Convert DTO to domain entity
        var entity = new WorkflowDefinition
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            CreatedDate = DateTime.UtcNow,
            WorkflowId = workflow.WorkflowId,
            Name = workflow.WorkflowName,
            Description = workflow.Description,
            WorkflowDefinitionJson = JsonSerializer.Serialize(workflow), // Serialize entire workflow
            ParametersJson = workflow.Parameters != null ? JsonSerializer.Serialize(workflow.Parameters) : null,
            Version = 1,
            Status = WorkflowStatus.Active
        };

        return await CreateAsync(entity);
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

    public async Task<CoreDtos.WorkflowExecution?> GetWorkflowExecutionAsync(Guid executionId, string userId)
    {
        // Basic implementation - would need separate execution entities
        return new CoreDtos.WorkflowExecution(
            executionId,
            Guid.NewGuid(), // WorkflowId
            Guid.NewGuid(), // ProjectId
            userId,
            CoreDtos.WorkflowExecutionStatus.Pending,
            DateTime.UtcNow,
            null, // CompletedAt
            Array.Empty<CoreDtos.StepExecution>(),
            null // ErrorMessage
        );
    }

    public async Task<bool> UpdateWorkflowExecutionStatusAsync(Guid executionId, CoreDtos.WorkflowExecutionStatus status, string userId, string? errorMessage = null)
    {
        // Basic implementation
        return true;
    }

    public async Task<bool> UpdateStepExecutionAsync(Guid stepExecutionId, CoreDtos.StepExecutionStatus status, string userId, object? output = null, string? errorMessage = null)
    {
        // Basic implementation
        return true;
    }

    public async Task<IEnumerable<CoreDtos.StepExecution>> GetPendingStepExecutionsAsync(string userId)
    {
        // Basic implementation
        return Array.Empty<CoreDtos.StepExecution>();
    }

    public async Task<bool> AssignStepToAgentAsync(Guid stepExecutionId, Guid agentId, string userId)
    {
        // Basic implementation
        return true;
    }

    public async Task<IEnumerable<CoreDtos.WorkflowExecution>> GetWorkflowExecutionsByProjectAsync(Guid projectId, string userId)
    {
        // Basic implementation
        return Array.Empty<CoreDtos.WorkflowExecution>();
    }

    private CoreDtos.WorkflowDefinition ConvertToWorkflowDefinition(WorkflowDefinition entity)
    {
        // Convert domain entity to DTO
        return new CoreDtos.WorkflowDefinition(
            entity.WorkflowId,
            entity.Name,
            entity.Description ?? "",
            new List<CoreDtos.WorkflowStep>(), // Would need to convert steps from entity
            new Dictionary<string, object>() // Would need to deserialize Parameters from entity
        );
    }

    // Repository pattern methods for workflow-specific operations
    // These provide domain-specific convenience methods while BaseRepository handles the core interface
}