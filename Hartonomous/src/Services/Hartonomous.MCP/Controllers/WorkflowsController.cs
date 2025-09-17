using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Hartonomous.MCP.Controllers;

/// <summary>
/// REST API controller for workflow management
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(IWorkflowRepository workflowRepository, ILogger<WorkflowsController> logger)
    {
        _workflowRepository = workflowRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all workflows for the authenticated user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<WorkflowDefinition>>> GetWorkflows()
    {
        try
        {
            var userId = GetUserId();
            var workflows = await _workflowRepository.GetWorkflowsByUserAsync(userId);
            return Ok(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflows for user {UserId}", GetUserId());
            return StatusCode(500, new { Message = "Failed to retrieve workflows", Error = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific workflow by ID
    /// </summary>
    [HttpGet("{workflowId:guid}")]
    public async Task<ActionResult<WorkflowDefinition>> GetWorkflow(Guid workflowId)
    {
        try
        {
            var userId = GetUserId();
            var workflow = await _workflowRepository.GetWorkflowAsync(workflowId, userId);

            if (workflow == null)
            {
                return NotFound(new { Message = "Workflow not found" });
            }

            return Ok(workflow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow {WorkflowId} for user {UserId}", workflowId, GetUserId());
            return StatusCode(500, new { Message = "Failed to retrieve workflow", Error = ex.Message });
        }
    }

    /// <summary>
    /// Create a new workflow
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<CreateWorkflowResponse>> CreateWorkflow([FromBody] WorkflowDefinition workflow)
    {
        try
        {
            var userId = GetUserId();
            var workflowId = await _workflowRepository.CreateWorkflowAsync(workflow, userId);

            var response = new CreateWorkflowResponse(workflowId);
            return CreatedAtAction(nameof(GetWorkflow), new { workflowId }, response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workflow for user {UserId}", GetUserId());
            return StatusCode(500, new { Message = "Failed to create workflow", Error = ex.Message });
        }
    }

    /// <summary>
    /// Start workflow execution
    /// </summary>
    [HttpPost("{workflowId:guid}/execute")]
    public async Task<ActionResult<StartWorkflowExecutionResponse>> StartExecution(
        Guid workflowId,
        [FromBody] StartWorkflowExecutionRequest request)
    {
        try
        {
            var userId = GetUserId();
            var executionId = await _workflowRepository.StartWorkflowExecutionAsync(
                workflowId, request.ProjectId, userId, request.Parameters);

            var response = new StartWorkflowExecutionResponse(executionId);
            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow execution for workflow {WorkflowId} for user {UserId}",
                workflowId, GetUserId());
            return StatusCode(500, new { Message = "Failed to start workflow execution", Error = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow execution by ID
    /// </summary>
    [HttpGet("executions/{executionId:guid}")]
    public async Task<ActionResult<WorkflowExecution>> GetExecution(Guid executionId)
    {
        try
        {
            var userId = GetUserId();
            var execution = await _workflowRepository.GetWorkflowExecutionAsync(executionId, userId);

            if (execution == null)
            {
                return NotFound(new { Message = "Workflow execution not found" });
            }

            return Ok(execution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow execution {ExecutionId} for user {UserId}",
                executionId, GetUserId());
            return StatusCode(500, new { Message = "Failed to retrieve workflow execution", Error = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow executions by project
    /// </summary>
    [HttpGet("projects/{projectId:guid}/executions")]
    public async Task<ActionResult<IEnumerable<WorkflowExecution>>> GetExecutionsByProject(Guid projectId)
    {
        try
        {
            var userId = GetUserId();
            var executions = await _workflowRepository.GetWorkflowExecutionsByProjectAsync(projectId, userId);
            return Ok(executions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow executions for project {ProjectId} for user {UserId}",
                projectId, GetUserId());
            return StatusCode(500, new { Message = "Failed to retrieve workflow executions", Error = ex.Message });
        }
    }

    /// <summary>
    /// Update workflow execution status
    /// </summary>
    [HttpPut("executions/{executionId:guid}/status")]
    public async Task<ActionResult> UpdateExecutionStatus(
        Guid executionId,
        [FromBody] UpdateExecutionStatusRequest request)
    {
        try
        {
            var userId = GetUserId();
            var success = await _workflowRepository.UpdateWorkflowExecutionStatusAsync(
                executionId, request.Status, userId, request.ErrorMessage);

            if (!success)
            {
                return NotFound(new { Message = "Workflow execution not found or access denied" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update workflow execution status for execution {ExecutionId} for user {UserId}",
                executionId, GetUserId());
            return StatusCode(500, new { Message = "Failed to update execution status", Error = ex.Message });
        }
    }

    /// <summary>
    /// Delete workflow
    /// </summary>
    [HttpDelete("{workflowId:guid}")]
    public async Task<ActionResult> DeleteWorkflow(Guid workflowId)
    {
        try
        {
            var userId = GetUserId();
            var success = await _workflowRepository.DeleteWorkflowAsync(workflowId, userId);

            if (!success)
            {
                return NotFound(new { Message = "Workflow not found or access denied" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete workflow {WorkflowId} for user {UserId}", workflowId, GetUserId());
            return StatusCode(500, new { Message = "Failed to delete workflow", Error = ex.Message });
        }
    }

    private string GetUserId()
    {
        return User.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               User.FindFirst("sub")?.Value ??
               throw new UnauthorizedAccessException("User ID not found in claims");
    }
}

/// <summary>
/// Response model for workflow creation
/// </summary>
public record CreateWorkflowResponse(Guid WorkflowId);

/// <summary>
/// Request model for starting workflow execution
/// </summary>
public record StartWorkflowExecutionRequest(
    Guid ProjectId,
    Dictionary<string, object>? Parameters = null
);

/// <summary>
/// Response model for starting workflow execution
/// </summary>
public record StartWorkflowExecutionResponse(Guid ExecutionId);

/// <summary>
/// Request model for updating execution status
/// </summary>
public record UpdateExecutionStatusRequest(
    WorkflowExecutionStatus Status,
    string? ErrorMessage = null
);