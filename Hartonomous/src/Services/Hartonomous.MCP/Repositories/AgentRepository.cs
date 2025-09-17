using Dapper;
using Hartonomous.Core.Shared.DTOs;
using Hartonomous.Core.Shared.Interfaces;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;
using System.Text.Json;

namespace Hartonomous.MCP.Repositories;

/// <summary>
/// Repository implementation for agent management using Dapper
/// </summary>
public class AgentRepository : IAgentRepository
{
    private readonly string _connectionString;

    public AgentRepository(IConfiguration configuration)
    {
        _connectionString = configuration.GetConnectionString("DefaultConnection") ??
            throw new ArgumentNullException(nameof(configuration), "DefaultConnection is required");
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

    public Task<Guid> CreateAsync(AgentDto entity, string userId)
    {
        throw new NotImplementedException();
    }

    public Task<bool> UpdateAsync(AgentDto entity, string userId)
    {
        throw new NotImplementedException();
    }
}