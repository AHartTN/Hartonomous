using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hartonomous.Orchestration.Services;

/// <summary>
/// Workflow state management service
/// </summary>
public class WorkflowStateManager : IWorkflowStateManager
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly ILogger<WorkflowStateManager> _logger;
    private readonly Dictionary<Guid, WorkflowStateDto> _stateCache = new();

    public WorkflowStateManager(
        IWorkflowRepository workflowRepository,
        ILogger<WorkflowStateManager> logger)
    {
        _workflowRepository = workflowRepository;
        _logger = logger;
    }

    public async Task<bool> InitializeStateAsync(Guid executionId, Dictionary<string, object> initialState)
    {
        try
        {
            _logger.LogInformation("Initializing state for execution {ExecutionId}", executionId);

            var state = new WorkflowStateDto(
                executionId,
                initialState,
                string.Empty,
                new List<string>(),
                new List<string>(),
                DateTime.UtcNow
            );

            var result = await _workflowRepository.SaveWorkflowStateAsync(executionId, state);

            if (result)
            {
                _stateCache[executionId] = state;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize state for execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> UpdateStateAsync(Guid executionId, Dictionary<string, object> state)
    {
        try
        {
            _logger.LogDebug("Updating state for execution {ExecutionId}", executionId);

            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            // Merge state updates
            var mergedState = new Dictionary<string, object>(currentState.State);
            foreach (var kvp in state)
            {
                mergedState[kvp.Key] = kvp.Value;
            }

            var updatedState = currentState with
            {
                State = mergedState,
                LastUpdated = DateTime.UtcNow
            };

            var result = await _workflowRepository.SaveWorkflowStateAsync(executionId, updatedState);

            if (result)
            {
                _stateCache[executionId] = updatedState;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update state for execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<WorkflowStateDto?> GetCurrentStateAsync(Guid executionId)
    {
        try
        {
            // Check cache first
            if (_stateCache.TryGetValue(executionId, out var cachedState))
            {
                return cachedState;
            }

            // Load from repository
            var state = await _workflowRepository.GetWorkflowStateAsync(executionId);
            if (state != null)
            {
                _stateCache[executionId] = state;
            }

            return state;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get current state for execution {ExecutionId}", executionId);
            return null;
        }
    }

    public async Task<WorkflowStateDto?> GetStateAtVersionAsync(Guid executionId, int version)
    {
        try
        {
            var stateHistory = await _workflowRepository.GetWorkflowStateHistoryAsync(executionId, 100);
            return stateHistory.FirstOrDefault(); // In a real implementation, you'd filter by version
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state at version {Version} for execution {ExecutionId}",
                version, executionId);
            return null;
        }
    }

    public async Task<List<WorkflowStateDto>> GetStateHistoryAsync(Guid executionId, int limit = 10)
    {
        try
        {
            return await _workflowRepository.GetWorkflowStateHistoryAsync(executionId, limit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get state history for execution {ExecutionId}", executionId);
            return new List<WorkflowStateDto>();
        }
    }

    public async Task<bool> SetVariableAsync(Guid executionId, string key, object value)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            var variables = currentState.State.GetValueOrDefault("variables", new Dictionary<string, object>()) as Dictionary<string, object>
                ?? new Dictionary<string, object>();

            variables[key] = value;

            return await UpdateStateAsync(executionId, new Dictionary<string, object>
            {
                ["variables"] = variables
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set variable {Key} for execution {ExecutionId}", key, executionId);
            return false;
        }
    }

    public async Task<T?> GetVariableAsync<T>(Guid executionId, string key)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState?.State.ContainsKey("variables") == true &&
                currentState.State["variables"] is Dictionary<string, object> variables &&
                variables.ContainsKey(key))
            {
                var value = variables[key];

                if (value is T directValue)
                {
                    return directValue;
                }

                if (value is JsonElement jsonElement)
                {
                    return JsonSerializer.Deserialize<T>(jsonElement.GetRawText());
                }

                // Try to convert
                try
                {
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch
                {
                    return default(T);
                }
            }

            return default(T);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get variable {Key} for execution {ExecutionId}", key, executionId);
            return default(T);
        }
    }

    public async Task<bool> RemoveVariableAsync(Guid executionId, string key)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState?.State.ContainsKey("variables") == true &&
                currentState.State["variables"] is Dictionary<string, object> variables)
            {
                variables.Remove(key);

                return await UpdateStateAsync(executionId, new Dictionary<string, object>
                {
                    ["variables"] = variables
                });
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove variable {Key} for execution {ExecutionId}", key, executionId);
            return false;
        }
    }

    public async Task<bool> UpdateCurrentNodeAsync(Guid executionId, string nodeId)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            var updatedState = currentState with
            {
                CurrentNode = nodeId,
                LastUpdated = DateTime.UtcNow
            };

            var result = await _workflowRepository.SaveWorkflowStateAsync(executionId, updatedState);

            if (result)
            {
                _stateCache[executionId] = updatedState;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update current node for execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> MarkNodeCompletedAsync(Guid executionId, string nodeId)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            var completedNodes = new List<string>(currentState.CompletedNodes);
            if (!completedNodes.Contains(nodeId))
            {
                completedNodes.Add(nodeId);
            }

            var pendingNodes = new List<string>(currentState.PendingNodes);
            pendingNodes.Remove(nodeId);

            var updatedState = currentState with
            {
                CompletedNodes = completedNodes,
                PendingNodes = pendingNodes,
                LastUpdated = DateTime.UtcNow
            };

            var result = await _workflowRepository.SaveWorkflowStateAsync(executionId, updatedState);

            if (result)
            {
                _stateCache[executionId] = updatedState;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to mark node {NodeId} as completed for execution {ExecutionId}",
                nodeId, executionId);
            return false;
        }
    }

    public async Task<bool> AddPendingNodeAsync(Guid executionId, string nodeId)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            var pendingNodes = new List<string>(currentState.PendingNodes);
            if (!pendingNodes.Contains(nodeId))
            {
                pendingNodes.Add(nodeId);
            }

            var updatedState = currentState with
            {
                PendingNodes = pendingNodes,
                LastUpdated = DateTime.UtcNow
            };

            var result = await _workflowRepository.SaveWorkflowStateAsync(executionId, updatedState);

            if (result)
            {
                _stateCache[executionId] = updatedState;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add pending node {NodeId} for execution {ExecutionId}",
                nodeId, executionId);
            return false;
        }
    }

    public async Task<bool> RemovePendingNodeAsync(Guid executionId, string nodeId)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            var pendingNodes = new List<string>(currentState.PendingNodes);
            pendingNodes.Remove(nodeId);

            var updatedState = currentState with
            {
                PendingNodes = pendingNodes,
                LastUpdated = DateTime.UtcNow
            };

            var result = await _workflowRepository.SaveWorkflowStateAsync(executionId, updatedState);

            if (result)
            {
                _stateCache[executionId] = updatedState;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove pending node {NodeId} for execution {ExecutionId}",
                nodeId, executionId);
            return false;
        }
    }

    public async Task<bool> CanProceedToNodeAsync(Guid executionId, string nodeId, List<string> dependencies)
    {
        try
        {
            if (!dependencies.Any())
            {
                return true;
            }

            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            // Check if all dependencies are completed
            return dependencies.All(dep => currentState.CompletedNodes.Contains(dep));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check if can proceed to node {NodeId} for execution {ExecutionId}",
                nodeId, executionId);
            return false;
        }
    }

    public async Task<bool> CreateSnapshotAsync(Guid executionId)
    {
        try
        {
            var currentState = await GetCurrentStateAsync(executionId);
            if (currentState == null)
            {
                return false;
            }

            // Create a snapshot by saving current state with snapshot flag
            var snapshotState = currentState with
            {
                State = new Dictionary<string, object>(currentState.State)
                {
                    ["isSnapshot"] = true,
                    ["snapshotCreatedAt"] = DateTime.UtcNow
                }
            };

            return await _workflowRepository.SaveWorkflowStateAsync(executionId, snapshotState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot for execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> RestoreFromSnapshotAsync(Guid executionId, int snapshotVersion)
    {
        try
        {
            var stateHistory = await _workflowRepository.GetWorkflowStateHistoryAsync(executionId, 100);
            var snapshot = stateHistory.FirstOrDefault(); // In a real implementation, you'd find by version

            if (snapshot == null)
            {
                return false;
            }

            // Restore state from snapshot
            var restoredState = snapshot with
            {
                State = new Dictionary<string, object>(snapshot.State)
                {
                    ["restoredAt"] = DateTime.UtcNow,
                    ["restoredFromVersion"] = snapshotVersion
                },
                LastUpdated = DateTime.UtcNow
            };

            var result = await _workflowRepository.SaveWorkflowStateAsync(executionId, restoredState);

            if (result)
            {
                _stateCache[executionId] = restoredState;
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore from snapshot for execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> ClearStateAsync(Guid executionId)
    {
        try
        {
            _stateCache.Remove(executionId);

            // In a real implementation, you might want to archive the state instead of deleting
            var emptyState = new WorkflowStateDto(
                executionId,
                new Dictionary<string, object>(),
                string.Empty,
                new List<string>(),
                new List<string>(),
                DateTime.UtcNow
            );

            return await _workflowRepository.SaveWorkflowStateAsync(executionId, emptyState);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear state for execution {ExecutionId}", executionId);
            return false;
        }
    }
}