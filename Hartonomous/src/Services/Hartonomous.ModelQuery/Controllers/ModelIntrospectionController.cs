/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Model Query Engine (MQE) introspection API controller,
 * providing advanced model analysis, semantic search, and AI-native query capabilities for model understanding.
 */

using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Hartonomous.ModelQuery.DTOs;
using Hartonomous.ModelQuery.Interfaces;
using Hartonomous.Core.DTOs;

namespace Hartonomous.ModelQuery.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class ModelIntrospectionController : ControllerBase
{
    private readonly IModelIntrospectionService _introspectionService;
    private readonly ILogger<ModelIntrospectionController> _logger;

    public ModelIntrospectionController(IModelIntrospectionService introspectionService, ILogger<ModelIntrospectionController> logger)
    {
        _introspectionService = introspectionService;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("User ID not found in token");

    [HttpGet("models/{modelId}/analyze")]
    public async Task<ActionResult<ModelIntrospectionDto>> AnalyzeModel(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var analysis = await _introspectionService.AnalyzeModelAsync(modelId, userId);

            if (analysis == null)
                return NotFound($"Model {modelId} not found or access denied");

            return Ok(analysis);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("search")]
    public async Task<ActionResult<IEnumerable<SemanticSearchResultDto>>> SemanticSearch([FromBody] SemanticSearchRequestDto request)
    {
        try
        {
            var userId = GetUserId();
            var results = await _introspectionService.SemanticSearchAsync(request, userId);
            return Ok(results);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error performing semantic search with query: {Query}", request.Query);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("models/{modelId}/statistics")]
    public async Task<ActionResult<Dictionary<string, object>>> GetModelStatistics(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var statistics = await _introspectionService.GetModelStatisticsAsync(modelId, userId);
            return Ok(statistics);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving statistics for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("models/{modelId}/capabilities")]
    public async Task<ActionResult<IEnumerable<string>>> GetModelCapabilities(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var capabilities = await _introspectionService.GetModelCapabilitiesAsync(modelId, userId);
            return Ok(capabilities);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving capabilities for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("models/compare")]
    public async Task<ActionResult<ModelComparisonDto>> CompareModels([FromBody] CompareModelsRequest request)
    {
        try
        {
            var userId = GetUserId();
            var comparison = await _introspectionService.CompareModelsAsync(
                request.ModelAId,
                request.ModelBId,
                request.ComparisonType,
                userId);

            if (comparison == null)
                return NotFound("One or both models not found or access denied");

            return Ok(comparison);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing models {ModelAId} and {ModelBId}", request.ModelAId, request.ModelBId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public record CompareModelsRequest(Guid ModelAId, Guid ModelBId, string ComparisonType);