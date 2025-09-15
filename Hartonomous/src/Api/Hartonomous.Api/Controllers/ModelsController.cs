using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;

namespace Hartonomous.Api.Controllers;

[ApiController]
[Route("api/projects/{projectId:guid}/[controller]")]
[Authorize]
public class ModelsController : ControllerBase
{
    private readonly IModelRepository _modelRepository;
    private readonly IProjectRepository _projectRepository;
    private readonly ILogger<ModelsController> _logger;

    public ModelsController(
        IModelRepository modelRepository,
        IProjectRepository projectRepository,
        ILogger<ModelsController> logger)
    {
        _modelRepository = modelRepository;
        _projectRepository = projectRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all models for a specific project (user-scoped)
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ModelMetadataDto>>> GetModels(Guid projectId)
    {
        try
        {
            var userId = User.GetUserId();

            // Verify user owns the project
            var project = await _projectRepository.GetProjectByIdAsync(projectId, userId);
            if (project == null)
            {
                return NotFound($"Project with ID {projectId} not found or access denied");
            }

            var models = await _modelRepository.GetModelsByProjectAsync(projectId, userId);
            return Ok(models);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to get models for project {ProjectId}", projectId);
            return Unauthorized("Invalid user credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving models for project {ProjectId}", projectId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get a specific model by ID (user-scoped)
    /// </summary>
    [HttpGet("{modelId:guid}")]
    public async Task<ActionResult<ModelMetadataDto>> GetModel(Guid projectId, Guid modelId)
    {
        try
        {
            var userId = User.GetUserId();
            var model = await _modelRepository.GetModelByIdAsync(modelId, userId);

            if (model == null)
            {
                return NotFound($"Model with ID {modelId} not found or access denied");
            }

            return Ok(model);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to get model {ModelId}", modelId);
            return Unauthorized("Invalid user credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Create a new model in a project
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> CreateModel(Guid projectId, [FromBody] CreateModelRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.ModelName))
            {
                return BadRequest("Model name is required");
            }

            if (string.IsNullOrWhiteSpace(request.Version))
            {
                return BadRequest("Version is required");
            }

            if (string.IsNullOrWhiteSpace(request.License))
            {
                return BadRequest("License is required");
            }

            var userId = User.GetUserId();

            var modelId = await _modelRepository.CreateModelAsync(
                projectId,
                request.ModelName,
                request.Version,
                request.License,
                request.MetadataJson,
                userId);

            _logger.LogInformation("Created model {ModelId} in project {ProjectId} for user {UserId}",
                modelId, projectId, userId);

            return CreatedAtAction(nameof(GetModel),
                new { projectId, modelId }, modelId);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to create model in project {ProjectId}", projectId);
            return Unauthorized("Project not found or access denied");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating model in project {ProjectId}", projectId);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Delete a model (user-scoped)
    /// </summary>
    [HttpDelete("{modelId:guid}")]
    public async Task<ActionResult> DeleteModel(Guid projectId, Guid modelId)
    {
        try
        {
            var userId = User.GetUserId();
            var deleted = await _modelRepository.DeleteModelAsync(modelId, userId);

            if (!deleted)
            {
                return NotFound($"Model with ID {modelId} not found or access denied");
            }

            _logger.LogInformation("Deleted model {ModelId} from project {ProjectId} for user {UserId}",
                modelId, projectId, userId);
            return NoContent();
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Unauthorized access attempt to delete model {ModelId}", modelId);
            return Unauthorized("Invalid user credentials");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public record CreateModelRequest(
    [Required(ErrorMessage = "Model name is required")]
    [StringLength(256, MinimumLength = 1, ErrorMessage = "Model name must be between 1 and 256 characters")]
    string ModelName,

    [Required(ErrorMessage = "Version is required")]
    [StringLength(50, MinimumLength = 1, ErrorMessage = "Version must be between 1 and 50 characters")]
    string Version,

    [Required(ErrorMessage = "License is required")]
    [StringLength(100, MinimumLength = 1, ErrorMessage = "License must be between 1 and 100 characters")]
    string License,

    string? MetadataJson = null);