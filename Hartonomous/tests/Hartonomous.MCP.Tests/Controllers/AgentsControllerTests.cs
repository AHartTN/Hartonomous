using Hartonomous.MCP.Controllers;
using Hartonomous.Core.Shared.DTOs;
using Hartonomous.Core.Shared.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Moq;
using System.Security.Claims;

namespace Hartonomous.MCP.Tests.Controllers;

public class AgentsControllerTests
{
    private readonly Mock<IAgentRepository> _mockRepository;
    private readonly Mock<ILogger<AgentsController>> _mockLogger;
    private readonly AgentsController _controller;
    private readonly string _testUserId = "test-user-123";

    public AgentsControllerTests()
    {
        _mockRepository = new Mock<IAgentRepository>();
        _mockLogger = new Mock<ILogger<AgentsController>>();
        _controller = new AgentsController(_mockRepository.Object, _mockLogger.Object);

        // Setup user context
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, _testUserId)
        };
        var identity = new ClaimsIdentity(claims);
        var principal = new ClaimsPrincipal(identity);

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = principal
            }
        };
    }

    [Fact]
    public async Task GetAgents_ShouldReturnOk_WithAgentList()
    {
        // Arrange
        var agents = new List<AgentDto>
        {
            new(Guid.NewGuid(), "Agent1", "CodeGenerator", "conn-1", new[] { "generate-code" }, null, null, DateTime.UtcNow, DateTime.UtcNow, AgentStatus.Online),
            new(Guid.NewGuid(), "Agent2", "CodeAnalyzer", "conn-2", new[] { "analyze-code" }, null, null, DateTime.UtcNow, DateTime.UtcNow, AgentStatus.Idle)
        };

        _mockRepository.Setup(r => r.GetAgentsByUserAsync(_testUserId))
                      .ReturnsAsync(agents);

        // Act
        var result = await _controller.GetAgents();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedAgents = Assert.IsAssignableFrom<IEnumerable<AgentDto>>(okResult.Value);
        Assert.Equal(2, returnedAgents.Count());
    }

    [Fact]
    public async Task GetAgent_ShouldReturnOk_WhenAgentExists()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var agent = new AgentDto(agentId, "TestAgent", "CodeGenerator", "conn-1", new[] { "generate-code" }, null, null, DateTime.UtcNow, DateTime.UtcNow, AgentStatus.Online);

        _mockRepository.Setup(r => r.GetAgentByIdAsync(agentId, _testUserId))
                      .ReturnsAsync(agent);

        // Act
        var result = await _controller.GetAgent(agentId);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedAgent = Assert.IsType<AgentDto>(okResult.Value);
        Assert.Equal(agentId, returnedAgent.AgentId);
    }

    [Fact]
    public async Task GetAgent_ShouldReturnNotFound_WhenAgentDoesNotExist()
    {
        // Arrange
        var agentId = Guid.NewGuid();

        _mockRepository.Setup(r => r.GetAgentByIdAsync(agentId, _testUserId))
                      .ReturnsAsync((AgentDto?)null);

        // Act
        var result = await _controller.GetAgent(agentId);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task DiscoverAgents_ShouldReturnOk_WithDiscoveryResponse()
    {
        // Arrange
        var request = new AgentDiscoveryRequest("CodeGenerator", new[] { "generate-code" });
        var agents = new List<AgentDto>
        {
            new(Guid.NewGuid(), "Agent1", "CodeGenerator", "conn-1", new[] { "generate-code" }, null, null, DateTime.UtcNow, DateTime.UtcNow, AgentStatus.Online)
        };

        _mockRepository.Setup(r => r.DiscoverAgentsAsync(request, _testUserId))
                      .ReturnsAsync(agents);

        // Act
        var result = await _controller.DiscoverAgents(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<AgentDiscoveryResponse>(okResult.Value);
        Assert.Single(response.AvailableAgents);
    }

    [Fact]
    public async Task UpdateAgentStatus_ShouldReturnNoContent_WhenSuccessful()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var request = new UpdateAgentStatusRequest(AgentStatus.Busy);

        _mockRepository.Setup(r => r.UpdateAgentStatusAsync(agentId, AgentStatus.Busy, _testUserId))
                      .ReturnsAsync(true);

        // Act
        var result = await _controller.UpdateAgentStatus(agentId, request);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UpdateAgentStatus_ShouldReturnNotFound_WhenAgentNotFound()
    {
        // Arrange
        var agentId = Guid.NewGuid();
        var request = new UpdateAgentStatusRequest(AgentStatus.Busy);

        _mockRepository.Setup(r => r.UpdateAgentStatusAsync(agentId, AgentStatus.Busy, _testUserId))
                      .ReturnsAsync(false);

        // Act
        var result = await _controller.UpdateAgentStatus(agentId, request);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UnregisterAgent_ShouldReturnNoContent_WhenSuccessful()
    {
        // Arrange
        var agentId = Guid.NewGuid();

        _mockRepository.Setup(r => r.UnregisterAgentAsync(agentId, _testUserId))
                      .ReturnsAsync(true);

        // Act
        var result = await _controller.UnregisterAgent(agentId);

        // Assert
        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task UnregisterAgent_ShouldReturnNotFound_WhenAgentNotFound()
    {
        // Arrange
        var agentId = Guid.NewGuid();

        _mockRepository.Setup(r => r.UnregisterAgentAsync(agentId, _testUserId))
                      .ReturnsAsync(false);

        // Act
        var result = await _controller.UnregisterAgent(agentId);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result);
    }
}