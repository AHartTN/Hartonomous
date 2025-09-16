using System.ComponentModel.DataAnnotations;

namespace Hartonomous.MCP.DTOs;

/// <summary>
/// Agent registration request for MCP system
/// </summary>
public record AgentRegistrationRequest(
    [Required(ErrorMessage = "Agent name is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Agent name must be between 1 and 256 characters")]
    string AgentName,

    [Required(ErrorMessage = "Agent type is required")]
    string AgentType,

    [Required(ErrorMessage = "Capabilities are required")]
    IEnumerable<string> Capabilities,

    string? Description = null,

    Dictionary<string, object>? Configuration = null
);

/// <summary>
/// Agent information DTO
/// </summary>
public record AgentDto(
    Guid AgentId,
    string AgentName,
    string AgentType,
    string ConnectionId,
    IEnumerable<string> Capabilities,
    string? Description,
    Dictionary<string, object>? Configuration,
    DateTime RegisteredAt,
    DateTime LastHeartbeat,
    AgentStatus Status
);

/// <summary>
/// MCP message for agent communication
/// </summary>
public record McpMessage(
    Guid MessageId,
    Guid FromAgentId,
    Guid? ToAgentId,
    string MessageType,
    object Payload,
    Dictionary<string, object>? Metadata = null,
    DateTime Timestamp = default
)
{
    public DateTime Timestamp { get; init; } = Timestamp == default ? DateTime.UtcNow : Timestamp;
}

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
/// Agent heartbeat message
/// </summary>
public record AgentHeartbeat(
    Guid AgentId,
    AgentStatus Status,
    Dictionary<string, object>? Metrics = null
);

/// <summary>
/// Agent discovery request
/// </summary>
public record AgentDiscoveryRequest(
    string? AgentType = null,
    IEnumerable<string>? RequiredCapabilities = null
);

/// <summary>
/// Agent discovery response
/// </summary>
public record AgentDiscoveryResponse(
    IEnumerable<AgentDto> AvailableAgents
);

/// <summary>
/// Task assignment for agents
/// </summary>
public record TaskAssignment(
    Guid TaskId,
    Guid AgentId,
    string TaskType,
    object TaskData,
    int Priority = 0,
    DateTime? DueDate = null,
    Dictionary<string, object>? Metadata = null
);

/// <summary>
/// Task result from agent
/// </summary>
public record TaskResult(
    Guid TaskId,
    Guid AgentId,
    TaskResultStatus Status,
    object? Result = null,
    string? ErrorMessage = null,
    Dictionary<string, object>? Metrics = null
);

/// <summary>
/// Agent status enumeration
/// </summary>
public enum AgentStatus
{
    Connecting,
    Online,
    Busy,
    Idle,
    Offline,
    Error
}

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

/// <summary>
/// Task result status
/// </summary>
public enum TaskResultStatus
{
    Success,
    Failed,
    Cancelled,
    Timeout
}