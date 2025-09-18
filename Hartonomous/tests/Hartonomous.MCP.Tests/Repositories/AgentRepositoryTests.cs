using Hartonomous.Core.DTOs;
using Hartonomous.MCP.Repositories;
using Microsoft.Data.Sqlite;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Data;

namespace Hartonomous.MCP.Tests.Repositories;

public class AgentRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly AgentRepository _repository;
    private readonly string _testUserId = $"test-user-{Guid.NewGuid()}";

    public AgentRepositoryTests()
    {
        // Use localhost SQL Server with test database for testing
        var connectionString = "Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;";

        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = connectionString
            })
            .Build();

        _repository = new AgentRepository(configuration);

        InitializeDatabase();
    }

    private void InitializeDatabase()
    {
        // Clean up any existing test data for this user
        using var connection = new SqlConnection("Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;");
        connection.Open();
        using var command = new SqlCommand("DELETE FROM dbo.Agents WHERE UserId = @UserId", connection);
        command.Parameters.AddWithValue("@UserId", _testUserId);
        command.ExecuteNonQuery();
    }

    [Fact]
    public async Task RegisterAgentAsync_ShouldReturnAgentId_WhenValidRequest()
    {
        // Arrange
        var request = new AgentRegistrationRequest(
            "TestAgent",
            "CodeGenerator",
            new[] { "generate-code", "analyze-code" },
            "Test agent for code generation",
            new Dictionary<string, object> { { "language", "csharp" } }
        );
        var connectionId = "conn-123";

        // Act
        var agentId = await _repository.RegisterAgentAsync(request, connectionId, _testUserId);

        // Assert
        Assert.NotEqual(Guid.Empty, agentId);
    }

    [Fact]
    public async Task GetAgentByIdAsync_ShouldReturnAgent_WhenExists()
    {
        // Arrange
        var request = new AgentRegistrationRequest(
            "TestAgent",
            "CodeGenerator",
            new[] { "generate-code", "analyze-code" },
            "Test agent for code generation"
        );
        var connectionId = "conn-123";
        var agentId = await _repository.RegisterAgentAsync(request, connectionId, _testUserId);

        // Act
        var agent = await _repository.GetAgentByIdAsync(agentId, _testUserId);

        // Assert
        Assert.NotNull(agent);
        Assert.Equal(agentId, agent.AgentId);
        Assert.Equal(request.AgentName, agent.AgentName);
        Assert.Equal(request.AgentType, agent.AgentType);
        Assert.Equal(connectionId, agent.ConnectionId);
        Assert.Equal(AgentStatus.Online, agent.Status);
        Assert.Contains("generate-code", agent.Capabilities);
        Assert.Contains("analyze-code", agent.Capabilities);
    }

    [Fact]
    public async Task GetAgentByIdAsync_ShouldReturnNull_WhenNotExists()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var agent = await _repository.GetAgentByIdAsync(nonExistentId, _testUserId);

        // Assert
        Assert.Null(agent);
    }

    [Fact]
    public async Task GetAgentByIdAsync_ShouldReturnNull_WhenDifferentUser()
    {
        // Arrange
        var request = new AgentRegistrationRequest(
            "TestAgent",
            "CodeGenerator",
            new[] { "generate-code" }
        );
        var connectionId = "conn-123";
        var agentId = await _repository.RegisterAgentAsync(request, connectionId, _testUserId);

        // Act
        var agent = await _repository.GetAgentByIdAsync(agentId, "different-user");

        // Assert
        Assert.Null(agent);
    }

    [Fact]
    public async Task UpdateAgentHeartbeatAsync_ShouldUpdateStatusAndHeartbeat()
    {
        // Arrange
        var request = new AgentRegistrationRequest("TestAgent", "CodeGenerator", new[] { "generate-code" });
        var connectionId = "conn-123";
        var agentId = await _repository.RegisterAgentAsync(request, connectionId, _testUserId);
        var metrics = new Dictionary<string, object> { { "cpu", 75.5 }, { "memory", 512 } };

        // Act
        var result = await _repository.UpdateAgentHeartbeatAsync(agentId, AgentStatus.Busy, _testUserId, metrics);

        // Assert
        Assert.True(result);

        var agent = await _repository.GetAgentByIdAsync(agentId, _testUserId);
        Assert.NotNull(agent);
        Assert.Equal(AgentStatus.Busy, agent.Status);
    }

    [Fact]
    public async Task DiscoverAgentsAsync_ShouldReturnMatchingAgents()
    {
        // Arrange
        var agent1 = new AgentRegistrationRequest("Agent1", "CodeGenerator", new[] { "generate-code" });
        var agent2 = new AgentRegistrationRequest("Agent2", "CodeAnalyzer", new[] { "analyze-code" });
        var agent3 = new AgentRegistrationRequest("Agent3", "CodeGenerator", new[] { "generate-code", "optimize-code" });

        await _repository.RegisterAgentAsync(agent1, "conn-1", _testUserId);
        await _repository.RegisterAgentAsync(agent2, "conn-2", _testUserId);
        await _repository.RegisterAgentAsync(agent3, "conn-3", _testUserId);

        var request = new AgentDiscoveryRequest("CodeGenerator", new[] { "generate-code" });

        // Act
        var agents = await _repository.DiscoverAgentsAsync(request, _testUserId);

        // Assert
        var agentList = agents.ToList();
        Assert.Equal(2, agentList.Count);
        Assert.All(agentList, a => Assert.Equal("CodeGenerator", a.AgentType));
        Assert.All(agentList, a => Assert.Contains("generate-code", a.Capabilities));
    }

    [Fact]
    public async Task UnregisterAgentAsync_ShouldRemoveAgent()
    {
        // Arrange
        var request = new AgentRegistrationRequest("TestAgent", "CodeGenerator", new[] { "generate-code" });
        var connectionId = "conn-123";
        var agentId = await _repository.RegisterAgentAsync(request, connectionId, _testUserId);

        // Act
        var result = await _repository.UnregisterAgentAsync(agentId, _testUserId);

        // Assert
        Assert.True(result);

        var agent = await _repository.GetAgentByIdAsync(agentId, _testUserId);
        Assert.Null(agent);
    }

    [Fact]
    public async Task UpdateAgentConnectionAsync_ShouldUpdateConnectionId()
    {
        // Arrange
        var request = new AgentRegistrationRequest("TestAgent", "CodeGenerator", new[] { "generate-code" });
        var oldConnectionId = "conn-123";
        var newConnectionId = "conn-456";
        var agentId = await _repository.RegisterAgentAsync(request, oldConnectionId, _testUserId);

        // Act
        var result = await _repository.UpdateAgentConnectionAsync(agentId, newConnectionId, _testUserId);

        // Assert
        Assert.True(result);

        var agent = await _repository.GetAgentByIdAsync(agentId, _testUserId);
        Assert.NotNull(agent);
        Assert.Equal(newConnectionId, agent.ConnectionId);
    }

    public void Dispose()
    {
        // Clean up test data
        using var connection = new SqlConnection("Server=localhost;Database=HartonomousDB;Trusted_Connection=true;MultipleActiveResultSets=true;TrustServerCertificate=true;");
        connection.Open();
        using var command = new SqlCommand("DELETE FROM dbo.Agents WHERE UserId = @UserId", connection);
        command.Parameters.AddWithValue("@UserId", _testUserId);
        command.ExecuteNonQuery();

        _connection?.Dispose();
    }
}