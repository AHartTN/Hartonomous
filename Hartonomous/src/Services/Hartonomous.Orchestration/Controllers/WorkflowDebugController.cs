using Hartonomous.Infrastructure.Security;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.Orchestration.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkflowDebugController : ControllerBase
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowStateManager _stateManager;
    private readonly ILogger<WorkflowDebugController> _logger;

    public WorkflowDebugController(
        IWorkflowRepository workflowRepository,
        IWorkflowStateManager stateManager,
        ILogger<WorkflowDebugController> logger)
    {
        _workflowRepository = workflowRepository;
        _stateManager = stateManager;
        _logger = logger;
    }

    /// <summary>
    /// Get debug information for workflow execution
    /// </summary>
    [HttpGet("executions/{executionId}")]
    public async Task<ActionResult<WorkflowDebugDto>> GetExecutionDebugInfo(Guid executionId)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                return NotFound($"Execution {executionId} not found");
            }

            // Get debug events
            var events = await _workflowRepository.GetWorkflowEventsAsync(executionId);

            // Get current state variables
            var state = await _stateManager.GetCurrentStateAsync(executionId);
            var variables = state?.State ?? new Dictionary<string, object>();

            // Get breakpoints
            var breakpoints = await _workflowRepository.GetBreakpointsByExecutionAsync(executionId, userId);

            var debugInfo = new WorkflowDebugDto(
                executionId,
                events,
                variables,
                breakpoints,
                null // Current breakpoint would be determined by execution engine
            );

            return Ok(debugInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get debug info for execution {ExecutionId}", executionId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow execution events
    /// </summary>
    [HttpGet("executions/{executionId}/events")]
    public async Task<ActionResult<List<DebugEvent>>> GetExecutionEvents(
        Guid executionId,
        [FromQuery] DateTime? since = null,
        [FromQuery] int limit = 100)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                return NotFound($"Execution {executionId} not found");
            }

            var events = await _workflowRepository.GetWorkflowEventsAsync(executionId, since);

            // Apply limit
            if (limit > 0)
            {
                events = events.Take(limit).ToList();
            }

            return Ok(events);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get events for execution {ExecutionId}", executionId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Create breakpoint for workflow execution
    /// </summary>
    [HttpPost("executions/{executionId}/breakpoints")]
    public async Task<ActionResult<Guid>> CreateBreakpoint(
        Guid executionId,
        [FromBody] CreateBreakpointRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                return NotFound($"Execution {executionId} not found");
            }

            var breakpoint = new BreakpointDto(
                Guid.NewGuid(),
                request.NodeId,
                request.Condition,
                true,
                DateTime.UtcNow
            );

            var success = await _workflowRepository.CreateBreakpointAsync(executionId, breakpoint, userId);
            if (!success)
            {
                return BadRequest("Failed to create breakpoint");
            }

            _logger.LogInformation("Created breakpoint {BreakpointId} for execution {ExecutionId}",
                breakpoint.BreakpointId, executionId);

            return CreatedAtAction(nameof(GetBreakpoint),
                new { executionId, breakpointId = breakpoint.BreakpointId },
                breakpoint.BreakpointId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create breakpoint for execution {ExecutionId}", executionId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get breakpoint by ID
    /// </summary>
    [HttpGet("executions/{executionId}/breakpoints/{breakpointId}")]
    public async Task<ActionResult<BreakpointDto>> GetBreakpoint(Guid executionId, Guid breakpointId)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var breakpoints = await _workflowRepository.GetBreakpointsByExecutionAsync(executionId, userId);
            var breakpoint = breakpoints.FirstOrDefault(b => b.BreakpointId == breakpointId);

            if (breakpoint == null)
            {
                return NotFound($"Breakpoint {breakpointId} not found");
            }

            return Ok(breakpoint);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get breakpoint {BreakpointId}", breakpointId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all breakpoints for execution
    /// </summary>
    [HttpGet("executions/{executionId}/breakpoints")]
    public async Task<ActionResult<List<BreakpointDto>>> GetExecutionBreakpoints(Guid executionId)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                return NotFound($"Execution {executionId} not found");
            }

            var breakpoints = await _workflowRepository.GetBreakpointsByExecutionAsync(executionId, userId);
            return Ok(breakpoints);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get breakpoints for execution {ExecutionId}", executionId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Remove breakpoint
    /// </summary>
    [HttpDelete("executions/{executionId}/breakpoints/{breakpointId}")]
    public async Task<ActionResult> RemoveBreakpoint(Guid executionId, Guid breakpointId)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var success = await _workflowRepository.RemoveBreakpointAsync(breakpointId, userId);
            if (!success)
            {
                return NotFound($"Breakpoint {breakpointId} not found");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to remove breakpoint {BreakpointId}", breakpointId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get execution metrics for monitoring
    /// </summary>
    [HttpGet("executions/{executionId}/metrics")]
    public async Task<ActionResult<Dictionary<string, object>>> GetExecutionMetrics(Guid executionId)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(executionId, userId);
            if (execution == null)
            {
                return NotFound($"Execution {executionId} not found");
            }

            // Calculate execution metrics
            var nodeExecutions = await _workflowRepository.GetNodeExecutionsByExecutionAsync(executionId);
            var state = await _stateManager.GetCurrentStateAsync(executionId);

            var metrics = new Dictionary<string, object>
            {
                ["executionId"] = executionId,
                ["status"] = execution.Status.ToString(),
                ["startedAt"] = execution.StartedAt,
                ["duration"] = execution.Duration?.TotalSeconds ?? 0,
                ["nodeCount"] = nodeExecutions.Count,
                ["completedNodes"] = nodeExecutions.Count(n => n.Status == NodeExecutionStatus.Completed),
                ["failedNodes"] = nodeExecutions.Count(n => n.Status == NodeExecutionStatus.Failed),
                ["runningNodes"] = nodeExecutions.Count(n => n.Status == NodeExecutionStatus.Running),
                ["pendingNodes"] = state?.PendingNodes.Count ?? 0,
                ["progress"] = CalculateProgress(nodeExecutions),
                ["averageNodeDuration"] = CalculateAverageNodeDuration(nodeExecutions),
                ["errorRate"] = CalculateErrorRate(nodeExecutions)
            };

            return Ok(metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get metrics for execution {ExecutionId}", executionId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow health status
    /// </summary>
    [HttpGet("workflows/{workflowId}/health")]
    public async Task<ActionResult<Dictionary<string, object>>> GetWorkflowHealth(Guid workflowId)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify workflow belongs to user
            var workflow = await _workflowRepository.GetWorkflowByIdAsync(workflowId, userId);
            if (workflow == null)
            {
                return NotFound($"Workflow {workflowId} not found");
            }

            // Get recent executions for health analysis
            var recentExecutions = await _workflowRepository.GetExecutionsByWorkflowAsync(workflowId, userId, 50);

            var health = new Dictionary<string, object>
            {
                ["workflowId"] = workflowId,
                ["status"] = workflow.Status.ToString(),
                ["totalExecutions"] = recentExecutions.Count,
                ["successRate"] = CalculateSuccessRate(recentExecutions),
                ["averageExecutionTime"] = CalculateAverageExecutionTime(recentExecutions),
                ["lastExecution"] = recentExecutions.FirstOrDefault()?.StartedAt,
                ["activeExecutions"] = recentExecutions.Count(e =>
                    e.Status == WorkflowExecutionStatus.Running ||
                    e.Status == WorkflowExecutionStatus.Pending ||
                    e.Status == WorkflowExecutionStatus.Paused),
                ["healthScore"] = CalculateHealthScore(recentExecutions),
                ["lastErrors"] = GetLastErrors(recentExecutions, 5)
            };

            return Ok(health);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get health for workflow {WorkflowId}", workflowId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get system-wide monitoring dashboard data
    /// </summary>
    [HttpGet("dashboard")]
    public async Task<ActionResult<Dictionary<string, object>>> GetMonitoringDashboard()
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Get active executions
            var activeExecutions = await _workflowRepository.GetActiveExecutionsAsync(userId);

            // Get user workflows
            var userWorkflows = await _workflowRepository.GetWorkflowsByUserAsync(userId, 100);

            var dashboard = new Dictionary<string, object>
            {
                ["totalWorkflows"] = userWorkflows.Count,
                ["activeWorkflows"] = userWorkflows.Count(w => w.Status == WorkflowStatus.Active),
                ["totalActiveExecutions"] = activeExecutions.Count,
                ["runningExecutions"] = activeExecutions.Count(e => e.Status == WorkflowExecutionStatus.Running),
                ["pausedExecutions"] = activeExecutions.Count(e => e.Status == WorkflowExecutionStatus.Paused),
                ["pendingExecutions"] = activeExecutions.Count(e => e.Status == WorkflowExecutionStatus.Pending),
                ["workflowsByCategory"] = userWorkflows
                    .GroupBy(w => w.Category ?? "Uncategorized")
                    .ToDictionary(g => g.Key, g => g.Count()),
                ["recentExecutions"] = activeExecutions
                    .OrderByDescending(e => e.StartedAt)
                    .Take(10)
                    .Select(e => new
                    {
                        e.ExecutionId,
                        e.WorkflowName,
                        e.Status,
                        e.StartedAt,
                        e.Duration
                    }),
                ["systemHealth"] = CalculateSystemHealth(userWorkflows, activeExecutions)
            };

            return Ok(dashboard);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get monitoring dashboard");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    private double CalculateProgress(List<NodeExecutionDto> nodeExecutions)
    {
        if (!nodeExecutions.Any()) return 0;

        var completed = nodeExecutions.Count(n =>
            n.Status == NodeExecutionStatus.Completed ||
            n.Status == NodeExecutionStatus.Skipped);

        return (double)completed / nodeExecutions.Count * 100;
    }

    private double CalculateAverageNodeDuration(List<NodeExecutionDto> nodeExecutions)
    {
        var completedNodes = nodeExecutions
            .Where(n => n.Duration.HasValue)
            .ToList();

        return completedNodes.Any()
            ? completedNodes.Average(n => n.Duration!.Value.TotalSeconds)
            : 0;
    }

    private double CalculateErrorRate(List<NodeExecutionDto> nodeExecutions)
    {
        if (!nodeExecutions.Any()) return 0;

        var failed = nodeExecutions.Count(n => n.Status == NodeExecutionStatus.Failed);
        return (double)failed / nodeExecutions.Count * 100;
    }

    private double CalculateSuccessRate(List<WorkflowExecutionDto> executions)
    {
        if (!executions.Any()) return 0;

        var successful = executions.Count(e => e.Status == WorkflowExecutionStatus.Completed);
        return (double)successful / executions.Count * 100;
    }

    private double CalculateAverageExecutionTime(List<WorkflowExecutionDto> executions)
    {
        var completedExecutions = executions
            .Where(e => e.Duration.HasValue)
            .ToList();

        return completedExecutions.Any()
            ? completedExecutions.Average(e => e.Duration!.Value.TotalSeconds)
            : 0;
    }

    private double CalculateHealthScore(List<WorkflowExecutionDto> executions)
    {
        if (!executions.Any()) return 100;

        var successRate = CalculateSuccessRate(executions);
        var recentFailures = executions
            .Where(e => e.StartedAt > DateTime.UtcNow.AddHours(-24) &&
                       e.Status == WorkflowExecutionStatus.Failed)
            .Count();

        // Simple health score calculation
        var healthScore = successRate;
        if (recentFailures > 5) healthScore -= 20;
        if (recentFailures > 10) healthScore -= 30;

        return Math.Max(0, Math.Min(100, healthScore));
    }

    private List<object> GetLastErrors(List<WorkflowExecutionDto> executions, int limit)
    {
        return executions
            .Where(e => e.Status == WorkflowExecutionStatus.Failed && !string.IsNullOrEmpty(e.ErrorMessage))
            .OrderByDescending(e => e.StartedAt)
            .Take(limit)
            .Select(e => new
            {
                e.ExecutionId,
                e.WorkflowName,
                e.ErrorMessage,
                e.StartedAt
            })
            .Cast<object>()
            .ToList();
    }

    private Dictionary<string, object> CalculateSystemHealth(
        List<WorkflowDefinitionDto> workflows,
        List<WorkflowExecutionDto> activeExecutions)
    {
        return new Dictionary<string, object>
        {
            ["status"] = "healthy", // Could be "healthy", "warning", "critical"
            ["workflowHealth"] = workflows.Count(w => w.Status == WorkflowStatus.Active) / (double)workflows.Count * 100,
            ["executionHealth"] = activeExecutions.Any() ? 100 : 0, // Simplified
            ["systemLoad"] = activeExecutions.Count / 100.0 * 100, // Simplified load calculation
            ["lastUpdated"] = DateTime.UtcNow
        };
    }
}

/// <summary>
/// Request DTO for creating breakpoints
/// </summary>
public record CreateBreakpointRequest(
    string NodeId,
    string? Condition = null
);