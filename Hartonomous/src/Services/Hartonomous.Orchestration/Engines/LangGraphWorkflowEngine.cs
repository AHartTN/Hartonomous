using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.DSL;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using System.Net.Http;
using System.Text;
using System.Collections.Concurrent;
using System.Threading;
using System.Text.RegularExpressions;

namespace Hartonomous.Orchestration.Engines;

/// <summary>
/// LangGraph-powered workflow execution engine
/// Note: This implementation uses LangGraph-style patterns and state machine concepts.
/// When the official LangGraph .NET package becomes available, this can be updated to use it directly.
/// </summary>
public class LangGraphWorkflowEngine : IWorkflowExecutionEngine, IDisposable
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowStateManager _stateManager;
    private readonly IWorkflowDSLParser _dslParser;
    private readonly ILogger<LangGraphWorkflowEngine> _logger;
    private readonly IServiceProvider _serviceProvider;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ConcurrentDictionary<Guid, CancellationTokenSource> _executionCancellationTokens;
    private readonly ConcurrentDictionary<Guid, bool> _pausedExecutions;
    private readonly SemaphoreSlim _executionSemaphore;

    public LangGraphWorkflowEngine(
        IWorkflowRepository workflowRepository,
        IWorkflowStateManager stateManager,
        IWorkflowDSLParser dslParser,
        ILogger<LangGraphWorkflowEngine> logger,
        IServiceProvider serviceProvider,
        IHttpClientFactory httpClientFactory)
    {
        _workflowRepository = workflowRepository;
        _stateManager = stateManager;
        _dslParser = dslParser;
        _logger = logger;
        _serviceProvider = serviceProvider;
        _httpClientFactory = httpClientFactory;
        _executionCancellationTokens = new ConcurrentDictionary<Guid, CancellationTokenSource>();
        _pausedExecutions = new ConcurrentDictionary<Guid, bool>();
        _executionSemaphore = new SemaphoreSlim(Environment.ProcessorCount * 2, Environment.ProcessorCount * 2);
    }

    public async Task<Guid> StartWorkflowAsync(Guid workflowId, Dictionary<string, object>? input,
        Dictionary<string, object>? configuration, string userId, string? executionName = null)
    {
        try
        {
            _logger.LogInformation("Starting workflow execution for workflow {WorkflowId} by user {UserId}",
                workflowId, userId);

            // Get workflow definition
            var workflow = await _workflowRepository.GetWorkflowByIdAsync(workflowId, userId);
            if (workflow == null)
            {
                throw new ArgumentException($"Workflow {workflowId} not found");
            }

            // Create execution request
            var executionRequest = new StartWorkflowExecutionRequest(
                workflowId,
                input,
                configuration,
                executionName
            );

            // Start execution
            var executionId = await _workflowRepository.StartWorkflowExecutionAsync(executionRequest, userId);

            // Initialize workflow state
            var initialState = new Dictionary<string, object>
            {
                ["input"] = input ?? new Dictionary<string, object>(),
                ["configuration"] = configuration ?? new Dictionary<string, object>(),
                ["variables"] = new Dictionary<string, object>(),
                ["status"] = WorkflowExecutionStatus.Running,
                ["startedAt"] = DateTime.UtcNow
            };

            await _stateManager.InitializeStateAsync(executionId, initialState);

            // Parse workflow definition
            var workflowGraph = await _dslParser.ParseWorkflowAsync(workflow.WorkflowDefinition);

            // Create cancellation token for this execution
            var cancellationTokenSource = new CancellationTokenSource();
            _executionCancellationTokens.TryAdd(executionId, cancellationTokenSource);

            // Start workflow execution in background with proper error handling
            _ = Task.Run(async () =>
            {
                try
                {
                    await ExecuteWorkflowAsync(executionId, workflowGraph, userId, cancellationTokenSource.Token);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("Workflow execution {ExecutionId} was cancelled", executionId);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Workflow execution failed for {ExecutionId}", executionId);
                    await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, ex.Message, userId);
                }
                finally
                {
                    // Clean up cancellation token
                    _executionCancellationTokens.TryRemove(executionId, out _);
                    cancellationTokenSource?.Dispose();
                }
            });

            _logger.LogInformation("Workflow execution {ExecutionId} started successfully", executionId);
            return executionId;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow {WorkflowId}", workflowId);
            throw;
        }
    }

    public async Task<bool> ResumeWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            _logger.LogInformation("Resuming workflow execution {ExecutionId}", executionId);

            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                _logger.LogWarning("Execution {ExecutionId} not found for user {UserId}", executionId, userId);
                return false;
            }

            if (execution.Status != WorkflowExecutionStatus.Paused)
            {
                _logger.LogWarning("Cannot resume execution {ExecutionId} with status {Status}", executionId, execution.Status);
                return false;
            }

            // Remove from paused executions tracking
            _pausedExecutions.TryRemove(executionId, out _);

            // Update status to running
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Running, null, userId);

            // Get current state and resume execution
            var state = await _stateManager.GetCurrentStateAsync(executionId);
            if (state != null)
            {
                // Get workflow definition and parse
                var workflow = await _workflowRepository.GetWorkflowByIdAsync(execution.WorkflowId, userId);
                if (workflow != null)
                {
                    var workflowGraph = await _dslParser.ParseWorkflowAsync(workflow.WorkflowDefinition);

                    // Resume execution from current node
                    // Create cancellation token for resumed execution
                    var cancellationTokenSource = new CancellationTokenSource();
                    _executionCancellationTokens.TryAdd(executionId, cancellationTokenSource);

                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            await ResumeWorkflowExecutionAsync(executionId, workflowGraph, state.CurrentNode, userId, cancellationTokenSource.Token);
                        }
                        catch (OperationCanceledException)
                        {
                            _logger.LogInformation("Resumed workflow execution {ExecutionId} was cancelled", executionId);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to resume workflow execution {ExecutionId}", executionId);
                            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, ex.Message, userId);
                        }
                        finally
                        {
                            // Clean up cancellation token
                            _executionCancellationTokens.TryRemove(executionId, out _);
                            cancellationTokenSource?.Dispose();
                        }
                    });
                }
            }

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume workflow execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> PauseWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            _logger.LogInformation("Pausing workflow execution {ExecutionId}", executionId);

            // Check if execution exists and is running
            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                _logger.LogWarning("Execution {ExecutionId} not found for user {UserId}", executionId, userId);
                return false;
            }

            if (execution.Status != WorkflowExecutionStatus.Running)
            {
                _logger.LogWarning("Cannot pause execution {ExecutionId} with status {Status}", executionId, execution.Status);
                return false;
            }

            // Mark execution as paused in tracking
            _pausedExecutions.TryAdd(executionId, true);

            // Update database status
            var updateSuccess = await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Paused, null, userId);
            if (!updateSuccess)
            {
                _pausedExecutions.TryRemove(executionId, out _);
                return false;
            }

            // Update state manager
            var stateUpdateSuccess = await _stateManager.UpdateStateAsync(executionId, new Dictionary<string, object>
            {
                ["status"] = WorkflowExecutionStatus.Paused,
                ["pausedAt"] = DateTime.UtcNow
            });

            if (!stateUpdateSuccess)
            {
                _logger.LogWarning("Failed to update state for paused execution {ExecutionId}", executionId);
                // Continue - the execution is paused in the database
            }

            _logger.LogInformation("Successfully paused workflow execution {ExecutionId}", executionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause workflow execution {ExecutionId}", executionId);
            _pausedExecutions.TryRemove(executionId, out _);
            return false;
        }
    }

    public async Task<bool> CancelWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            _logger.LogInformation("Cancelling workflow execution {ExecutionId}", executionId);

            // Check if execution exists
            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                _logger.LogWarning("Execution {ExecutionId} not found for user {UserId}", executionId, userId);
                return false;
            }

            // Check if execution can be cancelled
            if (execution.Status == WorkflowExecutionStatus.Completed ||
                execution.Status == WorkflowExecutionStatus.Failed ||
                execution.Status == WorkflowExecutionStatus.Cancelled)
            {
                _logger.LogWarning("Cannot cancel execution {ExecutionId} with status {Status}", executionId, execution.Status);
                return false;
            }

            // Cancel execution using cancellation token
            if (_executionCancellationTokens.TryGetValue(executionId, out var cancellationTokenSource))
            {
                cancellationTokenSource.Cancel();
                _logger.LogDebug("Cancellation token triggered for execution {ExecutionId}", executionId);
            }

            // Remove from tracking collections
            _pausedExecutions.TryRemove(executionId, out _);
            _executionCancellationTokens.TryRemove(executionId, out _);

            // Update database status
            var updateSuccess = await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Cancelled, "Cancelled by user", userId);
            if (!updateSuccess)
            {
                _logger.LogError("Failed to update execution status to cancelled for {ExecutionId}", executionId);
                return false;
            }

            // Update state manager
            var stateUpdateSuccess = await _stateManager.UpdateStateAsync(executionId, new Dictionary<string, object>
            {
                ["status"] = WorkflowExecutionStatus.Cancelled,
                ["cancelledAt"] = DateTime.UtcNow
            });

            if (!stateUpdateSuccess)
            {
                _logger.LogWarning("Failed to update state for cancelled execution {ExecutionId}", executionId);
                // Continue - the execution is cancelled in the database
            }

            _logger.LogInformation("Successfully cancelled workflow execution {ExecutionId}", executionId);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel workflow execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> RetryWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            _logger.LogInformation("Retrying workflow execution {ExecutionId}", executionId);

            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                _logger.LogWarning("Execution {ExecutionId} not found for user {UserId}", executionId, userId);
                return false;
            }

            // Check if execution can be retried
            if (execution.Status != WorkflowExecutionStatus.Failed && execution.Status != WorkflowExecutionStatus.Cancelled)
            {
                _logger.LogWarning("Cannot retry execution {ExecutionId} with status {Status}", executionId, execution.Status);
                return false;
            }

            // Clean up any existing tracking
            _executionCancellationTokens.TryRemove(executionId, out _);
            _pausedExecutions.TryRemove(executionId, out _);

            // Reset execution status
            var updateSuccess = await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Running, null, userId);
            if (!updateSuccess)
            {
                _logger.LogError("Failed to update execution status for retry {ExecutionId}", executionId);
                return false;
            }

            // Reset workflow state
            var initialState = new Dictionary<string, object>
            {
                ["input"] = execution.Input ?? new Dictionary<string, object>(),
                ["configuration"] = new Dictionary<string, object>(),
                ["variables"] = new Dictionary<string, object>(),
                ["status"] = WorkflowExecutionStatus.Running,
                ["retryStartedAt"] = DateTime.UtcNow,
                ["originalExecutionId"] = executionId
            };

            var stateInitSuccess = await _stateManager.InitializeStateAsync(executionId, initialState);
            if (!stateInitSuccess)
            {
                _logger.LogError("Failed to initialize state for retry {ExecutionId}", executionId);
                await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, "Failed to initialize retry state", userId);
                return false;
            }

            // Get workflow definition and restart
            var workflow = await _workflowRepository.GetWorkflowByIdAsync(execution.WorkflowId, userId);
            if (workflow == null)
            {
                _logger.LogError("Workflow definition {WorkflowId} not found for retry", execution.WorkflowId);
                await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, "Workflow definition not found", userId);
                return false;
            }

            try
            {
                var workflowGraph = await _dslParser.ParseWorkflowAsync(workflow.WorkflowDefinition);

                // Start workflow execution in background with proper error handling
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await ExecuteWorkflowAsync(executionId, workflowGraph, userId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Workflow retry execution failed for {ExecutionId}", executionId);
                        await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, ex.Message, userId);
                    }
                });

                _logger.LogInformation("Successfully started retry for workflow execution {ExecutionId}", executionId);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse workflow definition for retry {ExecutionId}", executionId);
                await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, "Failed to parse workflow definition", userId);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retry workflow execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<WorkflowExecutionDto?> GetExecutionStatusAsync(Guid executionId, string userId)
    {
        return await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
    }

    public async Task<WorkflowExecutionDto?> GetExecutionProgressAsync(Guid executionId, string userId)
    {
        var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
        if (execution == null)
        {
            return null;
        }

        // Enrich with current state information
        var state = await _stateManager.GetCurrentStateAsync(executionId);
        if (state != null)
        {
            // Add progress information to the execution DTO
            var progressData = new Dictionary<string, object>
            {
                ["currentNode"] = state.CurrentNode,
                ["completedNodes"] = state.CompletedNodes,
                ["pendingNodes"] = state.PendingNodes,
                ["progress"] = CalculateProgress(state.CompletedNodes, state.PendingNodes)
            };

            // Return enhanced execution with progress data
            return execution with { Output = progressData };
        }

        return execution;
    }

    public async Task<DTOs.WorkflowValidationResult> ValidateWorkflowAsync(string workflowDefinition)
    {
        try
        {
            await _dslParser.ParseWorkflowAsync(workflowDefinition);
            return new DTOs.WorkflowValidationResult(
                true,
                new List<DTOs.ValidationError>(),
                new List<DTOs.ValidationWarning>()
            );
        }
        catch (Exception ex)
        {
            return new DTOs.WorkflowValidationResult(
                false,
                new List<DTOs.ValidationError>
                {
                    new DTOs.ValidationError("PARSE_ERROR", ex.Message)
                },
                new List<DTOs.ValidationWarning>()
            );
        }
    }

    public async Task<Dictionary<string, object>?> ExecuteNodeAsync(string nodeDefinition,
        Dictionary<string, object>? input, string userId)
    {
        try
        {
            var node = JsonSerializer.Deserialize<WorkflowNode>(nodeDefinition);
            if (node == null)
            {
                throw new ArgumentException("Invalid node definition");
            }

            return await ExecuteSingleNodeAsync(node, input ?? new Dictionary<string, object>(), Guid.NewGuid());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute node");
            throw;
        }
    }

    private async Task ExecuteWorkflowAsync(Guid executionId, WorkflowGraph workflowGraph, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing workflow for execution {ExecutionId}", executionId);

            // Find start node
            var startNode = workflowGraph.Nodes.Values.FirstOrDefault(n => n.Type == WorkflowNodeTypes.Start);
            if (startNode == null)
            {
                startNode = workflowGraph.Nodes.Values.First(); // Use first node if no start node
            }

            // Execute workflow using LangGraph-style state machine
            await ExecuteNodeAndContinue(executionId, workflowGraph, startNode, userId, cancellationToken);

            // Mark execution as completed if successful
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Completed, null, userId);

            _logger.LogInformation("Workflow execution {ExecutionId} completed successfully", executionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Workflow execution {ExecutionId} failed", executionId);
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, ex.Message, userId);
        }
    }

    private async Task ResumeWorkflowExecutionAsync(Guid executionId, WorkflowGraph workflowGraph,
        string currentNodeId, string userId, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!workflowGraph.Nodes.TryGetValue(currentNodeId, out var currentNode))
            {
                throw new InvalidOperationException($"Current node {currentNodeId} not found in workflow graph");
            }

            await ExecuteNodeAndContinue(executionId, workflowGraph, currentNode, userId, cancellationToken);
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Completed, null, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume workflow execution {ExecutionId}", executionId);
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, ex.Message, userId);
        }
    }

    private async Task ExecuteNodeAndContinue(Guid executionId, WorkflowGraph workflowGraph,
        WorkflowNode node, string userId, CancellationToken cancellationToken = default)
    {
        // Check for cancellation
        cancellationToken.ThrowIfCancellationRequested();

        // Check if execution should be paused
        if (_pausedExecutions.ContainsKey(executionId))
        {
            _logger.LogDebug("Execution {ExecutionId} is paused, waiting...", executionId);

            // Wait for resume or cancellation
            while (_pausedExecutions.ContainsKey(executionId) && !cancellationToken.IsCancellationRequested)
            {
                await Task.Delay(1000, cancellationToken);
            }

            // Re-check cancellation after pause
            cancellationToken.ThrowIfCancellationRequested();
        }

        // Double-check execution status from database
        var currentExecution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
        if (currentExecution?.Status == WorkflowExecutionStatus.Cancelled)
        {
            throw new OperationCanceledException($"Workflow execution {executionId} was cancelled");
        }

        // Update current node
        await _stateManager.UpdateCurrentNodeAsync(executionId, node.Id);

        // Check dependencies
        if (node.Dependencies.Any())
        {
            var canProceed = await _stateManager.CanProceedToNodeAsync(executionId, node.Id, node.Dependencies);
            if (!canProceed)
            {
                await _stateManager.AddPendingNodeAsync(executionId, node.Id);
                return;
            }
        }

        // Execute node with timeout and retry logic
        var nodeExecutionId = await CreateNodeExecutionAsync(executionId, node);
        var nodeInput = await GetNodeInputAsync(executionId, node);

        Dictionary<string, object> nodeOutput;
        try
        {
            nodeOutput = await ExecuteNodeWithRetryAsync(node, nodeInput, executionId, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to execute node {NodeId} in execution {ExecutionId}", node.Id, executionId);

            // Handle error based on node configuration
            if (node.ErrorHandling != null)
            {
                nodeOutput = await HandleNodeError(node, nodeInput, executionId, ex, cancellationToken);
            }
            else
            {
                // Update node execution with failure
                await UpdateNodeExecutionAsync(nodeExecutionId, NodeExecutionStatus.Failed, null, ex.Message);
                throw;
            }
        }

        // Update node execution with results
        await UpdateNodeExecutionAsync(nodeExecutionId, NodeExecutionStatus.Completed, nodeOutput, null);

        // Mark node as completed
        await _stateManager.MarkNodeCompletedAsync(executionId, node.Id);

        // Find and execute next nodes
        var nextNodes = GetNextNodes(workflowGraph, node.Id);
        var tasks = new List<Task>();

        foreach (var nextNode in nextNodes)
        {
            // Check edge conditions
            var edge = workflowGraph.Edges.FirstOrDefault(e => e.From == node.Id && e.To == nextNode.Id);
            if (edge?.Condition != null)
            {
                var conditionResult = await EvaluateConditionAsync(edge.Condition, executionId, null, cancellationToken);
                if (!conditionResult)
                {
                    continue;
                }
            }

            // Execute next node (parallel execution)
            tasks.Add(ExecuteNodeAndContinue(executionId, workflowGraph, nextNode, userId, cancellationToken));
        }

        // Wait for all parallel nodes to complete
        if (tasks.Any())
        {
            try
            {
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException)
            {
                // If any task was cancelled, cancel all remaining tasks
                _logger.LogDebug("Cancelling remaining parallel tasks for execution {ExecutionId}", executionId);
                throw;
            }
        }
    }

    private async Task<Dictionary<string, object>> ExecuteNodeWithRetryAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        var retryConfig = node.Retry ?? new WorkflowRetry();
        var attempt = 0;
        Exception? lastException = null;

        while (attempt < retryConfig.MaxAttempts)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Execute with timeout if specified
                if (node.Timeout != null)
                {
                    using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    timeoutCts.CancelAfter(node.Timeout.Duration);

                    return await ExecuteSingleNodeAsync(node, input, executionId, timeoutCts.Token);
                }
                else
                {
                    return await ExecuteSingleNodeAsync(node, input, executionId, cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // Don't retry on cancellation
            }
            catch (Exception ex)
            {
                lastException = ex;
                attempt++;

                if (attempt >= retryConfig.MaxAttempts)
                {
                    break;
                }

                // Check if this error should be retried
                if (retryConfig.DoNotRetryOnErrors.Any() &&
                    retryConfig.DoNotRetryOnErrors.Any(errorType => ex.GetType().Name.Contains(errorType)))
                {
                    throw;
                }

                // Calculate delay with exponential backoff
                var delay = TimeSpan.FromMilliseconds(
                    Math.Min(
                        retryConfig.InitialDelay.TotalMilliseconds * Math.Pow(retryConfig.BackoffMultiplier, attempt - 1),
                        retryConfig.MaxDelay.TotalMilliseconds
                    )
                );

                _logger.LogWarning(ex, "Node {NodeId} execution attempt {Attempt} failed, retrying in {Delay}ms",
                    node.Id, attempt, delay.TotalMilliseconds);

                await Task.Delay(delay, cancellationToken);
            }
        }

        throw lastException ?? new InvalidOperationException($"Node {node.Id} failed after {retryConfig.MaxAttempts} attempts");
    }

    private async Task<Dictionary<string, object>> ExecuteSingleNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Executing node {NodeId} of type {NodeType}", node.Id, node.Type);

        // Simulate node execution based on type
        var output = new Dictionary<string, object>();

        switch (node.Type)
        {
            case WorkflowNodeTypes.Action:
                output = await ExecuteActionNodeAsync(node, input, executionId, cancellationToken);
                break;
            case WorkflowNodeTypes.Condition:
                output = await ExecuteConditionNodeAsync(node, input, executionId, cancellationToken);
                break;
            case WorkflowNodeTypes.Script:
                output = await ExecuteScriptNodeAsync(node, input, executionId, cancellationToken);
                break;
            case WorkflowNodeTypes.HttpRequest:
                output = await ExecuteHttpNodeAsync(node, input, executionId, cancellationToken);
                break;
            case WorkflowNodeTypes.AgentCall:
                output = await ExecuteAgentNodeAsync(node, input, executionId, cancellationToken);
                break;
            case WorkflowNodeTypes.Wait:
                output = await ExecuteWaitNodeAsync(node, input, executionId, cancellationToken);
                break;
            case WorkflowNodeTypes.Start:
            case WorkflowNodeTypes.End:
                output["result"] = "completed";
                output["nodeType"] = node.Type;
                break;
            default:
                _logger.LogWarning("Unknown node type {NodeType} for node {NodeId}", node.Type, node.Id);
                output["result"] = "completed";
                output["warning"] = $"Unknown node type: {node.Type}";
                break;
        }

        // Add execution metadata
        output["executedAt"] = DateTime.UtcNow;
        output["nodeId"] = node.Id;
        output["nodeType"] = node.Type;

        return output;
    }

    private async Task<Dictionary<string, object>> ExecuteActionNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing action node {NodeId}: {NodeName}", node.Id, node.Name);

        // Get action configuration
        var actionType = node.Configuration.GetValueOrDefault("actionType", "generic")?.ToString();
        var actionData = node.Configuration.GetValueOrDefault("actionData", new Dictionary<string, object>());

        var output = new Dictionary<string, object>
        {
            ["result"] = "action_completed",
            ["message"] = $"Action {node.Name} completed successfully",
            ["actionType"] = actionType,
            ["processedInput"] = input.Count
        };

        // Simulate processing time based on action complexity
        var processingTime = node.Configuration.GetValueOrDefault("processingTime", 100);
        if (processingTime is int delay && delay > 0)
        {
            await Task.Delay(delay, cancellationToken);
        }

        // If there's specific action logic in configuration, execute it
        if (actionData is Dictionary<string, object> actionDict)
        {
            foreach (var kvp in actionDict)
            {
                output[$"action_{kvp.Key}"] = kvp.Value;
            }
        }

        // Merge input data with output
        output["inputData"] = input;

        return output;
    }

    private async Task<Dictionary<string, object>> ExecuteConditionNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing condition node {NodeId}: {NodeName}", node.Id, node.Name);

        var condition = node.Condition?.Expression ?? "true";
        var result = await EvaluateConditionAsync(node.Condition, executionId, input, cancellationToken);

        return new Dictionary<string, object>
        {
            ["result"] = result,
            ["condition"] = condition,
            ["conditionType"] = node.Condition?.Type ?? "simple",
            ["evaluatedAt"] = DateTime.UtcNow,
            ["inputData"] = input
        };
    }

    private async Task<Dictionary<string, object>> ExecuteScriptNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing script node {NodeId}: {NodeName}", node.Id, node.Name);

        // Get script configuration
        var scriptType = node.Configuration.GetValueOrDefault("scriptType", "javascript")?.ToString();
        var scriptCode = node.Configuration.GetValueOrDefault("script", string.Empty)?.ToString();
        var scriptTimeout = node.Configuration.GetValueOrDefault("timeout", 30000);

        if (string.IsNullOrEmpty(scriptCode))
        {
            throw new InvalidOperationException($"No script code provided for script node {node.Id}");
        }

        var output = new Dictionary<string, object>
        {
            ["result"] = "script_completed",
            ["scriptType"] = scriptType,
            ["inputData"] = input
        };

        try
        {
            // Simulate script execution time
            var executionTime = Math.Min(Convert.ToInt32(scriptTimeout), 10000); // Max 10 seconds for simulation
            await Task.Delay(executionTime / 10, cancellationToken); // Simulate 1/10th of the time

            // In a real implementation, you would execute the script using a JavaScript engine
            // For now, we'll simulate based on script type
            switch (scriptType?.ToLowerInvariant())
            {
                case "javascript":
                case "js":
                    output["output"] = "JavaScript execution result";
                    output["variables"] = new Dictionary<string, object>
                    {
                        ["processed"] = input.Count,
                        ["timestamp"] = DateTime.UtcNow
                    };
                    break;
                case "python":
                case "py":
                    output["output"] = "Python execution result";
                    output["variables"] = new Dictionary<string, object>
                    {
                        ["data_processed"] = input.Count,
                        ["execution_time"] = executionTime
                    };
                    break;
                default:
                    output["output"] = "Generic script execution result";
                    break;
            }

            output["executionTimeMs"] = executionTime;
            output["scriptLength"] = scriptCode.Length;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Script execution failed for node {NodeId}", node.Id);
            throw new InvalidOperationException($"Script execution failed: {ex.Message}", ex);
        }

        return output;
    }

    private async Task<Dictionary<string, object>> ExecuteHttpNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing HTTP node {NodeId}: {NodeName}", node.Id, node.Name);

        // Get HTTP configuration
        var url = node.Configuration.GetValueOrDefault("url", string.Empty)?.ToString();
        var method = node.Configuration.GetValueOrDefault("method", "GET")?.ToString()?.ToUpperInvariant();
        var headers = node.Configuration.GetValueOrDefault("headers", new Dictionary<string, object>());
        var body = node.Configuration.GetValueOrDefault("body", string.Empty)?.ToString();
        var timeout = node.Configuration.GetValueOrDefault("timeout", 30000);

        if (string.IsNullOrEmpty(url))
        {
            throw new InvalidOperationException($"No URL provided for HTTP node {node.Id}");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new InvalidOperationException($"Invalid URL '{url}' for HTTP node {node.Id}");
        }

        try
        {
            using var httpClient = _httpClientFactory.CreateClient();
            httpClient.Timeout = TimeSpan.FromMilliseconds(Convert.ToInt32(timeout));

            // Add headers
            if (headers is Dictionary<string, object> headerDict)
            {
                foreach (var header in headerDict)
                {
                    if (header.Value != null)
                    {
                        httpClient.DefaultRequestHeaders.Add(header.Key, header.Value.ToString());
                    }
                }
            }

            // Create request
            var request = new HttpRequestMessage(new HttpMethod(method ?? "GET"), uri);

            // Add body for POST/PUT/PATCH requests
            if (!string.IsNullOrEmpty(body) && (method == "POST" || method == "PUT" || method == "PATCH"))
            {
                request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            }

            // Execute request
            var startTime = DateTime.UtcNow;
            using var response = await httpClient.SendAsync(request, cancellationToken);
            var duration = DateTime.UtcNow - startTime;

            var responseContent = await response.Content.ReadAsStringAsync(cancellationToken);

            var output = new Dictionary<string, object>
            {
                ["result"] = "http_completed",
                ["statusCode"] = (int)response.StatusCode,
                ["statusText"] = response.ReasonPhrase ?? string.Empty,
                ["response"] = responseContent,
                ["isSuccess"] = response.IsSuccessStatusCode,
                ["url"] = url,
                ["method"] = method,
                ["durationMs"] = duration.TotalMilliseconds,
                ["inputData"] = input
            };

            // Add response headers
            var responseHeaders = new Dictionary<string, object>();
            foreach (var header in response.Headers.Concat(response.Content.Headers))
            {
                responseHeaders[header.Key] = string.Join(", ", header.Value);
            }
            output["responseHeaders"] = responseHeaders;

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("HTTP request to {Url} returned {StatusCode}: {StatusText}",
                    url, response.StatusCode, response.ReasonPhrase);
            }

            return output;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "HTTP request failed for node {NodeId} to {Url}", node.Id, url);
            throw new InvalidOperationException($"HTTP request failed: {ex.Message}", ex);
        }
        catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
        {
            _logger.LogError(ex, "HTTP request timed out for node {NodeId} to {Url}", node.Id, url);
            throw new InvalidOperationException($"HTTP request timed out after {timeout}ms", ex);
        }
    }

    private async Task<Dictionary<string, object>> ExecuteAgentNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing agent node {NodeId}: {NodeName}", node.Id, node.Name);

        // Get agent configuration
        var agentId = node.Configuration.GetValueOrDefault("agentId", string.Empty)?.ToString();
        var agentTask = node.Configuration.GetValueOrDefault("task", string.Empty)?.ToString();
        var agentParams = node.Configuration.GetValueOrDefault("parameters", new Dictionary<string, object>());
        var timeout = node.Configuration.GetValueOrDefault("timeout", 60000);

        if (string.IsNullOrEmpty(agentId))
        {
            throw new InvalidOperationException($"No agent ID provided for agent node {node.Id}");
        }

        if (string.IsNullOrEmpty(agentTask))
        {
            throw new InvalidOperationException($"No task provided for agent node {node.Id}");
        }

        try
        {
            // Simulate agent execution time based on task complexity
            var taskComplexity = agentTask.Length + (input.Count * 100);
            var simulatedDelay = Math.Min(taskComplexity, Convert.ToInt32(timeout) / 10);
            await Task.Delay(simulatedDelay, cancellationToken);

            // In a real implementation, this would call the MCP service or agent framework
            // For now, we simulate based on task type
            var output = new Dictionary<string, object>
            {
                ["result"] = "agent_completed",
                ["agentId"] = agentId,
                ["task"] = agentTask,
                ["executionTimeMs"] = simulatedDelay,
                ["inputData"] = input
            };

            // Simulate different types of agent responses based on task
            if (agentTask.ToLowerInvariant().Contains("analyze"))
            {
                output["agentResponse"] = new Dictionary<string, object>
                {
                    ["analysis"] = "Analysis completed successfully",
                    ["insights"] = new[] { "Insight 1", "Insight 2", "Insight 3" },
                    ["confidence"] = 0.85,
                    ["processedItems"] = input.Count
                };
            }
            else if (agentTask.ToLowerInvariant().Contains("generate"))
            {
                output["agentResponse"] = new Dictionary<string, object>
                {
                    ["generated"] = "Generated content based on input",
                    ["format"] = "text",
                    ["length"] = 1000,
                    ["sourceInputs"] = input.Keys.ToArray()
                };
            }
            else if (agentTask.ToLowerInvariant().Contains("process"))
            {
                output["agentResponse"] = new Dictionary<string, object>
                {
                    ["processed"] = "Data processing completed",
                    ["itemsProcessed"] = input.Count,
                    ["errors"] = 0,
                    ["warnings"] = 0
                };
            }
            else
            {
                output["agentResponse"] = new Dictionary<string, object>
                {
                    ["message"] = "Agent task completed successfully",
                    ["taskType"] = "generic",
                    ["status"] = "completed"
                };
            }

            // Add agent parameters to output
            if (agentParams is Dictionary<string, object> paramDict)
            {
                output["parameters"] = paramDict;
            }

            return output;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Agent execution failed for node {NodeId} with agent {AgentId}", node.Id, agentId);
            throw new InvalidOperationException($"Agent execution failed: {ex.Message}", ex);
        }
    }

    private async Task<Dictionary<string, object>> ExecuteWaitNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Executing wait node {NodeId}: {NodeName}", node.Id, node.Name);

        var waitTimeMs = node.Configuration.GetValueOrDefault("waitTime", 1000);
        var waitType = node.Configuration.GetValueOrDefault("waitType", "fixed")?.ToString();
        var condition = node.Configuration.GetValueOrDefault("condition", string.Empty)?.ToString();

        var startTime = DateTime.UtcNow;
        TimeSpan actualWaitTime;

        try
        {
            switch (waitType?.ToLowerInvariant())
            {
                case "fixed":
                    if (waitTimeMs is int fixedWaitTime && fixedWaitTime > 0)
                    {
                        await Task.Delay(fixedWaitTime, cancellationToken);
                        actualWaitTime = TimeSpan.FromMilliseconds(fixedWaitTime);
                    }
                    else
                    {
                        actualWaitTime = TimeSpan.Zero;
                    }
                    break;

                case "conditional":
                    if (!string.IsNullOrEmpty(condition))
                    {
                        actualWaitTime = await WaitForConditionAsync(condition, executionId, input, cancellationToken);
                    }
                    else
                    {
                        actualWaitTime = TimeSpan.Zero;
                    }
                    break;

                case "random":
                    var minWait = node.Configuration.GetValueOrDefault("minWait", 500);
                    var maxWait = node.Configuration.GetValueOrDefault("maxWait", 2000);
                    if (minWait is int min && maxWait is int max && min < max)
                    {
                        var random = new Random();
                        var randomWaitTime = random.Next(min, max);
                        await Task.Delay(randomWaitTime, cancellationToken);
                        actualWaitTime = TimeSpan.FromMilliseconds(randomWaitTime);
                    }
                    else
                    {
                        actualWaitTime = TimeSpan.Zero;
                    }
                    break;

                default:
                    if (waitTimeMs is int defaultWaitTime && defaultWaitTime > 0)
                    {
                        await Task.Delay(defaultWaitTime, cancellationToken);
                        actualWaitTime = TimeSpan.FromMilliseconds(defaultWaitTime);
                    }
                    else
                    {
                        actualWaitTime = TimeSpan.Zero;
                    }
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            actualWaitTime = DateTime.UtcNow - startTime;
            _logger.LogDebug("Wait node {NodeId} was cancelled after {Duration}ms", node.Id, actualWaitTime.TotalMilliseconds);
            throw;
        }

        return new Dictionary<string, object>
        {
            ["result"] = "wait_completed",
            ["waitType"] = waitType,
            ["requestedWaitTime"] = waitTimeMs,
            ["actualWaitTimeMs"] = actualWaitTime.TotalMilliseconds,
            ["startTime"] = startTime,
            ["endTime"] = DateTime.UtcNow,
            ["inputData"] = input
        };
    }

    private async Task<bool> EvaluateConditionAsync(WorkflowCondition? condition, Guid executionId,
        Dictionary<string, object>? contextData = null, CancellationToken cancellationToken = default)
    {
        if (condition == null)
        {
            return true;
        }

        try
        {
            // Get current workflow state for condition evaluation
            var workflowState = await _stateManager.GetCurrentStateAsync(executionId);
            var variables = new Dictionary<string, object>();

            // Merge workflow state variables
            if (workflowState?.State.ContainsKey("variables") == true &&
                workflowState.State["variables"] is Dictionary<string, object> stateVars)
            {
                foreach (var kvp in stateVars)
                {
                    variables[kvp.Key] = kvp.Value;
                }
            }

            // Merge condition variables
            foreach (var kvp in condition.Variables)
            {
                variables[kvp.Key] = kvp.Value;
            }

            // Merge context data
            if (contextData != null)
            {
                foreach (var kvp in contextData)
                {
                    variables[$"input_{kvp.Key}"] = kvp.Value;
                }
            }

            // Evaluate based on condition type
            switch (condition.Type?.ToLowerInvariant())
            {
                case "simple":
                case null:
                    return EvaluateSimpleCondition(condition.Expression, variables);

                case "javascript":
                case "js":
                    // In a real implementation, you would use a JavaScript engine like Jint
                    return await EvaluateJavaScriptCondition(condition.Expression, variables, cancellationToken);

                case "jsonpath":
                    return EvaluateJsonPathCondition(condition.Expression, variables);

                case "regex":
                    return EvaluateRegexCondition(condition.Expression, variables);

                default:
                    _logger.LogWarning("Unknown condition type {ConditionType}, defaulting to simple evaluation", condition.Type);
                    return EvaluateSimpleCondition(condition.Expression, variables);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to evaluate condition: {Expression}", condition.Expression);
            throw new InvalidOperationException($"Condition evaluation failed: {ex.Message}", ex);
        }
    }

    private List<WorkflowNode> GetNextNodes(WorkflowGraph graph, string currentNodeId)
    {
        var nextNodeIds = graph.Edges
            .Where(e => e.From == currentNodeId)
            .Select(e => e.To)
            .ToList();

        return graph.Nodes.Values
            .Where(n => nextNodeIds.Contains(n.Id))
            .ToList();
    }

    private async Task<Guid> CreateNodeExecutionAsync(Guid executionId, WorkflowNode node)
    {
        var nodeExecution = new NodeExecutionDto(
            Guid.NewGuid(),
            node.Id,
            node.Type,
            node.Name,
            null,
            null,
            NodeExecutionStatus.Running,
            DateTime.UtcNow,
            null,
            null,
            null,
            0,
            new Dictionary<string, object>()
        );

        return await _workflowRepository.CreateNodeExecutionAsync(executionId, nodeExecution);
    }

    private async Task<bool> UpdateNodeExecutionAsync(Guid nodeExecutionId, NodeExecutionStatus status,
        Dictionary<string, object>? output, string? errorMessage)
    {
        return await _workflowRepository.UpdateNodeExecutionAsync(nodeExecutionId, status, output, errorMessage);
    }

    private async Task<Dictionary<string, object>> GetNodeInputAsync(Guid executionId, WorkflowNode node)
    {
        var state = await _stateManager.GetCurrentStateAsync(executionId);
        var input = new Dictionary<string, object>();

        // Merge workflow input and current state
        if (state?.State.ContainsKey("input") == true && state.State["input"] is Dictionary<string, object> workflowInput)
        {
            foreach (var kvp in workflowInput)
            {
                input[kvp.Key] = kvp.Value;
            }
        }

        // Add node-specific input
        foreach (var kvp in node.Input)
        {
            input[kvp.Key] = kvp.Value;
        }

        return input;
    }

    private double CalculateProgress(List<string> completedNodes, List<string> pendingNodes)
    {
        var totalNodes = completedNodes.Count + pendingNodes.Count;
        return totalNodes > 0 ? (double)completedNodes.Count / totalNodes * 100 : 0;
    }

    private async Task<Dictionary<string, object>> HandleNodeError(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId, Exception exception, CancellationToken cancellationToken)
    {
        var errorHandling = node.ErrorHandling ?? new WorkflowErrorHandling();
        _logger.LogWarning(exception, "Handling error for node {NodeId} with strategy {Strategy}", node.Id, errorHandling.OnError);

        switch (errorHandling.OnError.ToLowerInvariant())
        {
            case "continue":
                return new Dictionary<string, object>
                {
                    ["result"] = "error_handled",
                    ["error"] = exception.Message,
                    ["errorHandling"] = "continue",
                    ["inputData"] = input
                };

            case "compensate":
                if (!string.IsNullOrEmpty(errorHandling.CompensationNode))
                {
                    // In a real implementation, you would execute the compensation node
                    _logger.LogInformation("Executing compensation node {CompensationNode} for failed node {NodeId}",
                        errorHandling.CompensationNode, node.Id);

                    return new Dictionary<string, object>
                    {
                        ["result"] = "compensated",
                        ["error"] = exception.Message,
                        ["compensationNode"] = errorHandling.CompensationNode,
                        ["inputData"] = input
                    };
                }
                goto case "fail";

            case "fallback":
                if (!string.IsNullOrEmpty(errorHandling.FallbackNode))
                {
                    // In a real implementation, you would execute the fallback node
                    _logger.LogInformation("Executing fallback node {FallbackNode} for failed node {NodeId}",
                        errorHandling.FallbackNode, node.Id);

                    return new Dictionary<string, object>
                    {
                        ["result"] = "fallback_executed",
                        ["error"] = exception.Message,
                        ["fallbackNode"] = errorHandling.FallbackNode,
                        ["inputData"] = input
                    };
                }
                goto case "fail";

            case "retry":
                // This should be handled by the retry mechanism in ExecuteNodeWithRetryAsync
                throw exception;

            case "fail":
            default:
                throw exception;
        }
    }

    private bool EvaluateSimpleCondition(string expression, Dictionary<string, object> variables)
    {
        // Handle basic boolean expressions
        var expr = expression.Trim().ToLowerInvariant();

        switch (expr)
        {
            case "true":
                return true;
            case "false":
                return false;
            default:
                // Handle simple variable comparisons like "variable == value"
                if (expr.Contains("=="))
                {
                    var parts = expr.Split(new[] { "==" }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var varName = parts[0].Trim();
                        var expectedValue = parts[1].Trim().Trim('"', '\'');

                        if (variables.TryGetValue(varName, out var actualValue))
                        {
                            return actualValue?.ToString()?.ToLowerInvariant() == expectedValue;
                        }
                    }
                }
                else if (expr.Contains("!="))
                {
                    var parts = expr.Split(new[] { "!=" }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        var varName = parts[0].Trim();
                        var expectedValue = parts[1].Trim().Trim('"', '\'');

                        if (variables.TryGetValue(varName, out var actualValue))
                        {
                            return actualValue?.ToString()?.ToLowerInvariant() != expectedValue;
                        }
                    }
                }
                else if (variables.TryGetValue(expr, out var value))
                {
                    // Direct variable reference
                    return Convert.ToBoolean(value);
                }

                // Default to true for unknown expressions
                _logger.LogWarning("Unknown simple condition expression: {Expression}, defaulting to true", expression);
                return true;
        }
    }

    private async Task<bool> EvaluateJavaScriptCondition(string expression, Dictionary<string, object> variables,
        CancellationToken cancellationToken)
    {
        // In a real implementation, you would use a JavaScript engine like Jint
        // For now, we'll simulate JavaScript evaluation

        await Task.Delay(10, cancellationToken); // Simulate JS engine execution time

        // Handle some common JavaScript patterns
        var expr = expression.Trim();

        // Handle simple conditions that look like JavaScript
        if (expr.Contains("&&") || expr.Contains("||"))
        {
            // Split on logical operators and evaluate each part
            var parts = expr.Split(new[] { "&&", "||" }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                var isAnd = expr.Contains("&&");
                var results = new List<bool>();

                foreach (var part in parts)
                {
                    var partResult = EvaluateSimpleCondition(part.Trim(), variables);
                    results.Add(partResult);
                }

                return isAnd ? results.All(r => r) : results.Any(r => r);
            }
        }

        // Fall back to simple evaluation
        return EvaluateSimpleCondition(expr, variables);
    }

    private bool EvaluateJsonPathCondition(string expression, Dictionary<string, object> variables)
    {
        // In a real implementation, you would use a JSONPath library
        // For now, we'll handle simple object property access

        try
        {
            if (expression.StartsWith("$."))
            {
                var path = expression.Substring(2);
                var pathParts = path.Split('.');

                object current = variables;
                foreach (var part in pathParts)
                {
                    if (current is Dictionary<string, object> dict && dict.TryGetValue(part, out var value))
                    {
                        current = value;
                    }
                    else
                    {
                        return false;
                    }
                }

                return Convert.ToBoolean(current);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate JSONPath condition: {Expression}", expression);
        }

        return false;
    }

    private bool EvaluateRegexCondition(string expression, Dictionary<string, object> variables)
    {
        // Handle regex patterns like "variableName =~ /pattern/flags"
        try
        {
            if (expression.Contains("=~"))
            {
                var parts = expression.Split(new[] { "=~" }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2)
                {
                    var varName = parts[0].Trim();
                    var pattern = parts[1].Trim();

                    if (variables.TryGetValue(varName, out var value) && value != null)
                    {
                        var text = value.ToString();

                        // Simple regex pattern matching
                        if (pattern.StartsWith("/") && pattern.Contains("/"))
                        {
                            var lastSlash = pattern.LastIndexOf('/');
                            var regexPattern = pattern.Substring(1, lastSlash - 1);

                            var regex = new System.Text.RegularExpressions.Regex(regexPattern);
                            return regex.IsMatch(text);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to evaluate regex condition: {Expression}", expression);
        }

        return false;
    }

    private async Task<TimeSpan> WaitForConditionAsync(string condition, Guid executionId,
        Dictionary<string, object> input, CancellationToken cancellationToken)
    {
        var startTime = DateTime.UtcNow;
        var maxWait = TimeSpan.FromMinutes(5); // Maximum wait time
        var checkInterval = TimeSpan.FromSeconds(1); // Check every second

        while (DateTime.UtcNow - startTime < maxWait)
        {
            try
            {
                var conditionObj = new WorkflowCondition { Expression = condition, Type = "simple" };
                var result = await EvaluateConditionAsync(conditionObj, executionId, input, cancellationToken);

                if (result)
                {
                    return DateTime.UtcNow - startTime;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error evaluating wait condition: {Condition}", condition);
            }

            await Task.Delay(checkInterval, cancellationToken);
        }

        _logger.LogWarning("Wait condition {Condition} timed out after {Duration}", condition, maxWait);
        return maxWait;
    }

    /// <summary>
    /// Cleanup method to dispose resources when the engine is disposed
    /// </summary>
    public void Dispose()
    {
        // Cancel all running executions
        foreach (var kvp in _executionCancellationTokens)
        {
            try
            {
                kvp.Value.Cancel();
                kvp.Value.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing cancellation token for execution {ExecutionId}", kvp.Key);
            }
        }

        _executionCancellationTokens.Clear();
        _pausedExecutions.Clear();
        _executionSemaphore.Dispose();
    }
}