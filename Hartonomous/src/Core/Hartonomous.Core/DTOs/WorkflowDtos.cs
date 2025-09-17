namespace Hartonomous.Core.DTOs;

/// <summary>
/// MCP workflow definition
/// </summary>
public record WorkflowDefinition(
    Guid WorkflowId,
    string WorkflowName,
    string Description,
    IEnumerable<WorkflowStep> Steps,
    Dictionary<string, object>? Parameters = null
);

/// <summary>
/// Individual workflow step
/// </summary>
public record WorkflowStep(
    Guid StepId,
    string StepName,
    string AgentType,
    object Input,
    IEnumerable<string>? DependsOn = null,
    Dictionary<string, object>? Configuration = null
);

/// <summary>
/// Workflow execution instance
/// </summary>
public record WorkflowExecution(
    Guid ExecutionId,
    Guid WorkflowId,
    Guid ProjectId,
    string UserId,
    WorkflowExecutionStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    IEnumerable<StepExecution> StepExecutions,
    string? ErrorMessage = null
);

/// <summary>
/// Individual step execution
/// </summary>
public record StepExecution(
    Guid StepExecutionId,
    Guid StepId,
    Guid? AssignedAgentId,
    StepExecutionStatus Status,
    object? Input,
    object? Output,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage = null
);

/// <summary>
/// Workflow execution status
/// </summary>
public enum WorkflowExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Cancelled
}

/// <summary>
/// Step execution status
/// </summary>
public enum StepExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped
}
