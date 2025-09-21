/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * Service registration extensions for Hartonomous Agent Client.
 */

using Hartonomous.AgentClient.Interfaces;
using Hartonomous.AgentClient.Models;
using Hartonomous.AgentClient.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hartonomous.AgentClient;

/// <summary>
/// Service collection extensions for Agent Client configuration
/// </summary>
public static class ServiceExtensions
{
    /// <summary>
    /// Adds Hartonomous Agent Client services to the service collection
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddHartonomousAgentClient(this IServiceCollection services, IConfiguration configuration)
    {
        // Configure options
        services.Configure<AgentClientConfiguration>(configuration.GetSection("AgentClient"));

        // Register security validation policy with default settings
        services.AddSingleton<SecurityValidationPolicy>(provider =>
        {
            var config = configuration.GetSection("AgentClient:Security");
            var policy = new SecurityValidationPolicy();

            // Apply configuration if available
            if (config.Exists())
            {
                config.Bind(policy);
            }
            else
            {
                // Default secure settings
                policy.RequireCodeSigning = true;
                policy.RequireStrongName = false;
                policy.RequireTrustedPublisher = false;
                policy.CheckCertificateRevocation = true;
                policy.ValidationMode = SecurityValidationMode.Strict;
                policy.MaxAllowedTrustLevel = TrustLevel.Medium;
                policy.MaxAllowedNetworkAccess = NetworkAccessLevel.Internet;
                policy.MaxAllowedFileSystemAccess = FileSystemAccessLevel.Restricted;

                // Add some default restricted capabilities
                policy.RestrictedCapabilities.AddRange(new[]
                {
                    "system.execute",
                    "file.delete",
                    "registry.write",
                    "network.raw"
                });
            }

            return policy;
        });

        // Register security validator
        services.AddScoped<SecurityValidator>();

        // Register agent services
        services.AddScoped<IAgentLoader, AgentLoaderService>();
        services.AddScoped<IAgentRuntime, AgentRuntimeService>();
        services.AddScoped<ITaskExecutor, TaskExecutorService>();

        return services;
    }

    /// <summary>
    /// Adds Hartonomous Agent Client services with a custom security policy
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <param name="securityPolicy">Custom security validation policy</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddHartonomousAgentClient(
        this IServiceCollection services,
        IConfiguration configuration,
        SecurityValidationPolicy securityPolicy)
    {
        // Configure options
        services.Configure<AgentClientConfiguration>(configuration.GetSection("AgentClient"));

        // Register the provided security policy
        services.AddSingleton(securityPolicy);

        // Register security validator
        services.AddScoped<SecurityValidator>();

        // Register agent services
        services.AddScoped<IAgentLoader, AgentLoaderService>();
        services.AddScoped<IAgentRuntime, AgentRuntimeService>();
        services.AddScoped<ITaskExecutor, TaskExecutorService>();

        return services;
    }

    /// <summary>
    /// Adds Hartonomous Agent Client services for development environments with relaxed security
    /// </summary>
    /// <param name="services">The service collection</param>
    /// <param name="configuration">Configuration instance</param>
    /// <returns>The service collection for chaining</returns>
    public static IServiceCollection AddHartonomousAgentClientDevelopment(this IServiceCollection services, IConfiguration configuration)
    {
        var developmentPolicy = new SecurityValidationPolicy
        {
            RequireCodeSigning = false,
            RequireStrongName = false,
            RequireTrustedPublisher = false,
            CheckCertificateRevocation = false,
            FailOnWarnings = false,
            ValidationMode = SecurityValidationMode.Development,
            MaxAllowedTrustLevel = TrustLevel.Full,
            MaxAllowedNetworkAccess = NetworkAccessLevel.Full,
            MaxAllowedFileSystemAccess = FileSystemAccessLevel.Full
        };

        return services.AddHartonomousAgentClient(configuration, developmentPolicy);
    }
}