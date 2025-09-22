/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Multi-Context Protocol (MCP) agent repository for agent lifecycle management.
 * Features agent registration, discovery, heartbeat monitoring, and connection management with user-scoped security.
 */

using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Hartonomous.Core.Enums;
using Hartonomous.Core.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

/// <summary>
/// Repository implementation for agent management using proper architecture patterns
/// </summary>
public class AgentRepository : BaseRepository<Agent, Guid>, IAgentRepository
{
    public AgentRepository(IOptions<SqlServerOptions> sqlOptions, HartonomousDbContext context) : base(sqlOptions, context)
    {
    }

    protected override string GetTableName() => "dbo.Agents";

    protected override string GetSelectColumns() =>
        "AgentId as Id, UserId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, Status, RegisteredAt as CreatedDate, LastHeartbeat, Metrics";

    protected override (string Columns, string Parameters) GetInsertColumnsAndParameters() =>
        ("AgentId, UserId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, Status, RegisteredAt, LastHeartbeat",
         "@Id, @UserId, @AgentName, @AgentType, @ConnectionId, @Capabilities, @Description, @Configuration, @Status, @CreatedDate, @LastHeartbeat");

    protected override string GetUpdateSetClause() =>
        "AgentName = @AgentName, AgentType = @AgentType, ConnectionId = @ConnectionId, Capabilities = @Capabilities, Description = @Description, Configuration = @Configuration, Status = @Status, LastHeartbeat = @LastHeartbeat, Metrics = @Metrics";

    protected override Agent MapToEntity(dynamic row)
    {
        return new Agent
        {
            Id = row.Id,
            UserId = row.UserId,
            AgentName = row.AgentName,
            AgentType = row.AgentType,
            ConnectionId = row.ConnectionId,
            Capabilities = DeserializeFromJson<string[]>(row.Capabilities) ?? Array.Empty<string>(),
            Description = row.Description,
            Configuration = DeserializeFromJson<Dictionary<string, object>>(row.Configuration) ?? new Dictionary<string, object>(),
            Status = (AgentStatus)row.Status,
            CreatedDate = row.CreatedDate,
            LastHeartbeat = row.LastHeartbeat,
            Metrics = DeserializeFromJson<Dictionary<string, object>>(row.Metrics) ?? new Dictionary<string, object>()
        };
    }

    protected override object GetParameters(Agent entity)
    {
        return new
        {
            Id = entity.Id,
            UserId = entity.UserId,
            AgentName = entity.AgentName,
            AgentType = entity.AgentType,
            ConnectionId = entity.ConnectionId,
            Capabilities = SerializeToJson(entity.Capabilities),
            Description = entity.Description,
            Configuration = SerializeToJson(entity.Configuration),
            Status = (int)entity.Status,
            CreatedDate = entity.CreatedDate,
            LastHeartbeat = entity.LastHeartbeat,
            Metrics = SerializeToJson(entity.Metrics)
        };
    }

    protected override object[] GetParametersArray(Agent entity)
    {
        return new object[]
        {
            entity.Id,
            entity.UserId,
            entity.AgentName,
            entity.AgentType,
            entity.ConnectionId ?? (object)DBNull.Value,
            SerializeToJson(entity.Capabilities),
            entity.Description ?? (object)DBNull.Value,
            SerializeToJson(entity.Configuration),
            (int)entity.Status,
            entity.CreatedDate,
            entity.LastHeartbeat ?? (object)DBNull.Value,
            SerializeToJson(entity.Metrics)
        };
    }

    public async Task<Guid> RegisterAgentAsync(AgentRegistrationRequest request, string connectionId, string userId)
    {
        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var agent = new Agent
        {
            Id = agentId,
            UserId = userId,
            AgentName = request.AgentName,
            AgentType = request.AgentType,
            ConnectionId = connectionId,
            Capabilities = request.Capabilities ?? Array.Empty<string>(),
            Description = request.Description,
            Configuration = request.Configuration ?? new Dictionary<string, object>(),
            Status = AgentStatus.Online,
            CreatedDate = now,
            LastHeartbeat = now,
            Metrics = new Dictionary<string, object>()
        };

        await CreateAsync(agent);
        return agentId;
    }

    public async Task<bool> UpdateAgentConnectionAsync(Guid agentId, string connectionId, string userId)
    {
        var agent = await GetByIdAsync(agentId);
        if (agent?.UserId != userId) return false;

        agent.ConnectionId = connectionId;
        agent.Status = AgentStatus.Online;
        agent.LastHeartbeat = DateTime.UtcNow;

        return await UpdateAsync(agent);
    }

    // DTO convenience methods for the MCP hub layer
    public async Task<AgentDto?> GetAgentDtoByIdAsync(Guid agentId, string userId)
    {
        var agent = await GetByIdAsync(agentId);
        if (agent?.UserId != userId) return null;

        return MapToAgentDto(agent);
    }

    public async Task<IEnumerable<AgentDto>> GetAllAgentDtosAsync(string userId)
    {
        var agents = await GetByUserAsync(userId);
        return agents.Select(MapToAgentDto);
    }

    public async Task<IEnumerable<AgentDto>> GetAgentsByUserAsync(string userId)
    {
        return await GetAllAgentDtosAsync(userId);
    }

    public async Task<AgentDto?> GetAgentByIdAsync(Guid agentId, string userId)
    {
        return await GetAgentDtoByIdAsync(agentId, userId);
    }

    public async Task<bool> UnregisterAgentAsync(Guid agentId, string userId)
    {
        var agent = await GetByIdAsync(agentId);
        if (agent?.UserId != userId) return false;

        return await DeleteAsync(agentId);
    }

    public async Task<bool> UpdateAgentHeartbeatAsync(Guid agentId, AgentStatus status, string userId, Dictionary<string, object>? metrics = null)
    {
        var agent = await GetByIdAsync(agentId);
        if (agent?.UserId != userId) return false;

        agent.Status = status;
        agent.LastHeartbeat = DateTime.UtcNow;
        if (metrics != null)
            agent.Metrics = metrics;

        return await UpdateAsync(agent);
    }

    public async Task<IEnumerable<AgentDto>> DiscoverAgentsAsync(AgentDiscoveryRequest request, string userId)
    {
        var agents = await GetByUserAsync(userId);

        // Filter by status
        var filteredAgents = agents.Where(a => a.Status == AgentStatus.Online);

        // Filter by agent type if specified
        if (!string.IsNullOrEmpty(request.AgentType))
        {
            filteredAgents = filteredAgents.Where(a => a.AgentType == request.AgentType);
        }

        var agentDtos = filteredAgents.Select(MapToAgentDto);

        // Filter by capabilities if requested
        if (request.RequiredCapabilities?.Any() == true)
        {
            agentDtos = agentDtos.Where(agent =>
                request.RequiredCapabilities.All(required =>
                    agent.Capabilities.Contains(required)));
        }

        return agentDtos.OrderByDescending(a => a.LastHeartbeat);
    }

    public async Task<bool> DeleteAsync(Guid agentId, string userId)
    {
        var agent = await GetByIdAsync(agentId);
        if (agent?.UserId != userId) return false;

        return await DeleteAsync(agentId);
    }

    public async Task<bool> UpdateAgentStatusAsync(Guid agentId, AgentStatus status, string userId)
    {
        var agent = await GetByIdAsync(agentId);
        if (agent?.UserId != userId) return false;

        agent.Status = status;
        agent.LastHeartbeat = DateTime.UtcNow;

        return await UpdateAsync(agent);
    }

    public async Task<AgentDto?> GetAgentByConnectionIdAsync(string connectionId)
    {
        var agents = await _context.Agents
            .Where(a => a.ConnectionId == connectionId)
            .FirstOrDefaultAsync();

        return agents != null ? MapToAgentDto(agents) : null;
    }

    private static AgentDto MapToAgentDto(Agent agent)
    {
        return new AgentDto(
            agent.Id,
            agent.AgentName,
            agent.AgentType,
            agent.ConnectionId,
            agent.Capabilities ?? Array.Empty<string>(),
            agent.Description,
            agent.Configuration,
            agent.CreatedDate,
            agent.LastHeartbeat,
            agent.Status
        );
    }

}