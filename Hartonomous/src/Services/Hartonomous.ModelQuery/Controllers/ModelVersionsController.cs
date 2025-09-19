/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Model Query Engine (MQE) version management API controller,
 * providing T-SQL queryable model versioning, comparison, and lifecycle tracking capabilities.
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;

namespace Hartonomous.ModelQuery.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModelVersionsController : ControllerBase
{
    private readonly IModelVersionRepository _versionRepository;
    private readonly ILogger<ModelVersionsController> _logger;

    public ModelVersionsController(IModelVersionRepository versionRepository, ILogger<ModelVersionsController> logger)
    {
        _versionRepository = versionRepository;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("User ID not found in token");

    [HttpGet("models/{modelId}")]
    public async Task<ActionResult<IEnumerable<ModelVersionDto>>> GetModelVersions(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var versions = await _versionRepository.GetModelVersionsAsync(modelId, userId);
            return Ok(versions);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving versions for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{versionId}")]
    public async Task<ActionResult<ModelVersionDto>> GetVersion(Guid versionId)
    {
        try
        {
            var userId = GetUserId();
            var version = await _versionRepository.GetVersionByIdAsync(versionId, userId);

            if (version == null)
                return NotFound($"Version {versionId} not found or access denied");

            return Ok(version);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving version {VersionId}", versionId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("models/{modelId}/latest")]
    public async Task<ActionResult<ModelVersionDto>> GetLatestVersion(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var version = await _versionRepository.GetLatestVersionAsync(modelId, userId);

            if (version == null)
                return NotFound($"No versions found for model {modelId} or access denied");

            return Ok(version);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving latest version for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("models/{modelId}")]
    public async Task<ActionResult<Guid>> CreateVersion(Guid modelId, [FromBody] CreateVersionRequest request)
    {
        try
        {
            var userId = GetUserId();
            var versionId = await _versionRepository.CreateVersionAsync(
                modelId,
                request.Version,
                request.Description,
                request.Changes,
                request.ParentVersion,
                userId);

            return CreatedAtAction(nameof(GetVersion), new { versionId }, versionId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating version for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{versionId}")]
    public async Task<ActionResult> DeleteVersion(Guid versionId)
    {
        try
        {
            var userId = GetUserId();
            var deleted = await _versionRepository.DeleteVersionAsync(versionId, userId);

            if (!deleted)
                return NotFound($"Version {versionId} not found or access denied");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting version {VersionId}", versionId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("compare")]
    public async Task<ActionResult<ModelComparisonDto>> CompareVersions([FromBody] CompareVersionsRequest request)
    {
        try
        {
            var userId = GetUserId();
            var comparison = await _versionRepository.CompareVersionsAsync(request.VersionAId, request.VersionBId, userId);

            if (comparison == null)
                return NotFound("One or both versions not found or access denied");

            return Ok(comparison);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing versions {VersionAId} and {VersionBId}", request.VersionAId, request.VersionBId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public record CreateVersionRequest(string Version, string Description, Dictionary<string, object> Changes, string? ParentVersion);
public record CompareVersionsRequest(Guid VersionAId, Guid VersionBId);