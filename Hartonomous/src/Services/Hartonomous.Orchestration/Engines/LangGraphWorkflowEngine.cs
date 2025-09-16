using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.DSL;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Hartonomous.Orchestration.Engines;

/// <summary>
/// LangGraph-powered workflow execution engine
/// Note: This implementation uses LangGraph-style patterns and state machine concepts.
/// When the official LangGraph .NET package becomes available, this can be updated to use it directly.
/// </summary>
public class LangGraphWorkflowEngine : IWorkflowExecutionEngine
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowStateManager _stateManager;
    private readonly IWorkflowDSLParser _dslParser;
    private readonly ILogger<LangGraphWorkflowEngine> _logger;
    private readonly IServiceProvider _serviceProvider;

    public LangGraphWorkflowEngine(
        IWorkflowRepository workflowRepository,
        IWorkflowStateManager stateManager,
        IWorkflowDSLParser dslParser,
        ILogger<LangGraphWorkflowEngine> logger,
        IServiceProvider serviceProvider)
    {
        _workflowRepository = workflowRepository;
        _stateManager = stateManager;
        _dslParser = dslParser;
        _logger = logger;
        _serviceProvider = serviceProvider;
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

            // Start workflow execution in background
            _ = Task.Run(async () => await ExecuteWorkflowAsync(executionId, workflowGraph, userId));

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
            if (execution == null || execution.Status != WorkflowExecutionStatus.Paused)
            {
                return false;
            }

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
                    _ = Task.Run(async () => await ResumeWorkflowExecutionAsync(executionId, workflowGraph, state.CurrentNode, userId));
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

            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Paused, null, userId);
            await _stateManager.UpdateStateAsync(executionId, new Dictionary<string, object>
            {
                ["status"] = WorkflowExecutionStatus.Paused,
                ["pausedAt"] = DateTime.UtcNow
            });

            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pause workflow execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> CancelWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            _logger.LogInformation("Cancelling workflow execution {ExecutionId}", executionId);

            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Cancelled, "Cancelled by user", userId);
            await _stateManager.UpdateStateAsync(executionId, new Dictionary<string, object>
            {
                ["status"] = WorkflowExecutionStatus.Cancelled,
                ["cancelledAt"] = DateTime.UtcNow
            });

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
                return false;
            }

            // Reset execution status
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Running, null, userId);

            // Reset workflow state
            var initialState = new Dictionary<string, object>
            {
                ["input"] = execution.Input ?? new Dictionary<string, object>(),
                ["status"] = WorkflowExecutionStatus.Running,
                ["retryStartedAt"] = DateTime.UtcNow
            };

            await _stateManager.InitializeStateAsync(executionId, initialState);

            // Get workflow definition and restart
            var workflow = await _workflowRepository.GetWorkflowByIdAsync(execution.WorkflowId, userId);
            if (workflow != null)
            {
                var workflowGraph = await _dslParser.ParseWorkflowAsync(workflow.WorkflowDefinition);
                _ = Task.Run(async () => await ExecuteWorkflowAsync(executionId, workflowGraph, userId));
            }

            return true;
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

    private async Task ExecuteWorkflowAsync(Guid executionId, WorkflowGraph workflowGraph, string userId)
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
            await ExecuteNodeAndContinue(executionId, workflowGraph, startNode, userId);

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
        string currentNodeId, string userId)
    {
        try
        {
            var currentNode = workflowGraph.Nodes[currentNodeId];
            await ExecuteNodeAndContinue(executionId, workflowGraph, currentNode, userId);
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Completed, null, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to resume workflow execution {ExecutionId}", executionId);
            await _workflowRepository.UpdateExecutionStatusAsync(executionId, WorkflowExecutionStatus.Failed, ex.Message, userId);
        }
    }

    private async Task ExecuteNodeAndContinue(Guid executionId, WorkflowGraph workflowGraph,
        WorkflowNode node, string userId)
    {
        // Check if execution should be paused or cancelled
        var currentExecution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
        if (currentExecution?.Status == WorkflowExecutionStatus.Paused ||
            currentExecution?.Status == WorkflowExecutionStatus.Cancelled)
        {
            return;
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

        // Execute node
        var nodeExecutionId = await CreateNodeExecutionAsync(executionId, node);
        var nodeInput = await GetNodeInputAsync(executionId, node);
        var nodeOutput = await ExecuteSingleNodeAsync(node, nodeInput, executionId);

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
                var conditionResult = await EvaluateConditionAsync(edge.Condition, executionId);
                if (!conditionResult)
                {
                    continue;
                }
            }

            // Execute next node (parallel execution)
            tasks.Add(ExecuteNodeAndContinue(executionId, workflowGraph, nextNode, userId));
        }

        // Wait for all parallel nodes to complete
        if (tasks.Any())
        {
            await Task.WhenAll(tasks);
        }
    }

    private async Task<Dictionary<string, object>> ExecuteSingleNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId)
    {
        _logger.LogInformation("Executing node {NodeId} of type {NodeType}", node.Id, node.Type);

        // Simulate node execution based on type
        var output = new Dictionary<string, object>();

        switch (node.Type)
        {
            case WorkflowNodeTypes.Action:
                output = await ExecuteActionNodeAsync(node, input, executionId);
                break;
            case WorkflowNodeTypes.Condition:
                output = await ExecuteConditionNodeAsync(node, input, executionId);
                break;
            case WorkflowNodeTypes.Script:
                output = await ExecuteScriptNodeAsync(node, input, executionId);
                break;
            case WorkflowNodeTypes.HttpRequest:
                output = await ExecuteHttpNodeAsync(node, input, executionId);
                break;
            case WorkflowNodeTypes.AgentCall:
                output = await ExecuteAgentNodeAsync(node, input, executionId);
                break;
            case WorkflowNodeTypes.Wait:
                output = await ExecuteWaitNodeAsync(node, input, executionId);
                break;
            default:
                output["result"] = "completed";
                break;
        }

        // Add execution metadata
        output["executedAt"] = DateTime.UtcNow;
        output["nodeId"] = node.Id;
        output["nodeType"] = node.Type;

        return output;
    }

    private async Task<Dictionary<string, object>> ExecuteActionNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId)
    {
        // Simulate action execution
        await Task.Delay(100); // Simulate work
        return new Dictionary<string, object>
        {
            ["result"] = "action_completed",
            ["message"] = $"Action {node.Name} completed successfully"
        };
    }

    private async Task<Dictionary<string, object>> ExecuteConditionNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId)
    {
        var condition = node.Condition?.Expression ?? "true";
        var result = await EvaluateConditionAsync(node.Condition, executionId);

        return new Dictionary<string, object>
        {
            ["result"] = result,
            ["condition"] = condition
        };
    }

    private async Task<Dictionary<string, object>> ExecuteScriptNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId)
    {
        // Simulate script execution
        await Task.Delay(200);
        return new Dictionary<string, object>
        {
            ["result"] = "script_completed",
            ["output"] = "Script execution result"
        };
    }

    private async Task<Dictionary<string, object>> ExecuteHttpNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId)
    {
        // Simulate HTTP request
        await Task.Delay(300);
        return new Dictionary<string, object>
        {
            ["result"] = "http_completed",
            ["statusCode"] = 200,
            ["response"] = "HTTP request completed"
        };
    }

    private async Task<Dictionary<string, object>> ExecuteAgentNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId)
    {
        // This would integrate with the MCP service to call agents
        await Task.Delay(500);
        return new Dictionary<string, object>
        {
            ["result"] = "agent_completed",
            ["agentResponse"] = "Agent task completed"
        };
    }

    private async Task<Dictionary<string, object>> ExecuteWaitNodeAsync(WorkflowNode node,
        Dictionary<string, object> input, Guid executionId)
    {
        var waitTimeMs = node.Configuration.GetValueOrDefault("waitTime", 1000);
        if (waitTimeMs is int waitTime)
        {
            await Task.Delay(waitTime);
        }

        return new Dictionary<string, object>
        {
            ["result"] = "wait_completed",
            ["waitTime"] = waitTimeMs
        };
    }

    private async Task<bool> EvaluateConditionAsync(WorkflowCondition? condition, Guid executionId)
    {
        if (condition == null)
        {
            return true;
        }

        // Simple condition evaluation (in production, you'd use a proper expression evaluator)
        if (condition.Expression.Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (condition.Expression.Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // For complex conditions, you would integrate with a JavaScript engine or expression evaluator
        return await Task.FromResult(true); // Default to true for now
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
}