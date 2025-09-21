using System.ComponentModel.DataAnnotations;
using Hartonomous.Core.DTOs;

namespace Hartonomous.Orchestration.DTOs;

/// <summary>
/// Create workflow definition request
/// </summary>
public record CreateWorkflowRequest(
    [Required(ErrorMessage = "Workflow name is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Workflow name must be between 1 and 256 characters")]
    string Name,

    [Required(ErrorMessage = "Description is required")]
    [StringLength(1000, MinimumLength = 1, ErrorMessage = "Description must be between 1 and 1000 characters")]
    string Description,

    [Required(ErrorMessage = "Workflow definition is required")]
    string WorkflowDefinition,

    string? Category = null,
    Dictionary<string, object>? Parameters = null,
    List<string>? Tags = null
);

/// <summary>
/// Update workflow definition request
/// </summary>
public record UpdateWorkflowRequest(
    string? Name = null,
    string? Description = null,
    string? WorkflowDefinition = null,
    string? Category = null,
    Dictionary<string, object>? Parameters = null,
    List<string>? Tags = null
);

/// <summary>
/// Workflow definition DTO
/// </summary>
public record WorkflowDefinitionDto(
    Guid WorkflowId,
    string Name,
    string Description,
    string WorkflowDefinition,
    string? Category,
    Dictionary<string, object>? Parameters,
    List<string>? Tags,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string CreatedBy,
    int Version,
    WorkflowStatus Status
);

/// <summary>
/// Start workflow execution request
/// </summary>
public record StartWorkflowExecutionRequest(
    [Required(ErrorMessage = "Workflow ID is required")]
    Guid WorkflowId,

    Dictionary<string, object>? Input = null,
    Dictionary<string, object>? Configuration = null,
    string? ExecutionName = null,
    int Priority = 0
);

/// <summary>
/// Workflow execution DTO
/// </summary>
public record WorkflowExecutionDto(
    Guid ExecutionId,
    Guid WorkflowId,
    string WorkflowName,
    string? ExecutionName,
    Dictionary<string, object>? Input,
    Dictionary<string, object>? Output,
    WorkflowExecutionStatus Status,
    DateTime StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    string StartedBy,
    int Priority,
    TimeSpan? Duration,
    List<NodeExecutionDto> NodeExecutions
);

/// <summary>
/// Node execution within a workflow
/// </summary>
public record NodeExecutionDto(
    Guid NodeExecutionId,
    string NodeId,
    string NodeType,
    string NodeName,
    Dictionary<string, object>? Input,
    Dictionary<string, object>? Output,
    NodeExecutionStatus Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? ErrorMessage,
    TimeSpan? Duration,
    int RetryCount,
    Dictionary<string, object>? Metadata
);

/// <summary>
/// Workflow state snapshot
/// </summary>
public record WorkflowStateDto(
    Guid ExecutionId,
    Dictionary<string, object> State,
    string CurrentNode,
    List<string> CompletedNodes,
    List<string> PendingNodes,
    DateTime LastUpdated
);

/// <summary>
/// Workflow template DTO
/// </summary>
public record WorkflowTemplateDto(
    Guid TemplateId,
    string Name,
    string Description,
    string Category,
    string TemplateDefinition,
    Dictionary<string, ParameterDefinition> Parameters,
    List<string> Tags,
    DateTime CreatedAt,
    string CreatedBy,
    int UsageCount,
    bool IsPublic
);

/// <summary>
/// Parameter definition for templates
/// </summary>
public record ParameterDefinition(
    string Name,
    string Type,
    string Description,
    bool Required,
    object? DefaultValue,
    List<object>? AllowedValues,
    string? ValidationPattern
);

/// <summary>
/// Create template from workflow request
/// </summary>
public record CreateTemplateFromWorkflowRequest(
    [Required(ErrorMessage = "Workflow ID is required")]
    Guid WorkflowId,

    [Required(ErrorMessage = "Template name is required")]
    string Name,

    [Required(ErrorMessage = "Description is required")]
    string Description,

    string? Category = null,
    List<string>? Tags = null,
    bool IsPublic = false
);

/// <summary>
/// Workflow execution statistics
/// </summary>
public record WorkflowExecutionStatsDto(
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    int RunningExecutions,
    double AverageExecutionTime,
    double SuccessRate,
    DateTime? LastExecution,
    List<ExecutionTrendDataPoint> TrendData
);

/// <summary>
/// Execution trend data point
/// </summary>
public record ExecutionTrendDataPoint(
    DateTime Date,
    int TotalExecutions,
    int SuccessfulExecutions,
    int FailedExecutions,
    double AverageExecutionTime
);

/// <summary>
/// Workflow debug information
/// </summary>
public record WorkflowDebugDto(
    Guid ExecutionId,
    List<DebugEvent> Events,
    Dictionary<string, object> Variables,
    List<BreakpointDto> Breakpoints,
    string? CurrentBreakpoint
);

/// <summary>
/// Debug event during workflow execution
/// </summary>
public record DebugEvent(
    Guid EventId,
    string EventType,
    string NodeId,
    DateTime Timestamp,
    Dictionary<string, object> Data,
    string? Message
);

/// <summary>
/// Breakpoint definition
/// </summary>
public record BreakpointDto(
    Guid BreakpointId,
    string NodeId,
    string? Condition,
    bool IsEnabled,
    DateTime CreatedAt
);

/// <summary>
/// Workflow execution pause/resume request
/// </summary>
public record WorkflowControlRequest(
    [Required(ErrorMessage = "Execution ID is required")]
    Guid ExecutionId,

    [Required(ErrorMessage = "Action is required")]
    WorkflowControlAction Action,

    string? Reason = null
);

/// <summary>
/// Workflow search request
/// </summary>
public record WorkflowSearchRequest(
    string? Query = null,
    string? Category = null,
    List<string>? Tags = null,
    WorkflowStatus? Status = null,
    DateTime? CreatedAfter = null,
    DateTime? CreatedBefore = null,
    int Page = 1,
    int PageSize = 20,
    string SortBy = "CreatedAt",
    string SortDirection = "DESC"
);

/// <summary>
/// Paginated search result
/// </summary>
public record PaginatedResult<T>(
    List<T> Items,
    int TotalCount,
    int Page,
    int PageSize,
    int TotalPages
);

/// <summary>
/// Workflow validation result
/// </summary>
public record WorkflowValidationResult(
    bool IsValid,
    List<ValidationError> Errors,
    List<ValidationWarning> Warnings
);

/// <summary>
/// Validation error
/// </summary>
public record ValidationError(
    string Code,
    string Message,
    string? NodeId = null,
    string? Path = null
);

/// <summary>
/// Validation warning
/// </summary>
public record ValidationWarning(
    string Code,
    string Message,
    string? NodeId = null,
    string? Path = null
);

/// <summary>
/// Workflow status enumeration
/// </summary>
public enum WorkflowStatus
{
    Draft,
    Active,
    Inactive,
    Deprecated,
    Archived
}

/// <summary>
/// Workflow execution status enumeration
/// </summary>
public enum WorkflowExecutionStatus
{
    Pending,
    Running,
    Paused,
    Completed,
    Failed,
    Cancelled,
    TimedOut
}

/// <summary>
/// Node execution status enumeration
/// </summary>
public enum NodeExecutionStatus
{
    Pending,
    Running,
    Completed,
    Failed,
    Skipped,
    Cancelled,
    TimedOut
}

/// <summary>
/// Workflow control action enumeration
/// </summary>
public enum WorkflowControlAction
{
    Pause,
    Resume,
    Cancel,
    Retry,
    Stop
}