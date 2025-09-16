using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Models;

namespace Hartonomous.Orchestration.Interfaces;

/// <summary>
/// Repository interface for workflow management
/// </summary>
public interface IWorkflowRepository
{
    // Workflow Definition Operations
    Task<Guid> CreateWorkflowAsync(CreateWorkflowRequest request, string userId);
    Task<WorkflowDefinitionDto?> GetWorkflowByIdAsync(Guid workflowId, string userId);
    Task<bool> UpdateWorkflowAsync(Guid workflowId, UpdateWorkflowRequest request, string userId);
    Task<bool> DeleteWorkflowAsync(Guid workflowId, string userId);
    Task<PaginatedResult<WorkflowDefinitionDto>> SearchWorkflowsAsync(WorkflowSearchRequest request, string userId);
    Task<List<WorkflowDefinitionDto>> GetWorkflowsByUserAsync(string userId, int limit = 100);
    Task<bool> UpdateWorkflowStatusAsync(Guid workflowId, DTOs.WorkflowStatus status, string userId);
    Task<DTOs.WorkflowValidationResult> ValidateWorkflowAsync(string workflowDefinition);

    // Workflow Execution Operations
    Task<Guid> StartWorkflowExecutionAsync(StartWorkflowExecutionRequest request, string userId);
    Task<WorkflowExecutionDto?> GetExecutionByIdAsync(Guid executionId, string userId);
    Task<bool> UpdateExecutionStatusAsync(Guid executionId, DTOs.WorkflowExecutionStatus status, string? errorMessage, string userId);
    Task<bool> UpdateExecutionOutputAsync(Guid executionId, Dictionary<string, object> output, string userId);
    Task<List<WorkflowExecutionDto>> GetExecutionsByWorkflowAsync(Guid workflowId, string userId, int limit = 100);
    Task<List<WorkflowExecutionDto>> GetActiveExecutionsAsync(string userId);
    Task<bool> CancelExecutionAsync(Guid executionId, string userId);

    // Node Execution Operations
    Task<Guid> CreateNodeExecutionAsync(Guid executionId, NodeExecutionDto nodeExecution);
    Task<bool> UpdateNodeExecutionAsync(Guid nodeExecutionId, DTOs.NodeExecutionStatus status,
        Dictionary<string, object>? output, string? errorMessage);
    Task<List<NodeExecutionDto>> GetNodeExecutionsByExecutionAsync(Guid executionId);

    // Workflow State Operations
    Task<bool> SaveWorkflowStateAsync(Guid executionId, WorkflowStateDto state);
    Task<WorkflowStateDto?> GetWorkflowStateAsync(Guid executionId);
    Task<List<WorkflowStateDto>> GetWorkflowStateHistoryAsync(Guid executionId, int limit = 10);

    // Workflow Events and Debugging
    Task<bool> CreateWorkflowEventAsync(Guid executionId, DebugEvent debugEvent);
    Task<List<DebugEvent>> GetWorkflowEventsAsync(Guid executionId, DateTime? since = null);
    Task<bool> CreateBreakpointAsync(Guid executionId, BreakpointDto breakpoint, string userId);
    Task<bool> RemoveBreakpointAsync(Guid breakpointId, string userId);
    Task<List<BreakpointDto>> GetBreakpointsByExecutionAsync(Guid executionId, string userId);

    // Metrics and Statistics
    Task<WorkflowExecutionStatsDto> GetWorkflowStatsAsync(Guid workflowId, string userId,
        DateTime? fromDate = null, DateTime? toDate = null);
    Task<bool> RecordExecutionMetricAsync(Guid executionId, string metricName, double value,
        string? unit = null, Dictionary<string, string>? tags = null);
}