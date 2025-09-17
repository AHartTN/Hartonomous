using Hartonomous.Core.Shared.DTOs;
using Hartonomous.Core.Shared.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using System.Security.Claims;
using System.Text.Json;

namespace Hartonomous.MCP.Hubs;

/// <summary>
/// SignalR Hub for Multi-Agent Context Protocol communication
/// </summary>
[Authorize]
public class McpHub : Hub
{
    private readonly IAgentRepository _agentRepository;
    private readonly IMessageRepository _messageRepository;
    private readonly ILogger<McpHub> _logger;

    public McpHub(
        IAgentRepository agentRepository,
        IMessageRepository messageRepository,
        ILogger<McpHub> logger)
    {
        _agentRepository = agentRepository;
        _messageRepository = messageRepository;
        _logger = logger;
    }

    /// <summary>
    /// Register an agent when connecting
    /// </summary>
    public async Task RegisterAgent(AgentRegistrationRequest request)
    {
        try
        {
            var userId = GetUserId();
            var connectionId = Context.ConnectionId;

            _logger.LogInformation("Registering agent {AgentName} for user {UserId}", request.AgentName, userId);

            var agentId = await _agentRepository.RegisterAgentAsync(request, connectionId, userId);

            // Add to group for user-scoped communication
            await Groups.AddToGroupAsync(connectionId, $"User_{userId}");
            await Groups.AddToGroupAsync(connectionId, $"Agent_{agentId}");

            // Notify successful registration
            await Clients.Caller.SendAsync("AgentRegistered", new { AgentId = agentId });

            // Notify other agents in the user's scope
            await Clients.Group($"User_{userId}").SendAsync("AgentJoined", new AgentDto(
                agentId,
                request.AgentName,
                request.AgentType,
                connectionId,
                request.Capabilities,
                request.Description,
                request.Configuration,
                DateTime.UtcNow,
                DateTime.UtcNow,
                AgentStatus.Online
            ));

            _logger.LogInformation("Successfully registered agent {AgentId} for user {UserId}", agentId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to register agent {AgentName} for user {UserId}", request.AgentName, GetUserId());
            await Clients.Caller.SendAsync("Error", new { Message = "Failed to register agent", Error = ex.Message });
        }
    }

    /// <summary>
    /// Send a message to another agent
    /// </summary>
    public async Task SendMessage(Guid toAgentId, string messageType, object payload, Dictionary<string, object>? metadata = null)
    {
        try
        {
            var userId = GetUserId();
            var fromAgent = await _agentRepository.GetAgentByConnectionIdAsync(Context.ConnectionId);

            if (fromAgent == null)
            {
                await Clients.Caller.SendAsync("Error", new { Message = "Agent not registered" });
                return;
            }

            // Validate target agent exists and belongs to the same user
            var toAgent = await _agentRepository.GetAgentByIdAsync(toAgentId, userId);
            if (toAgent == null)
            {
                await Clients.Caller.SendAsync("Error", new { Message = "Target agent not found or access denied" });
                return;
            }

            var message = new McpMessage(
                Guid.NewGuid(),
                fromAgent.AgentId,
                toAgentId,
                messageType,
                payload,
                metadata
            );

            // Store message in database
            await _messageRepository.StoreMessageAsync(message, userId);

            // Send to target agent
            await Clients.Group($"Agent_{toAgentId}").SendAsync("MessageReceived", message);

            _logger.LogDebug("Message sent from {FromAgent} to {ToAgent} of type {MessageType}",
                fromAgent.AgentId, toAgentId, messageType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send message from connection {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", new { Message = "Failed to send message", Error = ex.Message });
        }
    }

    /// <summary>
    /// Broadcast message to all agents of a specific type
    /// </summary>
    public async Task BroadcastToAgentType(string agentType, string messageType, object payload, Dictionary<string, object>? metadata = null)
    {
        try
        {
            var userId = GetUserId();
            var fromAgent = await _agentRepository.GetAgentByConnectionIdAsync(Context.ConnectionId);

            if (fromAgent == null)
            {
                await Clients.Caller.SendAsync("Error", new { Message = "Agent not registered" });
                return;
            }

            // Get all agents of the specified type for this user
            var targetAgents = await _agentRepository.DiscoverAgentsAsync(
                new AgentDiscoveryRequest(agentType), userId);

            var message = new McpMessage(
                Guid.NewGuid(),
                fromAgent.AgentId,
                null, // Broadcast message
                messageType,
                payload,
                metadata
            );

            // Store message for each target agent
            foreach (var agent in targetAgents)
            {
                if (agent.AgentId != fromAgent.AgentId) // Don't send to self
                {
                    var targetMessage = message with { ToAgentId = agent.AgentId };
                    await _messageRepository.StoreMessageAsync(targetMessage, userId);
                    await Clients.Group($"Agent_{agent.AgentId}").SendAsync("MessageReceived", targetMessage);
                }
            }

            _logger.LogDebug("Broadcast sent from {FromAgent} to {TargetType} agents", fromAgent.AgentId, agentType);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to broadcast message from connection {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", new { Message = "Failed to broadcast message", Error = ex.Message });
        }
    }

    /// <summary>
    /// Send heartbeat to update agent status
    /// </summary>
    public async Task Heartbeat(AgentStatus status, Dictionary<string, object>? metrics = null)
    {
        try
        {
            var userId = GetUserId();
            var agent = await _agentRepository.GetAgentByConnectionIdAsync(Context.ConnectionId);

            if (agent == null)
            {
                await Clients.Caller.SendAsync("Error", new { Message = "Agent not registered" });
                return;
            }

            await _agentRepository.UpdateAgentHeartbeatAsync(agent.AgentId, status, userId, metrics);

            // Notify other agents in the user's scope of status change
            await Clients.Group($"User_{userId}").SendAsync("AgentStatusChanged", new
            {
                AgentId = agent.AgentId,
                Status = status,
                Timestamp = DateTime.UtcNow,
                Metrics = metrics
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process heartbeat from connection {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", new { Message = "Failed to process heartbeat", Error = ex.Message });
        }
    }

    /// <summary>
    /// Submit task result
    /// </summary>
    public async Task SubmitTaskResult(Guid taskId, TaskResultStatus status, object? result = null, string? errorMessage = null, Dictionary<string, object>? metrics = null)
    {
        try
        {
            var userId = GetUserId();
            var agent = await _agentRepository.GetAgentByConnectionIdAsync(Context.ConnectionId);

            if (agent == null)
            {
                await Clients.Caller.SendAsync("Error", new { Message = "Agent not registered" });
                return;
            }

            var taskResult = new TaskResult(taskId, agent.AgentId, status, result, errorMessage, metrics);
            await _messageRepository.StoreTaskResultAsync(taskResult, userId);

            // Notify the user's group about task completion
            await Clients.Group($"User_{userId}").SendAsync("TaskCompleted", taskResult);

            _logger.LogInformation("Task {TaskId} completed by agent {AgentId} with status {Status}",
                taskId, agent.AgentId, status);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit task result from connection {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", new { Message = "Failed to submit task result", Error = ex.Message });
        }
    }

    /// <summary>
    /// Request agent discovery
    /// </summary>
    public async Task DiscoverAgents(AgentDiscoveryRequest request)
    {
        try
        {
            var userId = GetUserId();
            var agents = await _agentRepository.DiscoverAgentsAsync(request, userId);

            await Clients.Caller.SendAsync("AgentsDiscovered", new AgentDiscoveryResponse(agents));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to discover agents for connection {ConnectionId}", Context.ConnectionId);
            await Clients.Caller.SendAsync("Error", new { Message = "Failed to discover agents", Error = ex.Message });
        }
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        _logger.LogInformation("Client connected: {ConnectionId} for user {UserId}", Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        try
        {
            var userId = GetUserId();
            var agent = await _agentRepository.GetAgentByConnectionIdAsync(Context.ConnectionId);

            if (agent != null)
            {
                // Update agent status to offline
                await _agentRepository.UpdateAgentStatusAsync(agent.AgentId, AgentStatus.Offline, userId);

                // Notify other agents in the user's scope
                await Clients.Group($"User_{userId}").SendAsync("AgentDisconnected", new
                {
                    AgentId = agent.AgentId,
                    Timestamp = DateTime.UtcNow
                });

                _logger.LogInformation("Agent {AgentId} disconnected", agent.AgentId);
            }

            _logger.LogInformation("Client disconnected: {ConnectionId} for user {UserId}", Context.ConnectionId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling disconnection for {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string GetUserId()
    {
        return Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
               Context.User?.FindFirst("sub")?.Value ??
               throw new UnauthorizedAccessException("User ID not found in claims");
    }
}