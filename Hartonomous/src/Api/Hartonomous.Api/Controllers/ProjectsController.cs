/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Projects Controller - API endpoints for project lifecycle
 * management. The user-scoped access patterns and project organization functionality
 * represent core platform capabilities.
 */

using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ProjectsController : ControllerBase
{
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<ProjectsController> _logger;

    public ProjectsController(IProjectRepository projectRepository, ILogger<ProjectsController> logger)
    {
        _projectRepository = projectRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all projects for the authenticated user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ProjectDto>>> GetProjects()
    {
        try
        {
            var userId = User.GetUserId();
            var projects = await _projectRepository.GetProjectsByUserAsync(userId);
            return Ok(projects);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to get projects");
            return Unauthorized("Invalid user credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving projects");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get a specific project by ID (user-scoped)
    /// </summary>
    [HttpGet("{id:guid}")]
    public async Task<ActionResult<ProjectDto>> GetProject(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            var project = await _projectRepository.GetProjectByIdAsync(id, userId);

            if (project == null)
            {
                return NotFound($"Project with ID {id} not found or access denied");
            }

            return Ok(project);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to get project {ProjectId}", id);
            return Unauthorized("Invalid user credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving project {ProjectId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Create a new project
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> CreateProject([FromBody] CreateProjectRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ProjectName))
            {
                return BadRequest("Project name is required");
            }

            var userId = User.GetUserId();
            var projectId = await _projectRepository.CreateProjectAsync(request, userId);

            _logger.LogInformation("Created project {ProjectId} for user {UserId}", projectId, userId);
            return CreatedAtAction(nameof(GetProject), new { id = projectId }, projectId);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to create project");
            return Unauthorized("Invalid user credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating project");
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Delete a project (user-scoped)
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<ActionResult> DeleteProject(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            var deleted = await _projectRepository.DeleteProjectAsync(id, userId);

            if (!deleted)
            {
                return NotFound($"Project with ID {id} not found or access denied");
            }

            _logger.LogInformation("Deleted project {ProjectId} for user {UserId}", id, userId);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to delete project {ProjectId}", id);
            return Unauthorized("Invalid user credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting project {ProjectId}", id);
            return StatusCode(500, "Internal server error");
        }
    }
}