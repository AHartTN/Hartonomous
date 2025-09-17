using Hartonomous.Core.Shared.DTOs;
using Hartonomous.Core.Shared.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace Hartonomous.MCP.Controllers;

/// <summary>
/// REST API controller for agent management
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
public class AgentsController : ControllerBase
{
    private readonly IAgentRepository _agentRepository;
    private readonly ILogger<AgentsController> _logger;

    public AgentsController(IAgentRepository agentRepository, ILogger<AgentsController> logger)
    {
        _agentRepository = agentRepository;
        _logger = logger;
    }

    /// <summary>
    /// Get all agents for the authenticated user
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<AgentDto>>> GetAgents()
    {
        try
        {
            var userId = GetUserId();
            var agents = await _agentRepository.GetAgentsByUserAsync(userId);
            return Ok(agents);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get agents for user {UserId}", GetUserId());
            return StatusCode(500, new { Message = "Failed to retrieve agents", Error = ex.Message });
        }
    }

    /// <summary>
    /// Get a specific agent by ID
    /// </summary>
    [HttpGet("{agentId:guid}")]
    public async Task<ActionResult<AgentDto>> GetAgent(Guid agentId)
    {
        try
        {
            var userId = GetUserId();
            var agent = await _agentRepository.GetAgentByIdAsync(agentId, userId);

            if (agent == null)
            {
                return NotFound(new { Message = "Agent not found" });
            }

            return Ok(agent);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get agent {AgentId} for user {UserId}", agentId, GetUserId());
            return StatusCode(500, new { Message = "Failed to retrieve agent", Error = ex.Message });
        }
    }

    /// <summary>
    /// Discover agents based on criteria
    /// </summary>
    [HttpPost("discover")]
    public async Task<ActionResult<AgentDiscoveryResponse>> DiscoverAgents([FromBody] AgentDiscoveryRequest request)
    {
        try
        {
            var userId = GetUserId();
            var agents = await _agentRepository.DiscoverAgentsAsync(request, userId);
            return Ok(new AgentDiscoveryResponse(agents));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover agents for user {UserId}", GetUserId());
            return StatusCode(500, new { Message = "Failed to discover agents", Error = ex.Message });
        }
    }

    /// <summary>
    /// Update agent status
    /// </summary>
    [HttpPut("{agentId:guid}/status")]
    public async Task<ActionResult> UpdateAgentStatus(Guid agentId, [FromBody] UpdateAgentStatusRequest request)
    {
        try
        {
            var userId = GetUserId();
            var success = await _agentRepository.UpdateAgentStatusAsync(agentId, request.Status, userId);

            if (!success)
            {
                return NotFound(new { Message = "Agent not found or access denied" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to update status for agent {AgentId} for user {UserId}", agentId, GetUserId());
            return StatusCode(500, new { Message = "Failed to update agent status", Error = ex.Message });
        }
    }

    /// <summary>
    /// Unregister an agent
    /// </summary>
    [HttpDelete("{agentId:guid}")]
    public async Task<ActionResult> UnregisterAgent(Guid agentId)
    {
        try
        {
            var userId = GetUserId();
            var success = await _agentRepository.UnregisterAgentAsync(agentId, userId);

            if (!success)
            {
                return NotFound(new { Message = "Agent not found or access denied" });
            }

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to unregister agent {AgentId} for user {UserId}", agentId, GetUserId());
            return StatusCode(500, new { Message = "Failed to unregister agent", Error = ex.Message });
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
/// Request model for updating agent status
/// </summary>
public record UpdateAgentStatusRequest(AgentStatus Status);