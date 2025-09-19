/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Model Query Engine (MQE) neural network weights management API controller,
 * enabling FILESTREAM-based storage and T-SQL querying of model weight tensors and parameters.
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
public class ModelWeightsController : ControllerBase
{
    private readonly IModelWeightRepository _weightRepository;
    private readonly ILogger<ModelWeightsController> _logger;

    public ModelWeightsController(IModelWeightRepository weightRepository, ILogger<ModelWeightsController> logger)
    {
        _weightRepository = weightRepository;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("User ID not found in token");

    [HttpGet("models/{modelId}")]
    public async Task<ActionResult<IEnumerable<ModelWeightDto>>> GetModelWeights(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var weights = await _weightRepository.GetModelWeightsAsync(modelId, userId);
            return Ok(weights);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weights for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{weightId}")]
    public async Task<ActionResult<ModelWeightDto>> GetWeight(Guid weightId)
    {
        try
        {
            var userId = GetUserId();
            var weight = await _weightRepository.GetWeightByIdAsync(weightId, userId);

            if (weight == null)
                return NotFound($"Weight {weightId} not found or access denied");

            return Ok(weight);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weight {WeightId}", weightId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("models/{modelId}/layers/{layerName}")]
    public async Task<ActionResult<IEnumerable<ModelWeightDto>>> GetWeightsByLayer(Guid modelId, string layerName)
    {
        try
        {
            var userId = GetUserId();
            var weights = await _weightRepository.GetWeightsByLayerAsync(modelId, layerName, userId);
            return Ok(weights);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weights for model {ModelId} layer {LayerName}", modelId, layerName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("models/{modelId}")]
    public async Task<ActionResult<Guid>> CreateWeight(Guid modelId, [FromBody] CreateWeightRequest request)
    {
        try
        {
            var userId = GetUserId();
            var weightId = await _weightRepository.CreateWeightAsync(
                modelId,
                request.LayerName,
                request.WeightName,
                request.DataType,
                request.Shape,
                request.SizeBytes,
                request.StoragePath,
                request.ChecksumSha256,
                userId);

            return CreatedAtAction(nameof(GetWeight), new { weightId }, weightId);
        }
        catch (UnauthorizedAccessException ex)
        {
            return Forbid(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating weight for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{weightId}")]
    public async Task<ActionResult> DeleteWeight(Guid weightId)
    {
        try
        {
            var userId = GetUserId();
            var deleted = await _weightRepository.DeleteWeightAsync(weightId, userId);

            if (!deleted)
                return NotFound($"Weight {weightId} not found or access denied");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting weight {WeightId}", weightId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{weightId}/data")]
    public async Task<ActionResult> GetWeightData(Guid weightId)
    {
        try
        {
            var userId = GetUserId();
            var weight = await _weightRepository.GetWeightByIdAsync(weightId, userId);

            if (weight == null)
                return NotFound($"Weight {weightId} not found or access denied");

            var dataStream = await _weightRepository.GetWeightDataStreamAsync(weightId, userId);

            if (dataStream == null)
                return NotFound($"Weight data not found for {weightId}");

            return File(dataStream, "application/octet-stream", $"{weight.WeightName}.bin");
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving weight data for {WeightId}", weightId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("{weightId}/data")]
    public async Task<ActionResult> StoreWeightData(Guid weightId, IFormFile file)
    {
        try
        {
            var userId = GetUserId();

            if (file == null || file.Length == 0)
                return BadRequest("No file provided");

            using var stream = file.OpenReadStream();
            var stored = await _weightRepository.StoreWeightDataAsync(weightId, stream, userId);

            if (!stored)
                return NotFound($"Weight {weightId} not found or access denied");

            return Ok("Weight data stored successfully");
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing weight data for {WeightId}", weightId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("{weightId}/storage-path")]
    public async Task<ActionResult> UpdateStoragePath(Guid weightId, [FromBody] UpdateStoragePathRequest request)
    {
        try
        {
            var userId = GetUserId();
            var updated = await _weightRepository.UpdateWeightStoragePathAsync(weightId, request.NewStoragePath, userId);

            if (!updated)
                return NotFound($"Weight {weightId} not found or access denied");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating storage path for weight {WeightId}", weightId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public record CreateWeightRequest(string LayerName, string WeightName, string DataType, int[] Shape, long SizeBytes, string StoragePath, string ChecksumSha256);
public record UpdateStoragePathRequest(string NewStoragePath);