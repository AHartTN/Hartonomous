using FluentAssertions;
using Hartonomous.AgentClient.Interfaces;
using System;
using System.IO;
using System.Threading;
using System.Collections.Generic;
using Hartonomous.AgentClient.Models;
using Hartonomous.AgentClient.Services;
using Hartonomous.Infrastructure.Observability.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using System.Text.Json;
using System.Threading.Tasks;
using Xunit;

namespace Hartonomous.AgentClient.Tests.Services;

public class AgentLoaderServiceTests : IDisposable
{
    private readonly Mock<ILogger<AgentLoaderService>> _loggerMock;
    private readonly Mock<IMetricsCollector> _metricsCollectorMock;
    private readonly Mock<ICapabilityRegistry> _capabilityRegistryMock;
    private readonly AgentClientConfiguration _configuration;
    private readonly AgentLoaderService _service;
    private readonly string _tempDirectory;

    public AgentLoaderServiceTests()
    {
        _loggerMock = new Mock<ILogger<AgentLoaderService>>();
        _metricsCollectorMock = new Mock<IMetricsCollector>();
        _capabilityRegistryMock = new Mock<ICapabilityRegistry>();

        _tempDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(_tempDirectory);

        _configuration = new AgentClientConfiguration
        {
            AgentInstallPath = _tempDirectory,
            AgentWorkspacePath = Path.Combine(_tempDirectory, "workspaces")
        };

        var configurationOptions = Options.Create(_configuration);

        _service = new AgentLoaderService(
            _loggerMock.Object,
            _metricsCollectorMock.Object,
            _capabilityRegistryMock.Object,
            configurationOptions);
    }

    [Fact]
    public async Task ValidateAgentAsync_WithNonExistentPath_ReturnsInvalidResult()
    {
        // Arrange
        var nonExistentPath = Path.Combine(_tempDirectory, "non-existent-agent");

        // Act
        var result = await _service.ValidateAgentAsync(nonExistentPath);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(error => error.Contains("not found"));
    }

    [Fact]
    public async Task ValidateAgentAsync_WithValidManifest_ReturnsValidResult()
    {
        // Arrange
        var agentPath = CreateTestAgentDirectory();

        // Act
        var result = await _service.ValidateAgentAsync(agentPath);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAgentAsync_WithInvalidManifest_ReturnsInvalidResult()
    {
        // Arrange
        var agentPath = CreateTestAgentDirectory(withInvalidManifest: true);

        // Act
        var result = await _service.ValidateAgentAsync(agentPath);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task GetLoadedAgentsAsync_InitiallyEmpty_ReturnsEmptyList()
    {
        // Act
        var agents = await _service.GetLoadedAgentsAsync();

        // Assert
        agents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLoadedAgentAsync_WithNonExistentAgent_ReturnsNull()
    {
        // Act
        var agent = await _service.GetLoadedAgentAsync("non-existent-agent");

        // Assert
        agent.Should().BeNull();
    }

    [Fact]
    public void IsAgentLoaded_WithNonExistentAgent_ReturnsFalse()
    {
        // Act
        var isLoaded = _service.IsAgentLoaded("non-existent-agent");

        // Assert
        isLoaded.Should().BeFalse();
    }

    [Fact]
    public async Task GetAgentCapabilitiesAsync_WithNonExistentAgent_ReturnsEmptyList()
    {
        // Act
        var capabilities = await _service.GetAgentCapabilitiesAsync("non-existent-agent");

        // Assert
        capabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task RegisterAgentCapabilitiesAsync_WithNonExistentAgent_ReturnsEmptyList()
    {
        // Act
        var registrations = await _service.RegisterAgentCapabilitiesAsync("non-existent-agent");

        // Assert
        registrations.Should().BeEmpty();
    }

    [Fact]
    public async Task UnregisterAgentCapabilitiesAsync_WithNonExistentAgent_DoesNotThrow()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.UnregisterAgentCapabilitiesAsync("non-existent-agent"))
            .Should().NotThrowAsync();
    }

    [Fact]
    public void GetLoadContext_WithNonExistentAgent_ReturnsNull()
    {
        // Act
        var loadContext = _service.GetLoadContext("non-existent-agent");

        // Assert
        loadContext.Should().BeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task LoadAgentAsync_WithInvalidPath_ThrowsArgumentNullException(string? agentPath)
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.LoadAgentAsync(agentPath!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task UnloadAgentAsync_WithInvalidAgentId_ThrowsArgumentNullException(string? agentId)
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.UnloadAgentAsync(agentId!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task UnloadAgentAsync_WithNonExistentAgent_DoesNotThrow()
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.UnloadAgentAsync("non-existent-agent"))
            .Should().NotThrowAsync();
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task ValidateAgentAsync_WithInvalidPath_ThrowsArgumentNullException(string? agentPath)
    {
        // Act & Assert
        await FluentActions
            .Invoking(() => _service.ValidateAgentAsync(agentPath!))
            .Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public void AgentLoaded_Event_IsDefinedAndCanBeSubscribed()
    {
        // Arrange
        var eventFired = false;

        // Act
        _service.AgentLoaded += (sender, args) => eventFired = true;

        // Assert
        // Event subscription should not throw
        eventFired.Should().BeFalse(); // Not fired yet
    }

    [Fact]
    public void AgentUnloaded_Event_IsDefinedAndCanBeSubscribed()
    {
        // Arrange
        var eventFired = false;

        // Act
        _service.AgentUnloaded += (sender, args) => eventFired = true;

        // Assert
        // Event subscription should not throw
        eventFired.Should().BeFalse(); // Not fired yet
    }

    [Fact]
    public void AgentLoadFailed_Event_IsDefinedAndCanBeSubscribed()
    {
        // Arrange
        var eventFired = false;

        // Act
        _service.AgentLoadFailed += (sender, args) => eventFired = true;

        // Assert
        // Event subscription should not throw
        eventFired.Should().BeFalse(); // Not fired yet
    }

    [Fact]
    public async Task ValidateAgentAsync_SetsValidatorVersion()
    {
        // Arrange
        var agentPath = CreateTestAgentDirectory();

        // Act
        var result = await _service.ValidateAgentAsync(agentPath);

        // Assert
        result.ValidatorVersion.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ValidateAgentAsync_SetsValidatedAtTimestamp()
    {
        // Arrange
        var agentPath = CreateTestAgentDirectory();
        var beforeValidation = DateTimeOffset.UtcNow;

        // Act
        var result = await _service.ValidateAgentAsync(agentPath);

        // Assert
        var afterValidation = DateTimeOffset.UtcNow;
        result.ValidatedAt.Should().BeAfter(beforeValidation).And.BeBefore(afterValidation);
    }

    [Fact]
    public async Task ValidateAgentAsync_WithMissingEntryPoint_ReturnsValidationError()
    {
        // Arrange
        var agentPath = CreateTestAgentDirectory();
        // Don't create the assembly file, so entry point validation fails

        // Act
        var result = await _service.ValidateAgentAsync(agentPath);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.Contains("assembly not found") || error.Contains("entry point"));
    }

    private string CreateTestAgentDirectory(bool withInvalidManifest = false)
    {
        var agentId = Guid.NewGuid().ToString();
        var agentPath = Path.Combine(_tempDirectory, agentId);
        Directory.CreateDirectory(agentPath);

        // Create agent manifest
        var manifest = CreateTestAgentDefinition(agentId);

        if (withInvalidManifest)
        {
            // Create invalid manifest (missing required fields)
            var invalidManifest = new
            {
                // Missing required fields like Id, Name, Version, etc.
                Description = "Invalid test agent"
            };
            var invalidJson = JsonSerializer.Serialize(invalidManifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(agentPath, "agent.json"), invalidJson);
        }
        else
        {
            var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(Path.Combine(agentPath, "agent.json"), json);

            // Create a dummy assembly file
            var assemblyPath = Path.Combine(agentPath, manifest.EntryPoint);
            File.WriteAllText(assemblyPath, "// Dummy assembly file for testing");
        }

        return agentPath;
    }

    private static AgentDefinition CreateTestAgentDefinition(string id)
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
            Tags = new[] { "test", "utility" },
            License = "MIT"
        };
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