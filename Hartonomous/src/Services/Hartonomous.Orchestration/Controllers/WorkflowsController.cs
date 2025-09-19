/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the workflow orchestration API controller,
 * providing DSL-based workflow definition, validation, and management capabilities.
 */

using Hartonomous.Infrastructure.Security;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Hartonomous.Orchestration.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkflowsController : ControllerBase
{
    private readonly IWorkflowRepository _workflowRepository;
    private readonly IWorkflowExecutionEngine _executionEngine;
    private readonly IWorkflowDSLParser _dslParser;
    private readonly ILogger<WorkflowsController> _logger;

    public WorkflowsController(
        IWorkflowRepository workflowRepository,
        IWorkflowExecutionEngine executionEngine,
        IWorkflowDSLParser dslParser,
        ILogger<WorkflowsController> logger)
    {
        _workflowRepository = workflowRepository;
        _executionEngine = executionEngine;
        _dslParser = dslParser;
        _logger = logger;
    }

    /// <summary>
    /// Create a new workflow definition
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> CreateWorkflow([FromBody] CreateWorkflowRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Validate workflow definition
            var validation = await _dslParser.ValidateDSLAsync(request.WorkflowDefinition);
            if (!validation.IsValid)
            {
                return BadRequest(new
                {
                    message = "Invalid workflow definition",
                    errors = validation.Errors,
                    warnings = validation.Warnings
                });
            }

            var workflowId = await _workflowRepository.CreateWorkflowAsync(request, userId);

            _logger.LogInformation("Created workflow {WorkflowId} for user {UserId}", workflowId, userId);

            return CreatedAtAction(nameof(GetWorkflow), new { id = workflowId }, workflowId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workflow");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow definition by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowDefinitionDto>> GetWorkflow(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var workflow = await _workflowRepository.GetWorkflowByIdAsync(id, userId);
            if (workflow == null)
            {
                return NotFound($"Workflow {id} not found");
            }

            return Ok(workflow);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow {WorkflowId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Update workflow definition
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateWorkflow(Guid id, [FromBody] UpdateWorkflowRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            // Validate workflow definition if provided
            if (!string.IsNullOrEmpty(request.WorkflowDefinition))
            {
                var validation = await _dslParser.ValidateDSLAsync(request.WorkflowDefinition);
                if (!validation.IsValid)
                {
                    return BadRequest(new
                    {
                        message = "Invalid workflow definition",
                        errors = validation.Errors,
                        warnings = validation.Warnings
                    });
                }
            }

            var success = await _workflowRepository.UpdateWorkflowAsync(id, request, userId);
            if (!success)
            {
                return NotFound($"Workflow {id} not found or update failed");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update workflow {WorkflowId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete workflow definition
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteWorkflow(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var success = await _workflowRepository.DeleteWorkflowAsync(id, userId);
            if (!success)
            {
                return NotFound($"Workflow {id} not found");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete workflow {WorkflowId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Search workflows with filters and pagination
    /// </summary>
    [HttpPost("search")]
    public async Task<ActionResult<PaginatedResult<WorkflowDefinitionDto>>> SearchWorkflows([FromBody] WorkflowSearchRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var result = await _workflowRepository.SearchWorkflowsAsync(request, userId);
            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search workflows");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get all workflows for the current user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<WorkflowDefinitionDto>>> GetUserWorkflows([FromQuery] int limit = 100)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var workflows = await _workflowRepository.GetWorkflowsByUserAsync(userId, limit);
            return Ok(workflows);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get user workflows");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Update workflow status
    /// </summary>
    [HttpPatch("{id}/status")]
    public async Task<ActionResult> UpdateWorkflowStatus(Guid id, [FromBody] WorkflowStatus status)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var success = await _workflowRepository.UpdateWorkflowStatusAsync(id, status, userId);
            if (!success)
            {
                return NotFound($"Workflow {id} not found");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update workflow status for {WorkflowId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Validate workflow definition
    /// </summary>
    [HttpPost("validate")]
    public async Task<ActionResult<WorkflowValidationResult>> ValidateWorkflow([FromBody] string workflowDefinition)
    {
        try
        {
            var validation = await _dslParser.ValidateDSLAsync(workflowDefinition);
            return Ok(validation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate workflow");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get workflow execution statistics
    /// </summary>
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<WorkflowExecutionStatsDto>> GetWorkflowStats(
        Guid id,
        [FromQuery] DateTime? fromDate = null,
        [FromQuery] DateTime? toDate = null)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var stats = await _workflowRepository.GetWorkflowStatsAsync(id, userId, fromDate, toDate);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get workflow stats for {WorkflowId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }
}