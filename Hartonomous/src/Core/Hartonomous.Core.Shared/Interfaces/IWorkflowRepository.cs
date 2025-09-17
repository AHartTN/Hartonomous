using Hartonomous.Core.Shared.DTOs;

namespace Hartonomous.Core.Shared.Interfaces;

/// <summary>
/// Repository interface for workflow management in MCP system
/// </summary>
public interface IWorkflowRepository : IRepository<WorkflowDefinition>
{
    /// <summary>
    /// Get workflows by user ID
    /// </summary>
    Task<IEnumerable<WorkflowDefinition>> GetWorkflowsByUserAsync(string userId);

    /// <summary>
    /// Get workflow by ID
    /// </summary>
    Task<WorkflowDefinition?> GetWorkflowAsync(Guid workflowId, string userId);

    /// <summary>
    /// Create new workflow
    /// </summary>
    Task<Guid> CreateWorkflowAsync(WorkflowDefinition workflow, string userId);

    /// <summary>
    /// Delete workflow
    /// </summary>
    Task<bool> DeleteWorkflowAsync(Guid workflowId, string userId);
    /// <summary>
    /// Start workflow execution
    /// </summary>
    Task<Guid> StartWorkflowExecutionAsync(Guid workflowId, Guid projectId, string userId, Dictionary<string, object>? parameters = null);

    /// <summary>
    /// Get workflow execution by ID with user scope
    /// </summary>
    Task<WorkflowExecution?> GetWorkflowExecutionAsync(Guid executionId, string userId);

    /// <summary>
    /// Update workflow execution status
    /// </summary>
    Task<bool> UpdateWorkflowExecutionStatusAsync(Guid executionId, WorkflowExecutionStatus status, string userId, string? errorMessage = null);

    /// <summary>
    /// Update step execution
    /// </summary>
    Task<bool> UpdateStepExecutionAsync(Guid stepExecutionId, StepExecutionStatus status, string userId, object? output = null, string? errorMessage = null);

    /// <summary>
    /// Get pending step executions for assignment
    /// </summary>
    Task<IEnumerable<StepExecution>> GetPendingStepExecutionsAsync(string userId);

    /// <summary>
    /// Assign step execution to agent
    /// </summary>
    Task<bool> AssignStepToAgentAsync(Guid stepExecutionId, Guid agentId, string userId);

    /// <summary>
    /// Get workflow executions by project
    /// </summary>
    Task<IEnumerable<WorkflowExecution>> GetWorkflowExecutionsByProjectAsync(Guid projectId, string userId);
}