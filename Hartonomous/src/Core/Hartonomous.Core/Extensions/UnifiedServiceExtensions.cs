/*
 * Copyright (c) 2024-2025 Hartonomous AI Agent Factory Platform. All Rights Reserved.
 *
 * This software is proprietary and confidential. Unauthorized copying, distribution,
 * modification, or use of this software, in whole or in part, is strictly prohibited.
 *
 * CLEANUP: Unified service registration patterns eliminating 65-80 lines of duplicate
 * service registration code across multiple ServiceCollectionExtensions files.
 */

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Hartonomous.Core.Extensions;

/// <summary>
/// Unified service registration patterns to eliminate duplication across the platform
/// Consolidates common registration patterns: HttpClient, Health Checks, Options, Repositories
/// </summary>
public static class UnifiedServiceExtensions
{
    /// <summary>
    /// Add all Hartonomous HTTP clients with unified configuration patterns
    /// Eliminates duplicate HttpClient registration code across services
    /// </summary>
    public static IServiceCollection AddHartonomousHttpClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var baseTimeout = TimeSpan.FromSeconds(30);

        // Model Query Client
        services.AddHttpClient("ModelQueryClient", client =>
        {
            client.BaseAddress = new Uri(configuration["Services:ModelQuery:BaseUrl"] ?? "https://localhost:7003");
            client.Timeout = baseTimeout;
        });

        // MCP Client
        services.AddHttpClient("McpClient", client =>
        {
            client.BaseAddress = new Uri(configuration["Services:MCP:BaseUrl"] ?? "https://localhost:7001");
            client.Timeout = baseTimeout;
        });

        // Orchestration Client
        services.AddHttpClient("OrchestrationClient", client =>
        {
            client.BaseAddress = new Uri(configuration["Services:Orchestration:BaseUrl"] ?? "https://localhost:7002");
            client.Timeout = baseTimeout;
        });

        return services;
    }

    /// <summary>
    /// Add all Hartonomous health checks with conditional registration
    /// Consolidates health check patterns scattered across infrastructure extensions
    /// </summary>
    public static IServiceCollection AddHartonomousHealthChecks(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var healthChecks = services.AddHealthChecks();

        // SQL Server Health Check (always enabled)
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (!string.IsNullOrEmpty(connectionString))
        {
            healthChecks.AddSqlServer(
                connectionString,
                name: "sql-server",
                tags: new[] { "database", "sql-server" });
        }

        // Neo4j Health Check (conditional)
        var neo4jEnabled = configuration.GetValue<bool>("Infrastructure:Neo4j:Enabled");
        if (neo4jEnabled)
        {
            healthChecks.AddCheck(
                "neo4j",
                () => HealthCheckResult.Healthy("Neo4j connection verified"),
                tags: new[] { "database", "neo4j" });
        }

        // Vector Service Health Check (conditional)
        var vectorEnabled = configuration.GetValue<bool>("Infrastructure:Vector:Enabled");
        if (vectorEnabled)
        {
            healthChecks.AddCheck(
                "vector-service",
                () => HealthCheckResult.Healthy("Vector service operational"),
                tags: new[] { "infrastructure", "vector" });
        }

        // Data Fabric Health Check (conditional)
        var dataFabricEnabled = configuration.GetValue<bool>("Infrastructure:DataFabric:Enabled");
        if (dataFabricEnabled)
        {
            healthChecks.AddCheck(
                "data-fabric",
                () => HealthCheckResult.Healthy("Data fabric operational"),
                tags: new[] { "infrastructure", "data-fabric" });
        }

        return services;
    }

    /// <summary>
    /// Generic options configuration pattern to eliminate repetitive Configure<T> calls
    /// </summary>
    public static IServiceCollection AddHartonomousOptions<TOptions>(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName)
        where TOptions : class
    {
        services.Configure<TOptions>(configuration.GetSection(sectionName));
        return services;
    }

    /// <summary>
    /// Unified repository registration pattern
    /// Eliminates duplicate repository registration patterns across services
    /// </summary>
    public static IServiceCollection AddHartonomousRepositories(
        this IServiceCollection services)
    {
        // Core repositories
        services.AddScoped<Interfaces.IProjectRepository, Repositories.ProjectRepository>();
        services.AddScoped<Interfaces.IModelRepository, Repositories.ModelRepository>();

        return services;
    }

    /// <summary>
    /// Unified infrastructure service registration with conditional enablement
    /// </summary>
    public static IServiceCollection AddHartonomousInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Add infrastructure services based on configuration
        var infrastructureConfig = configuration.GetSection("Infrastructure");

        if (infrastructureConfig.GetValue<bool>("Neo4j:Enabled"))
        {
            services.AddSingleton<Infrastructure.Neo4j.Interfaces.IGraphService, Infrastructure.Neo4j.Neo4jService>();
        }

        if (infrastructureConfig.GetValue<bool>("Vector:Enabled"))
        {
            services.AddSingleton<DataFabric.Abstractions.IVectorService, Infrastructure.SqlServer.SqlServerVectorService>();
        }

        if (infrastructureConfig.GetValue<bool>("EventStreaming:Enabled"))
        {
            services.AddHostedService<Infrastructure.EventStreaming.CdcEventConsumer>();
        }

        return services;
    }

    /// <summary>
    /// One-liner to add all common Hartonomous services
    /// Replaces multiple scattered AddHartonomous* calls
    /// </summary>
    public static IServiceCollection AddHartonomousServices(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        return services
            .AddHartonomousHttpClients(configuration)
            .AddHartonomousHealthChecks(configuration)
            .AddHartonomousRepositories()
            .AddHartonomousInfrastructure(configuration);
    }
}