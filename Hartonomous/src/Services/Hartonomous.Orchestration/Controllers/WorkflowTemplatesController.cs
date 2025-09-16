using Hartonomous.Infrastructure.Security;
using Hartonomous.Orchestration.DTOs;
using Hartonomous.Orchestration.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hartonomous.Orchestration.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class WorkflowTemplatesController : ControllerBase
{
    private readonly IWorkflowTemplateService _templateService;
    private readonly ILogger<WorkflowTemplatesController> _logger;

    public WorkflowTemplatesController(
        IWorkflowTemplateService templateService,
        ILogger<WorkflowTemplatesController> logger)
    {
        _templateService = templateService;
        _logger = logger;
    }

    /// <summary>
    /// Create template from existing workflow
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<Guid>> CreateTemplate([FromBody] CreateTemplateFromWorkflowRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var templateId = await _templateService.CreateTemplateAsync(request, userId);

            _logger.LogInformation("Created template {TemplateId} for user {UserId}", templateId, userId);

            return CreatedAtAction(nameof(GetTemplate), new { id = templateId }, templateId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create template");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get template by ID
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<WorkflowTemplateDto>> GetTemplate(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var template = await _templateService.GetTemplateByIdAsync(id, userId);
            if (template == null)
            {
                return NotFound($"Template {id} not found");
            }

            return Ok(template);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get template {TemplateId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Update template
    /// </summary>
    [HttpPut("{id}")]
    public async Task<ActionResult> UpdateTemplate(Guid id, [FromBody] WorkflowTemplateDto template)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var success = await _templateService.UpdateTemplateAsync(id, template, userId);
            if (!success)
            {
                return NotFound($"Template {id} not found or update failed");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update template {TemplateId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Delete template
    /// </summary>
    [HttpDelete("{id}")]
    public async Task<ActionResult> DeleteTemplate(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var success = await _templateService.DeleteTemplateAsync(id, userId);
            if (!success)
            {
                return NotFound($"Template {id} not found");
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete template {TemplateId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Search templates with filters and pagination
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PaginatedResult<WorkflowTemplateDto>>> SearchTemplates(
        [FromQuery] string? query = null,
        [FromQuery] string? category = null,
        [FromQuery] string? tags = null,
        [FromQuery] bool includePublic = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var tagList = string.IsNullOrEmpty(tags)
                ? null
                : tags.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList();

            var result = await _templateService.SearchTemplatesAsync(
                query, category, tagList, includePublic, userId, page, pageSize);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to search templates");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get templates by category
    /// </summary>
    [HttpGet("category/{category}")]
    public async Task<ActionResult<List<WorkflowTemplateDto>>> GetTemplatesByCategory(
        string category,
        [FromQuery] bool includePublic = false)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var templates = await _templateService.GetTemplatesByCategoryAsync(category, includePublic, userId);
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get templates by category {Category}", category);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get popular templates
    /// </summary>
    [HttpGet("popular")]
    public async Task<ActionResult<List<WorkflowTemplateDto>>> GetPopularTemplates(
        [FromQuery] int limit = 10,
        [FromQuery] bool includePublic = true)
    {
        try
        {
            var templates = await _templateService.GetPopularTemplatesAsync(limit, includePublic);
            return Ok(templates);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get popular templates");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Create workflow from template
    /// </summary>
    [HttpPost("{id}/workflows")]
    public async Task<ActionResult<Guid>> CreateWorkflowFromTemplate(
        Guid id,
        [FromBody] CreateWorkflowFromTemplateRequest request)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var workflowId = await _templateService.CreateWorkflowFromTemplateAsync(
                id, request.WorkflowName, request.Parameters, userId);

            _logger.LogInformation("Created workflow {WorkflowId} from template {TemplateId} for user {UserId}",
                workflowId, id, userId);

            return CreatedAtAction("GetWorkflow", "Workflows", new { id = workflowId }, workflowId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create workflow from template {TemplateId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Validate template parameters
    /// </summary>
    [HttpPost("{id}/validate")]
    public async Task<ActionResult<WorkflowValidationResult>> ValidateTemplateParameters(
        Guid id,
        [FromBody] Dictionary<string, object> parameters)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var validation = await _templateService.ValidateTemplateParametersAsync(id, parameters, userId);
            return Ok(validation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to validate template parameters for {TemplateId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Get template usage statistics
    /// </summary>
    [HttpGet("{id}/stats")]
    public async Task<ActionResult<Dictionary<string, object>>> GetTemplateUsageStats(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var stats = await _templateService.GetTemplateUsageStatsAsync(id, userId);
            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get template usage stats for {TemplateId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Export template to file
    /// </summary>
    [HttpGet("{id}/export")]
    public async Task<ActionResult> ExportTemplate(Guid id)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            var templateData = await _templateService.ExportTemplateAsync(id, userId);
            var template = await _templateService.GetTemplateByIdAsync(id, userId);

            var fileName = $"{template?.Name ?? "template"}_{id}.json";

            return File(templateData, "application/json", fileName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to export template {TemplateId}", id);
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }

    /// <summary>
    /// Import template from file
    /// </summary>
    [HttpPost("import")]
    public async Task<ActionResult<Guid>> ImportTemplate(IFormFile file)
    {
        try
        {
            var userId = User.GetUserId();
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized("User ID not found in token");
            }

            if (file == null || file.Length == 0)
            {
                return BadRequest("No file provided");
            }

            using var memoryStream = new MemoryStream();
            await file.CopyToAsync(memoryStream);
            var templateData = memoryStream.ToArray();

            var templateId = await _templateService.ImportTemplateAsync(templateData, userId);

            _logger.LogInformation("Imported template {TemplateId} for user {UserId}", templateId, userId);

            return CreatedAtAction(nameof(GetTemplate), new { id = templateId }, templateId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to import template");
            return StatusCode(500, new { message = "Internal server error", details = ex.Message });
        }
    }
}

/// <summary>
/// Request DTO for creating workflow from template
/// </summary>
public record CreateWorkflowFromTemplateRequest(
    string WorkflowName,
    Dictionary<string, object>? Parameters = null
);