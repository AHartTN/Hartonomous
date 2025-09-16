using Hartonomous.Orchestration.DTOs;

namespace Hartonomous.Orchestration.Interfaces;

/// <summary>
/// Interface for workflow state management
/// </summary>
public interface IWorkflowStateManager
{
    /// <summary>
    /// Initialize workflow state for new execution
    /// </summary>
    Task<bool> InitializeStateAsync(Guid executionId, Dictionary<string, object> initialState);

    /// <summary>
    /// Update workflow state
    /// </summary>
    Task<bool> UpdateStateAsync(Guid executionId, Dictionary<string, object> state);

    /// <summary>
    /// Get current workflow state
    /// </summary>
    Task<WorkflowStateDto?> GetCurrentStateAsync(Guid executionId);

    /// <summary>
    /// Get state at specific version
    /// </summary>
    Task<WorkflowStateDto?> GetStateAtVersionAsync(Guid executionId, int version);

    /// <summary>
    /// Get state history
    /// </summary>
    Task<List<WorkflowStateDto>> GetStateHistoryAsync(Guid executionId, int limit = 10);

    /// <summary>
    /// Set variable in workflow state
    /// </summary>
    Task<bool> SetVariableAsync(Guid executionId, string key, object value);

    /// <summary>
    /// Get variable from workflow state
    /// </summary>
    Task<T?> GetVariableAsync<T>(Guid executionId, string key);

    /// <summary>
    /// Remove variable from workflow state
    /// </summary>
    Task<bool> RemoveVariableAsync(Guid executionId, string key);

    /// <summary>
    /// Update current node in execution
    /// </summary>
    Task<bool> UpdateCurrentNodeAsync(Guid executionId, string nodeId);

    /// <summary>
    /// Mark node as completed
    /// </summary>
    Task<bool> MarkNodeCompletedAsync(Guid executionId, string nodeId);

    /// <summary>
    /// Add node to pending list
    /// </summary>
    Task<bool> AddPendingNodeAsync(Guid executionId, string nodeId);

    /// <summary>
    /// Remove node from pending list
    /// </summary>
    Task<bool> RemovePendingNodeAsync(Guid executionId, string nodeId);

    /// <summary>
    /// Check if execution can proceed to next node
    /// </summary>
    Task<bool> CanProceedToNodeAsync(Guid executionId, string nodeId, List<string> dependencies);

    /// <summary>
    /// Create state snapshot for recovery
    /// </summary>
    Task<bool> CreateSnapshotAsync(Guid executionId);

    /// <summary>
    /// Restore state from snapshot
    /// </summary>
    Task<bool> RestoreFromSnapshotAsync(Guid executionId, int snapshotVersion);

    /// <summary>
    /// Clear execution state
    /// </summary>
    Task<bool> ClearStateAsync(Guid executionId);
}