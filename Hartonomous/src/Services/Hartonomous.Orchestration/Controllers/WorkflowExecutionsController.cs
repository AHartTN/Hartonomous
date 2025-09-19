/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow execution engine API controller,
 * managing workflow runtime lifecycle, state management, and execution control operations.
 */

using Hartonomous.Infrastructure.Security;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.Orchestration.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkflowExecutionsController : ControllerBase
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowExecutionEngine _executionEngine;
    private readonly IWorkflowStateManager _stateManager;
    private readonly ILogger<WorkflowExecutionsController> _logger;

    public WorkflowExecutionsController(
        IWorkflowRepository workflowRepository,
        IWorkflowExecutionEngine executionEngine,
        IWorkflowStateManager stateManager,
        ILogger<WorkflowExecutionsController> logger)
    {
        _workflowRepository = workflowRepository;
        _executionEngine = executionEngine;
        _stateManager = stateManager;
        _logger = logger;
    }

    /// <summary>
    /// Start a new workflow execution
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> StartExecution([FromBody] StartWorkflowExecutionRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var executionId = await _executionEngine.StartWorkflowAsync(
                request.WorkflowId,
                request.Input,
                request.Configuration,
                userId,
                request.ExecutionName
            );

            _logger.LogInformation("Started workflow execution {ExecutionId} for user {UserId}", executionId, userId);

            return CreatedAtAction(nameof(GetExecution), new { id = executionId }, executionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start workflow execution");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow execution by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowExecutionDto>> GetExecution(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var execution = await _workflowRepository.GetExecutionByIdAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            return Ok(execution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution {ExecutionId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow execution progress with real-time status
    /// </summary>
    [HttpGet("{id}/progress")]
    public async Task<ActionResult<WorkflowExecutionDto>> GetExecutionProgress(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var execution = await _executionEngine.GetExecutionProgressAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            return Ok(execution);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution progress for {ExecutionId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Control workflow execution (pause, resume, cancel)
    /// </summary>
    [HttpPost("{id}/control")]
    public async Task<ActionResult> ControlExecution(Guid id, [FromBody] WorkflowControlRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            if (request.ExecutionId != id)
            {
                return BadRequest("Execution ID mismatch");
            }

            bool success = request.Action switch
            {
                WorkflowControlAction.Pause => await _executionEngine.PauseWorkflowAsync(id, userId),
                WorkflowControlAction.Resume => await _executionEngine.ResumeWorkflowAsync(id, userId),
                WorkflowControlAction.Cancel => await _executionEngine.CancelWorkflowAsync(id, userId),
                WorkflowControlAction.Retry => await _executionEngine.RetryWorkflowAsync(id, userId),
                WorkflowControlAction.Stop => await _executionEngine.CancelWorkflowAsync(id, userId),
                _ => throw new ArgumentException($"Unsupported action: {request.Action}")
            };

            if (!success)
            {
                return BadRequest($"Failed to {request.Action.ToString().ToLowerInvariant()} execution");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to control execution {ExecutionId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get executions for a specific workflow
    /// </summary>
    [HttpGet("workflow/{workflowId}")]
    public async Task<ActionResult<List<WorkflowExecutionDto>>> GetExecutionsByWorkflow(
        Guid workflowId,
        [FromQuery] int limit = 100)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var executions = await _workflowRepository.GetExecutionsByWorkflowAsync(workflowId, userId, limit);
            return Ok(executions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get executions for workflow {WorkflowId}", workflowId);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all active executions for the current user
    /// </summary>
    [HttpGet("active")]
    public async Task<ActionResult<List<WorkflowExecutionDto>>> GetActiveExecutions()
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var executions = await _workflowRepository.GetActiveExecutionsAsync(userId);
            return Ok(executions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get active executions");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow execution state
    /// </summary>
    [HttpGet("{id}/state")]
    public async Task<ActionResult<WorkflowStateDto>> GetExecutionState(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            var state = await _stateManager.GetCurrentStateAsync(id);
            if (state == null)
            {
                return NotFound($"State for execution {id} not found");
            }

            return Ok(state);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution state for {ExecutionId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow execution state history
    /// </summary>
    [HttpGet("{id}/state/history")]
    public async Task<ActionResult<List<WorkflowStateDto>>> GetExecutionStateHistory(
        Guid id,
        [FromQuery] int limit = 10)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            var stateHistory = await _stateManager.GetStateHistoryAsync(id, limit);
            return Ok(stateHistory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get execution state history for {ExecutionId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get node executions for a workflow execution
    /// </summary>
    [HttpGet("{id}/nodes")]
    public async Task<ActionResult<List<NodeExecutionDto>>> GetNodeExecutions(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            var nodeExecutions = await _workflowRepository.GetNodeExecutionsByExecutionAsync(id);
            return Ok(nodeExecutions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get node executions for {ExecutionId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Set variable in workflow execution state
    /// </summary>
    [HttpPost("{id}/variables/{key}")]
    public async Task<ActionResult> SetVariable(Guid id, string key, [FromBody] object value)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            var success = await _stateManager.SetVariableAsync(id, key, value);
            if (!success)
            {
                return BadRequest("Failed to set variable");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to set variable {Key} for execution {ExecutionId}", key, id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get variable from workflow execution state
    /// </summary>
    [HttpGet("{id}/variables/{key}")]
    public async Task<ActionResult<object>> GetVariable(Guid id, string key)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            var value = await _stateManager.GetVariableAsync<object>(id, key);
            if (value == null)
            {
                return NotFound($"Variable {key} not found");
            }

            return Ok(value);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get variable {Key} for execution {ExecutionId}", key, id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Create snapshot of execution state
    /// </summary>
    [HttpPost("{id}/snapshot")]
    public async Task<ActionResult> CreateSnapshot(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Verify execution belongs to user
            var execution = await _workflowRepository.GetExecutionByIdAsync(id, userId);
            if (execution == null)
            {
                return NotFound($"Execution {id} not found");
            }

            var success = await _stateManager.CreateSnapshotAsync(id);
            if (!success)
            {
                return BadRequest("Failed to create snapshot");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create snapshot for execution {ExecutionId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }
}