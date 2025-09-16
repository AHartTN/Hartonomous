using Hartonomous.Orchestration.DTOs;

namespace Hartonomous.Orchestration.Interfaces;

/// <summary>
/// Interface for workflow execution engine
/// </summary>
public interface IWorkflowExecutionEngine
{
    /// <summary>
    /// Start executing a workflow
    /// </summary>
    Task<Guid> StartWorkflowAsync(Guid workflowId, Dictionary<string, object>? input,
        Dictionary<string, object>? configuration, string userId, string? executionName = null);

    /// <summary>
    /// Resume a paused workflow execution
    /// </summary>
    Task<bool> ResumeWorkflowAsync(Guid executionId, string userId);

    /// <summary>
    /// Pause a running workflow execution
    /// </summary>
    Task<bool> PauseWorkflowAsync(Guid executionId, string userId);

    /// <summary>
    /// Cancel a workflow execution
    /// </summary>
    Task<bool> CancelWorkflowAsync(Guid executionId, string userId);

    /// <summary>
    /// Retry a failed workflow execution
    /// </summary>
    Task<bool> RetryWorkflowAsync(Guid executionId, string userId);

    /// <summary>
    /// Get the current status of a workflow execution
    /// </summary>
    Task<WorkflowExecutionDto?> GetExecutionStatusAsync(Guid executionId, string userId);

    /// <summary>
    /// Get real-time execution progress
    /// </summary>
    Task<WorkflowExecutionDto?> GetExecutionProgressAsync(Guid executionId, string userId);

    /// <summary>
    /// Validate a workflow definition before execution
    /// </summary>
    Task<WorkflowValidationResult> ValidateWorkflowAsync(string workflowDefinition);

    /// <summary>
    /// Execute a single node for testing
    /// </summary>
    Task<Dictionary<string, object>?> ExecuteNodeAsync(string nodeDefinition,
        Dictionary<string, object>? input, string userId);
}