/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Model Query Engine (MQE) neural network topology mapping API controller,
 * providing graph-based model architecture analysis and T-SQL queryable network structure management.
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
public class NeuralMapController : ControllerBase
{
    private readonly INeuralMapRepository _neuralMapRepository;
    private readonly ILogger<NeuralMapController> _logger;

    public NeuralMapController(INeuralMapRepository neuralMapRepository, ILogger<NeuralMapController> logger)
    {
        _neuralMapRepository = neuralMapRepository;
        _logger = logger;
    }

    private string GetUserId() => User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? throw new UnauthorizedAccessException("User ID not found in token");

    [HttpGet("models/{modelId}/graph")]
    public async Task<ActionResult<NeuralMapGraphDto>> GetModelGraph(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var graph = await _neuralMapRepository.GetModelGraphAsync(modelId, userId);

            if (graph == null)
                return NotFound($"Neural map graph not found for model {modelId}");

            return Ok(graph);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving neural map graph for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("models/{modelId}/nodes")]
    public async Task<ActionResult<IEnumerable<NeuralMapNodeDto>>> GetNodes(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var nodes = await _neuralMapRepository.GetNodesAsync(modelId, userId);
            return Ok(nodes);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving nodes for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("models/{modelId}/edges")]
    public async Task<ActionResult<IEnumerable<NeuralMapEdgeDto>>> GetEdges(Guid modelId)
    {
        try
        {
            var userId = GetUserId();
            var edges = await _neuralMapRepository.GetEdgesAsync(modelId, userId);
            return Ok(edges);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving edges for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("models/{modelId}/nodes")]
    public async Task<ActionResult<Guid>> CreateNode(Guid modelId, [FromBody] CreateNodeRequest request)
    {
        try
        {
            var userId = GetUserId();
            var nodeId = await _neuralMapRepository.CreateNodeAsync(
                modelId,
                request.NodeType,
                request.Name,
                request.Properties,
                userId);

            return CreatedAtAction(nameof(GetNodes), new { modelId }, nodeId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating node for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("models/{modelId}/edges")]
    public async Task<ActionResult<Guid>> CreateEdge(Guid modelId, [FromBody] CreateEdgeRequest request)
    {
        try
        {
            var userId = GetUserId();
            var edgeId = await _neuralMapRepository.CreateEdgeAsync(
                request.SourceNodeId,
                request.TargetNodeId,
                request.RelationType,
                request.Weight,
                request.Properties,
                userId);

            return CreatedAtAction(nameof(GetEdges), new { modelId }, edgeId);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating edge for model {ModelId}", modelId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("nodes/{nodeId}")]
    public async Task<ActionResult> DeleteNode(Guid nodeId)
    {
        try
        {
            var userId = GetUserId();
            var deleted = await _neuralMapRepository.DeleteNodeAsync(nodeId, userId);

            if (!deleted)
                return NotFound($"Node {nodeId} not found or access denied");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting node {NodeId}", nodeId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("edges/{edgeId}")]
    public async Task<ActionResult> DeleteEdge(Guid edgeId)
    {
        try
        {
            var userId = GetUserId();
            var deleted = await _neuralMapRepository.DeleteEdgeAsync(edgeId, userId);

            if (!deleted)
                return NotFound($"Edge {edgeId} not found or access denied");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting edge {EdgeId}", edgeId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("nodes/{nodeId}/properties")]
    public async Task<ActionResult> UpdateNodeProperties(Guid nodeId, [FromBody] Dictionary<string, object> properties)
    {
        try
        {
            var userId = GetUserId();
            var updated = await _neuralMapRepository.UpdateNodePropertiesAsync(nodeId, properties, userId);

            if (!updated)
                return NotFound($"Node {nodeId} not found or access denied");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating node properties {NodeId}", nodeId);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("edges/{edgeId}/properties")]
    public async Task<ActionResult> UpdateEdgeProperties(Guid edgeId, [FromBody] Dictionary<string, object> properties)
    {
        try
        {
            var userId = GetUserId();
            var updated = await _neuralMapRepository.UpdateEdgePropertiesAsync(edgeId, properties, userId);

            if (!updated)
                return NotFound($"Edge {edgeId} not found or access denied");

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating edge properties {EdgeId}", edgeId);
            return StatusCode(500, "Internal server error");
        }
    }
}

public record CreateNodeRequest(string NodeType, string Name, Dictionary<string, object> Properties);
public record CreateEdgeRequest(Guid SourceNodeId, Guid TargetNodeId, string RelationType, double Weight, Dictionary<string, object> Properties);