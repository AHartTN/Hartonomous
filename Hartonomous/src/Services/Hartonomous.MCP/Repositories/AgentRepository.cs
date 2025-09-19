/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * This file contains the Multi-Context Protocol (MCP) agent repository for agent lifecycle management.
 * Features agent registration, discovery, heartbeat monitoring, and connection management with user-scoped security.
 */

using Dapper;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Configuration;
using Hartonomous.Core.Entities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using System.Data;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

/// <summary>
/// Repository implementation for agent management using proper architecture patterns
/// </summary>
public class AgentRepository : BaseRepository<Agent, Guid>, IAgentRepository
{
    public AgentRepository(IOptions<SqlServerOptions> sqlOptions) : base(sqlOptions)
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

    public async Task<Guid> RegisterAgentAsync(AgentRegistrationRequest request, string connectionId, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.Agents (AgentId, UserId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, Status, RegisteredAt, LastHeartbeat)
            VALUES (@AgentId, @UserId, @AgentName, @AgentType, @ConnectionId, @Capabilities, @Description, @Configuration, @Status, @RegisteredAt, @LastHeartbeat);";

        var agentId = Guid.NewGuid();
        var now = DateTime.UtcNow;

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            AgentId = agentId,
            UserId = userId,
            AgentName = request.AgentName,
            AgentType = request.AgentType,
            ConnectionId = connectionId,
            Capabilities = JsonSerializer.Serialize(request.Capabilities),
            Description = request.Description,
            Configuration = request.Configuration != null ? JsonSerializer.Serialize(request.Configuration) : null,
            Status = (int)AgentStatus.Online,
            RegisteredAt = now,
            LastHeartbeat = now
        });

        return agentId;
    }

    public async Task<bool> UpdateAgentConnectionAsync(Guid agentId, string connectionId, string userId)
    {
        const string sql = @"
            UPDATE dbo.Agents
            SET ConnectionId = @ConnectionId, Status = @Status, LastHeartbeat = @LastHeartbeat
            WHERE AgentId = @AgentId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            ConnectionId = connectionId,
            Status = (int)AgentStatus.Online,
            LastHeartbeat = DateTime.UtcNow,
            AgentId = agentId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<AgentDto?> GetByIdAsync(Guid agentId, string userId)
    {
        const string sql = @"
            SELECT AgentId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, RegisteredAt, LastHeartbeat, Status
            FROM dbo.Agents
            WHERE AgentId = @AgentId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { AgentId = agentId, UserId = userId });

        return result != null ? MapToAgentDto(result) : null;
    }

    public async Task<IEnumerable<AgentDto>> GetAllAsync(string userId)
    {
        const string sql = @"
            SELECT AgentId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, RegisteredAt, LastHeartbeat, Status
            FROM dbo.Agents
            WHERE UserId = @UserId
            ORDER BY RegisteredAt DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, new { UserId = userId });

        return results.Select(MapToAgentDto);
    }

    public async Task<IEnumerable<AgentDto>> GetAgentsByUserAsync(string userId)
    {
        return await GetAllAsync(userId);
    }

    public async Task<AgentDto?> GetAgentByIdAsync(Guid agentId, string userId)
    {
        return await GetByIdAsync(agentId, userId);
    }

    public async Task<bool> UnregisterAgentAsync(Guid agentId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.Agents
            WHERE AgentId = @AgentId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { AgentId = agentId, UserId = userId });
        return rowsAffected > 0;
    }

    public async Task<bool> UpdateAgentHeartbeatAsync(Guid agentId, AgentStatus status, string userId, Dictionary<string, object>? metrics = null)
    {
        const string sql = @"
            UPDATE dbo.Agents
            SET Status = @Status, LastHeartbeat = @LastHeartbeat, Metrics = @Metrics
            WHERE AgentId = @AgentId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            LastHeartbeat = DateTime.UtcNow,
            Metrics = metrics != null ? JsonSerializer.Serialize(metrics) : null,
            AgentId = agentId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<IEnumerable<AgentDto>> DiscoverAgentsAsync(AgentDiscoveryRequest request, string userId)
    {
        var whereClause = "WHERE UserId = @UserId AND Status = @OnlineStatus";
        var parameters = new DynamicParameters();
        parameters.Add("UserId", userId);
        parameters.Add("OnlineStatus", (int)AgentStatus.Online);

        if (!string.IsNullOrEmpty(request.AgentType))
        {
            whereClause += " AND AgentType = @AgentType";
            parameters.Add("AgentType", request.AgentType);
        }

        // For capability filtering, we'll need to deserialize and check in application
        // In a production system, you might want to use a more sophisticated approach
        var sql = $@"
            SELECT AgentId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, RegisteredAt, LastHeartbeat, Status
            FROM dbo.Agents
            {whereClause}
            ORDER BY LastHeartbeat DESC;";

        using var connection = new SqlConnection(_connectionString);
        var results = await connection.QueryAsync(sql, parameters);

        var agents = results.Select(MapToAgentDto);

        // Filter by capabilities if requested
        if (request.RequiredCapabilities?.Any() == true)
        {
            agents = agents.Where(agent =>
                request.RequiredCapabilities.All(required =>
                    agent.Capabilities.Contains(required)));
        }

        return agents;
    }

    public async Task<bool> DeleteAsync(Guid agentId, string userId)
    {
        const string sql = @"
            DELETE FROM dbo.Agents
            WHERE AgentId = @AgentId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new { AgentId = agentId, UserId = userId });

        return rowsAffected > 0;
    }

    public async Task<bool> UpdateAgentStatusAsync(Guid agentId, AgentStatus status, string userId)
    {
        const string sql = @"
            UPDATE dbo.Agents
            SET Status = @Status, LastHeartbeat = @LastHeartbeat
            WHERE AgentId = @AgentId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            Status = (int)status,
            LastHeartbeat = DateTime.UtcNow,
            AgentId = agentId,
            UserId = userId
        });

        return rowsAffected > 0;
    }

    public async Task<AgentDto?> GetAgentByConnectionIdAsync(string connectionId)
    {
        const string sql = @"
            SELECT AgentId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, RegisteredAt, LastHeartbeat, Status
            FROM dbo.Agents
            WHERE ConnectionId = @ConnectionId;";

        using var connection = new SqlConnection(_connectionString);
        var result = await connection.QueryFirstOrDefaultAsync(sql, new { ConnectionId = connectionId });

        return result != null ? MapToAgentDto(result) : null;
    }

    private static AgentDto MapToAgentDto(dynamic row)
    {
        var capabilities = string.IsNullOrEmpty(row.Capabilities)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<string[]>(row.Capabilities) ?? Array.Empty<string>();

        var configuration = string.IsNullOrEmpty(row.Configuration)
            ? null
            : JsonSerializer.Deserialize<Dictionary<string, object>>(row.Configuration);

        return new AgentDto(
            row.AgentId,
            row.AgentName,
            row.AgentType,
            row.ConnectionId,
            capabilities,
            row.Description,
            configuration,
            row.RegisteredAt,
            row.LastHeartbeat,
            (AgentStatus)row.Status
        );
    }

    public async Task<Guid> CreateAsync(AgentDto entity, string userId)
    {
        const string sql = @"
            INSERT INTO dbo.Agents (AgentId, UserId, AgentName, AgentType, ConnectionId, Capabilities, Description, Configuration, Status, RegisteredAt, LastHeartbeat)
            VALUES (@AgentId, @UserId, @AgentName, @AgentType, @ConnectionId, @Capabilities, @Description, @Configuration, @Status, @RegisteredAt, @LastHeartbeat);";

        var agentId = entity.AgentId != Guid.Empty ? entity.AgentId : Guid.NewGuid();
        var now = DateTime.UtcNow;

        using var connection = new SqlConnection(_connectionString);
        await connection.ExecuteAsync(sql, new
        {
            AgentId = agentId,
            UserId = userId,
            AgentName = entity.AgentName,
            AgentType = entity.AgentType,
            ConnectionId = entity.ConnectionId,
            Capabilities = JsonSerializer.Serialize(entity.Capabilities),
            Description = entity.Description,
            Configuration = entity.Configuration != null ? JsonSerializer.Serialize(entity.Configuration) : null,
            Status = (int)entity.Status,
            RegisteredAt = now,
            LastHeartbeat = now
        });

        return agentId;
    }

    public async Task<bool> UpdateAsync(AgentDto entity, string userId)
    {
        const string sql = @"
            UPDATE dbo.Agents
            SET AgentName = @AgentName,
                AgentType = @AgentType,
                ConnectionId = @ConnectionId,
                Capabilities = @Capabilities,
                Description = @Description,
                Configuration = @Configuration,
                Status = @Status,
                LastHeartbeat = @LastHeartbeat
            WHERE AgentId = @AgentId AND UserId = @UserId;";

        using var connection = new SqlConnection(_connectionString);
        var rowsAffected = await connection.ExecuteAsync(sql, new
        {
            entity.AgentId,
            UserId = userId,
            entity.AgentName,
            entity.AgentType,
            entity.ConnectionId,
            Capabilities = JsonSerializer.Serialize(entity.Capabilities),
            entity.Description,
            Configuration = entity.Configuration != null ? JsonSerializer.Serialize(entity.Configuration) : null,
            Status = (int)entity.Status,
            LastHeartbeat = DateTime.UtcNow
        });

        return rowsAffected > 0;
    }
}