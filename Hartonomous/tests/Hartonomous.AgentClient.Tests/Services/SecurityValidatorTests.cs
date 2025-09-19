/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * Unit tests for SecurityValidator class.
 */

using System;
using System.IO;
using System.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using Hartonomous.AgentClient.Models;
using Hartonomous.AgentClient.Services;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Hartonomous.AgentClient.Tests.Services;

public class SecurityValidatorTests
{
    private readonly Mock<ILogger<SecurityValidator>> _mockLogger;
    private readonly SecurityValidationPolicy _defaultPolicy;
    private readonly SecurityValidator _securityValidator;

    public SecurityValidatorTests()
    {
        _mockLogger = new Mock<ILogger<SecurityValidator>>();
        _defaultPolicy = new SecurityValidationPolicy
        {
            RequireCodeSigning = true,
            RequireStrongName = false,
            RequireTrustedPublisher = false,
            CheckCertificateRevocation = false,
            ValidationMode = SecurityValidationMode.Strict
        };
        _securityValidator = new SecurityValidator(_mockLogger.Object, _defaultPolicy);
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithValidAssembly_ReturnsSuccessfulValidation()
    {
        // Arrange
        var tempAssemblyPath = CreateTestAssembly();
        var agentDefinition = CreateTestAgentDefinition();

        try
        {
            // Act
            var result = await _securityValidator.ValidateAgentSecurityAsync(tempAssemblyPath, agentDefinition);

            // Assert
            Assert.NotNull(result);
            Assert.Equal(tempAssemblyPath, result.AssemblyPath);
            Assert.Equal(agentDefinition.Id, result.AgentId);
            Assert.NotNull(result.FileHash);
            Assert.True(result.ValidationTimestamp <= DateTimeOffset.UtcNow);
        }
        finally
        {
            // Cleanup
            if (File.Exists(tempAssemblyPath))
                File.Delete(tempAssemblyPath);
        }
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithNonExistentFile_AddsSecurityIssue()
    {
        // Arrange
        var nonExistentPath = @"C:\NonExistent\Assembly.dll";
        var agentDefinition = CreateTestAgentDefinition();

        // Act
        var result = await _securityValidator.ValidateAgentSecurityAsync(nonExistentPath, agentDefinition);

        // Assert
        Assert.False(result.IsSecure);
        Assert.Contains(result.SecurityIssues, issue => issue.Contains("Assembly file not found"));
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithStrictPolicy_RequiresCodeSigning()
    {
        // Arrange
        var strictPolicy = new SecurityValidationPolicy
        {
            RequireCodeSigning = true,
            ValidationMode = SecurityValidationMode.Strict
        };
        var validator = new SecurityValidator(_mockLogger.Object, strictPolicy);
        var tempAssemblyPath = CreateTestAssembly();
        var agentDefinition = CreateTestAgentDefinition();

        try
        {
            // Act
            var result = await validator.ValidateAgentSecurityAsync(tempAssemblyPath, agentDefinition);

            // Assert - Should fail because test assembly is not code signed
            Assert.False(result.IsSecure);
            Assert.False(result.AuthenticodeValid);
        }
        finally
        {
            if (File.Exists(tempAssemblyPath))
                File.Delete(tempAssemblyPath);
        }
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithDevelopmentMode_IsPermissive()
    {
        // Arrange
        var developmentPolicy = new SecurityValidationPolicy
        {
            RequireCodeSigning = false,
            RequireStrongName = false,
            ValidationMode = SecurityValidationMode.Development
        };
        var validator = new SecurityValidator(_mockLogger.Object, developmentPolicy);
        var tempAssemblyPath = CreateTestAssembly();
        var agentDefinition = CreateTestAgentDefinition();

        try
        {
            // Act
            var result = await validator.ValidateAgentSecurityAsync(tempAssemblyPath, agentDefinition);

            // Assert - Should pass in development mode even without signing
            Assert.True(result.IsSecure);
        }
        finally
        {
            if (File.Exists(tempAssemblyPath))
                File.Delete(tempAssemblyPath);
        }
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithTrustLevelExceedsPolicy_AddsSecurityIssue()
    {
        // Arrange
        var restrictivePolicy = new SecurityValidationPolicy
        {
            MaxAllowedTrustLevel = TrustLevel.Low,
            RequireCodeSigning = false
        };
        var validator = new SecurityValidator(_mockLogger.Object, restrictivePolicy);
        var tempAssemblyPath = CreateTestAssembly();
        var agentDefinition = CreateTestAgentDefinition();
        agentDefinition = agentDefinition with
        {
            Security = agentDefinition.Security with { TrustLevel = TrustLevel.High }
        };

        try
        {
            // Act
            var result = await validator.ValidateAgentSecurityAsync(tempAssemblyPath, agentDefinition);

            // Assert
            Assert.False(result.IsSecure);
            Assert.Contains(result.SecurityIssues, issue => issue.Contains("trust level") && issue.Contains("exceeds policy maximum"));
        }
        finally
        {
            if (File.Exists(tempAssemblyPath))
                File.Delete(tempAssemblyPath);
        }
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithRestrictedCapabilities_AddsSecurityIssue()
    {
        // Arrange
        var restrictivePolicy = new SecurityValidationPolicy
        {
            RequireCodeSigning = false,
            RestrictedCapabilities = { "system.execute", "file.delete" }
        };
        var validator = new SecurityValidator(_mockLogger.Object, restrictivePolicy);
        var tempAssemblyPath = CreateTestAssembly();
        var agentDefinition = CreateTestAgentDefinition();
        agentDefinition = agentDefinition with
        {
            Capabilities = new[] { "data.read", "system.execute", "network.connect" }
        };

        try
        {
            // Act
            var result = await validator.ValidateAgentSecurityAsync(tempAssemblyPath, agentDefinition);

            // Assert
            Assert.False(result.IsSecure);
            Assert.Contains(result.SecurityIssues, issue => issue.Contains("restricted capabilities") && issue.Contains("system.execute"));
        }
        finally
        {
            if (File.Exists(tempAssemblyPath))
                File.Delete(tempAssemblyPath);
        }
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithHashMismatch_AddsSecurityIssue()
    {
        // Arrange
        var tempAssemblyPath = CreateTestAssembly();
        var agentDefinition = CreateTestAgentDefinition();
        agentDefinition = agentDefinition with { Checksum = "INVALID_HASH_VALUE" };

        try
        {
            // Act
            var result = await _securityValidator.ValidateAgentSecurityAsync(tempAssemblyPath, agentDefinition);

            // Assert
            Assert.False(result.IsSecure);
            Assert.Contains(result.SecurityIssues, issue => issue.Contains("File integrity check failed"));
            Assert.NotNull(result.FileHash);
        }
        finally
        {
            if (File.Exists(tempAssemblyPath))
                File.Delete(tempAssemblyPath);
        }
    }

    [Fact]
    public void ThrowIfValidationFailed_WithFailedValidation_ThrowsSecurityException()
    {
        // Arrange
        var failedResult = new SecurityValidationResult
        {
            IsSecure = false,
            AgentId = "test-agent",
            SecurityIssues = { "Critical security issue" }
        };
        var policy = new SecurityValidationPolicy();

        // Act & Assert
        var exception = Assert.Throws<SecurityException>(() =>
            SecurityValidator.ThrowIfValidationFailed(failedResult, policy));

        Assert.Contains("Security validation failed", exception.Message);
        Assert.Contains("test-agent", exception.Message);
    }

    [Fact]
    public void ThrowIfValidationFailed_WithWarningsAndFailOnWarnings_ThrowsSecurityException()
    {
        // Arrange
        var result = new SecurityValidationResult
        {
            IsSecure = true,
            AgentId = "test-agent",
            Warnings = { "Security warning" }
        };
        var policy = new SecurityValidationPolicy { FailOnWarnings = true };

        // Act & Assert
        var exception = Assert.Throws<SecurityException>(() =>
            SecurityValidator.ThrowIfValidationFailed(result, policy));

        Assert.Contains("warnings", exception.Message);
    }

    [Theory]
    [InlineData(SecurityValidationMode.Strict)]
    [InlineData(SecurityValidationMode.Permissive)]
    [InlineData(SecurityValidationMode.Development)]
    public void SecurityValidationPolicy_ValidationModes_SetCorrectly(SecurityValidationMode mode)
    {
        // Arrange & Act
        var policy = new SecurityValidationPolicy { ValidationMode = mode };

        // Assert
        Assert.Equal(mode, policy.ValidationMode);
    }

    [Fact]
    public void SecurityValidationResult_DefaultValues_AreSetCorrectly()
    {
        // Arrange & Act
        var result = new SecurityValidationResult();

        // Assert
        Assert.False(result.IsSecure);
        Assert.False(result.AuthenticodeValid);
        Assert.False(result.StrongNameValid);
        Assert.False(result.IntegrityValid);
        Assert.False(result.PublisherTrusted);
        Assert.False(result.CertificateChainValid);
        Assert.Empty(result.SecurityIssues);
        Assert.Empty(result.Warnings);
        Assert.True(result.ValidationTimestamp <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task ValidateAgentSecurityAsync_WithNullArguments_ThrowsArgumentNullException()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _securityValidator.ValidateAgentSecurityAsync(null!, CreateTestAgentDefinition()));

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            _securityValidator.ValidateAgentSecurityAsync("test.dll", null!));
    }

    private static string CreateTestAssembly()
    {
        // Create a minimal test assembly file for testing
        var tempPath = Path.GetTempFileName();
        var assemblyBytes = new byte[]
        {
            0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00, // DOS header
            0x04, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0x00, 0x00
        };
        File.WriteAllBytes(tempPath, assemblyBytes);

        // Rename to .dll extension
        var dllPath = Path.ChangeExtension(tempPath, ".dll");
        File.Move(tempPath, dllPath);
        return dllPath;
    }

    private static AgentDefinition CreateTestAgentDefinition()
    {
        return new AgentDefinition
        {
            Id = "test-agent-001",
            Name = "Test Agent",
            Version = "1.0.0",
            Description = "Test agent for security validation",
            Author = "Test Author",
            Type = AgentType.Utility,
            Capabilities = new[] { "data.read", "compute.basic" },
            Resources = new AgentResourceRequirements
            {
                MinCpuCores = 1,
                MinMemoryMb = 256,
                NetworkAccess = NetworkAccessLevel.Local,
                FileSystemAccess = FileSystemAccessLevel.ReadOnly
            },
            Security = new AgentSecurityConfiguration
            {
                TrustLevel = TrustLevel.Medium,
                RequireCodeSigning = true
            },
            EntryPoint = "TestAgent.dll"
        };
    }
}