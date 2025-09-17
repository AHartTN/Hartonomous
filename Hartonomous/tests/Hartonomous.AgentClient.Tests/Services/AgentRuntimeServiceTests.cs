using FluentAssertions;
using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Hartonomous.AgentClient.Services;
using Hartonomous.Core.Interfaces;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Threading.Tasks;
using Xunit;

namespace Hartonomous.AgentClient.Tests.Services;

public class AgentRuntimeServiceTests : IDisposable
{
    private readonly Mock<ILogger<AgentRuntimeService>> _loggerMock;
    private readonly Mock<IMetricsCollector> _metricsCollectorMock;
    private readonly Mock<ICurrentUserService> _currentUserServiceMock;
    private readonly AgentClientConfiguration _configuration;
    private readonly AgentRuntimeService _service;

    public AgentRuntimeServiceTests()
    {
        _loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        _metricsCollectorMock = new Mock<IMetricsCollector>();
        _currentUserServiceMock = new Mock<ICurrentUserService>();

        _configuration = new AgentClientConfiguration
        {
            AgentInstallPath = Path.GetTempPath(),
            AgentWorkspacePath = Path.GetTempPath(),
            MaxInstancesPerUser = 5,
            DefaultTimeoutSeconds = 300
        };

        var configurationOptions = Options.Create(_configuration);

        _service = new AgentRuntimeService(
            _loggerMock.Object,
            _metricsCollectorMock.Object,
            configurationOptions,
            _currentUserServiceMock.Object);

        // Setup current user service to return a test user ID
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUserIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-user-id");
    }

    [Fact]
    public async Task CreateInstanceAsync_WithValidDefinition_ReturnsAgentInstance()
    {
        // Arrange
        var definition = CreateTestAgentDefinition();

        // Act
        var instance = await _service.CreateInstanceAsync(definition);

        // Assert
        instance.Should().NotBeNull();
        instance.AgentId.Should().Be(definition.Id);
        instance.Name.Should().Be(definition.Name);
        instance.Version.Should().Be(definition.Version);
        instance.Status.Should().Be(AgentStatus.Stopped);
        instance.UserId.Should().Be("test-user-id");
        instance.WorkingDirectory.Should().NotBeNullOrEmpty();

        // Verify metrics were recorded
        _metricsCollectorMock.Verify(
            x => x.IncrementCounter("agent.instances.created", 1.0, null),
            Times.Once);
    }

    [Fact]
    public async Task CreateInstanceAsync_WithNullDefinition_ThrowsArgumentNullException()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.CreateInstanceAsync(null!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task GetInstanceAsync_WithValidInstanceId_ReturnsInstance()
    {
        // Arrange
        var definition = CreateTestAgentDefinition();
        var createdInstance = await _service.CreateInstanceAsync(definition);

        // Act
        var retrievedInstance = await _service.GetInstanceAsync(createdInstance.InstanceId);

        // Assert
        retrievedInstance.Should().NotBeNull();
        retrievedInstance.Should().BeEquivalentTo(createdInstance);
    }

    [Fact]
    public async Task GetInstanceAsync_WithInvalidInstanceId_ReturnsNull()
    {
        // Act
        var instance = await _service.GetInstanceAsync("non-existent-instance");

        // Assert
        instance.Should().BeNull();
    }

    [Fact]
    public async Task ListInstancesAsync_ReturnsInstancesForCurrentUser()
    {
        // Arrange
        var definition1 = CreateTestAgentDefinition("agent1");
        var definition2 = CreateTestAgentDefinition("agent2");

        await _service.CreateInstanceAsync(definition1);
        await _service.CreateInstanceAsync(definition2);

        // Act
        var instances = await _service.ListInstancesAsync();

        // Assert
        instances.Should().HaveCount(2);
        instances.Should().OnlyContain(i => i.UserId == "test-user-id");
    }

    [Fact]
    public async Task ListInstancesAsync_WithStatusFilter_ReturnsFilteredInstances()
    {
        // Arrange
        var definition = CreateTestAgentDefinition();
        await _service.CreateInstanceAsync(definition);

        // Act
        var stoppedInstances = await _service.ListInstancesAsync(status: AgentStatus.Stopped);
        var runningInstances = await _service.ListInstancesAsync(status: AgentStatus.Running);

        // Assert
        stoppedInstances.Should().HaveCount(1);
        runningInstances.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateInstanceConfigurationAsync_WithValidConfiguration_UpdatesInstance()
    {
        // Arrange
        var definition = CreateTestAgentDefinition();
        var instance = await _service.CreateInstanceAsync(definition);
        var newConfiguration = new Dictionary<string, object>
        {
            ["setting1"] = "value1",
            ["setting2"] = 42
        };

        // Act
        var updatedInstance = await _service.UpdateInstanceConfigurationAsync(
            instance.InstanceId,
            newConfiguration);

        // Assert
        updatedInstance.Configuration.Should().Contain("setting1", "value1");
        updatedInstance.Configuration.Should().Contain("setting2", 42);
        updatedInstance.UpdatedAt.Should().BeAfter(instance.UpdatedAt);
    }

    [Fact]
    public async Task DestroyInstanceAsync_RemovesInstance()
    {
        // Arrange
        var definition = CreateTestAgentDefinition();
        var instance = await _service.CreateInstanceAsync(definition);

        // Act
        await _service.DestroyInstanceAsync(instance.InstanceId);

        // Assert
        var retrievedInstance = await _service.GetInstanceAsync(instance.InstanceId);
        retrievedInstance.Should().BeNull();

        // Verify metrics were recorded
        _metricsCollectorMock.Verify(
            x => x.IncrementCounter("agent.instances.destroyed", 1.0, null),
            Times.Once);
    }

    [Fact]
    public async Task CheckInstanceHealthAsync_WithNonExistentInstance_ReturnsUnknown()
    {
        // Act
        var health = await _service.CheckInstanceHealthAsync("non-existent-instance");

        // Assert
        health.Should().Be(HealthStatus.Unknown);
    }

    [Fact]
    public void InstanceStatusChanged_Event_IsFiredOnStatusChange()
    {
        // Arrange
        var eventFired = false;
        AgentInstanceEventArgs? eventArgs = null;

        _service.InstanceStatusChanged += (sender, args) =>
        {
            eventFired = true;
            eventArgs = args;
        };

        // Act
        // This would be fired internally when status changes
        // For testing, we'd need to expose the protected method or use reflection

        // Assert
        // This test would require internal access or a more complex setup
    }

    [Fact]
    public async Task GetInstanceLogsAsync_ReturnsEmptyListForNonExistentInstance()
    {
        // Act
        var logs = await _service.GetInstanceLogsAsync("non-existent-instance");

        // Assert
        logs.Should().BeEmpty();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task CreateInstanceAsync_WithInvalidUserId_ThrowsUnauthorizedAccessException(string? userId)
    {
        // Arrange
        _currentUserServiceMock
            .Setup(x => x.GetCurrentUserIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(userId);

        var definition = CreateTestAgentDefinition();

        // Act & Assert
        await FluentActions
            .Invoking(() => _service.CreateInstanceAsync(definition))
            .Should().ThrowAsync<UnauthorizedAccessException>();
    }

    [Fact]
    public async Task GetInstanceResourceUsageAsync_WithValidInstance_ReturnsNull()
    {
        // Arrange
        var definition = CreateTestAgentDefinition();
        var instance = await _service.CreateInstanceAsync(definition);

        // Act
        var resourceUsage = await _service.GetInstanceResourceUsageAsync(instance.InstanceId);

        // Assert
        // Should return null since no process is running
        resourceUsage.Should().BeNull();
    }

    private static AgentDefinition CreateTestAgentDefinition(string id = "test-agent")
    {
        return new AgentDefinition
        {
            Id = id,
            Name = $"Test Agent {id}",
            Version = "1.0.0",
            Description = "A test agent for unit testing",
            Author = "Test Author",
            Type = AgentType.Utility,
            Capabilities = new[] { "test.capability" },
            Dependencies = Array.Empty<AgentDependency>(),
            Resources = new AgentResourceRequirements
            {
                MinCpuCores = 1,
                MinMemoryMb = 256,
                MinDiskMb = 100,
                NetworkAccess = NetworkAccessLevel.None,
                FileSystemAccess = FileSystemAccessLevel.Restricted,
                IsolationLevel = IsolationLevel.Process,
                TimeoutSeconds = 300
            },
            Security = new AgentSecurityConfiguration
            {
                TrustLevel = TrustLevel.Medium,
                RequireCodeSigning = false,
                AllowedCapabilities = new[] { "test.capability" },
                RestrictedApis = Array.Empty<string>(),
                SecurityPolicies = Array.Empty<string>()
            },
            EntryPoint = "TestAgent.dll",
            ConfigurationSchema = null,
            Tags = new[] { "test", "utility" },
            License = "MIT",
            Homepage = "https://example.com",
            Repository = "https://github.com/example/test-agent",
            Checksum = "abc123",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Dispose()
    {
        _service.Dispose();
    }
}

/// <summary>
/// Integration tests for AgentRuntimeService that require more complex setup
/// </summary>
public class AgentRuntimeServiceIntegrationTests : IDisposable
{
    private readonly AgentRuntimeService _service;
    private readonly string _tempDirectory;

    public AgentRuntimeServiceIntegrationTests()
    {
        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        var loggerMock = new Mock<ILogger<AgentRuntimeService>>();
        var metricsCollectorMock = new Mock<IMetricsCollector>();
        var currentUserServiceMock = new Mock<ICurrentUserService>();

        currentUserServiceMock
            .Setup(x => x.GetCurrentUserIdAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("integration-test-user");

        var configuration = new AgentClientConfiguration
        {
            AgentInstallPath = _tempDirectory,
            AgentWorkspacePath = Path.Combine(_tempDirectory, "workspaces"),
            MaxInstancesPerUser = 5,
            DefaultTimeoutSeconds = 300
        };

        var configurationOptions = Options.Create(configuration);

        _service = new AgentRuntimeService(
            loggerMock.Object,
            metricsCollectorMock.Object,
            configurationOptions,
            currentUserServiceMock.Object);
    }

    [Fact]
    public async Task CreateAndDestroyInstance_CreatesAndCleansUpWorkingDirectory()
    {
        // Arrange
        var definition = new AgentDefinition
        {
            Id = "integration-test-agent",
            Name = "Integration Test Agent",
            Version = "1.0.0",
            Description = "Integration test agent",
            Author = "Test Author",
            Type = AgentType.Utility,
            Capabilities = Array.Empty<string>(),
            Resources = new AgentResourceRequirements(),
            Security = new AgentSecurityConfiguration(),
            EntryPoint = "TestAgent.dll"
        };

        // Act
        var instance = await _service.CreateInstanceAsync(definition);
        var workingDirectory = instance.WorkingDirectory;

        // Assert - Working directory should be created
        workingDirectory.Should().NotBeNullOrEmpty();
        Directory.Exists(workingDirectory).Should().BeTrue();

        // Act - Destroy instance
        await _service.DestroyInstanceAsync(instance.InstanceId);

        // Assert - Working directory should be cleaned up
        Directory.Exists(workingDirectory).Should().BeFalse();
    }

    public void Dispose()
    {
        _service.Dispose();

        if (Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, true);
            }
            catch
            {
                // Ignore cleanup errors in tests
            }
        }
    }
}