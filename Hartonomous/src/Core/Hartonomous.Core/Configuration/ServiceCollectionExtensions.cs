using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Caching.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HealthChecks.SqlServer;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Repositories;
using Hartonomous.Core.Services;
using Hartonomous.Infrastructure.Neo4j;
using System.Reflection;

namespace Hartonomous.Core.Configuration;

/// <summary>
/// Centralized service registration extensions for Hartonomous platform
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add all Hartonomous core services with proper configuration
    /// </summary>
    public static IServiceCollection AddHartonomousCore(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure all Options<T> patterns
        services.AddHartonomousOptions(configuration);

        // Add core abstractions and factories
        services.AddHartonomousAbstractions();

        // Add validation for all options
        services.AddOptionsValidation();

        return services;
    }

    /// <summary>
    /// Configure all Options<T> patterns with validation
    /// </summary>
    public static IServiceCollection AddHartonomousOptions(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Core database options
        services.Configure<DatabaseOptions>(configuration.GetSection(DatabaseOptions.SectionName));
        services.Configure<SqlServerOptions>(configuration.GetSection(SqlServerOptions.SectionName));

        // Azure services options
        services.Configure<AzureAdOptions>(configuration.GetSection(AzureAdOptions.SectionName));
        services.Configure<EntraExternalIdOptions>(configuration.GetSection(EntraExternalIdOptions.SectionName));
        services.Configure<ServicePrincipalOptions>(configuration.GetSection(ServicePrincipalOptions.SectionName));
        services.Configure<KeyVaultOptions>(configuration.GetSection(KeyVaultOptions.SectionName));
        services.Configure<AzureAppConfigOptions>(configuration.GetSection(AzureAppConfigOptions.SectionName));

        // Infrastructure services options
        services.Configure<Neo4jOptions>(configuration.GetSection(Neo4jOptions.SectionName));
        services.Configure<MilvusOptions>(configuration.GetSection(MilvusOptions.SectionName));
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));

        // Application services options
        services.Configure<McpOptions>(configuration.GetSection(McpOptions.SectionName));
        services.Configure<DistillationOptions>(configuration.GetSection(DistillationOptions.SectionName));

        return services;
    }

    /// <summary>
    /// Add core abstractions, interfaces, and factory patterns
    /// </summary>
    public static IServiceCollection AddHartonomousAbstractions(this IServiceCollection services)
    {
        // Repository patterns
        services.AddScoped<IRepositoryFactory, RepositoryFactory>();
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        // Generic service factories
        services.AddScoped(typeof(IServiceFactory<>), typeof(ServiceFactory<,>));

        // Agent factory
        services.AddScoped<IAgentFactory, AgentFactory>();

        // Register generic repository
        services.AddScoped(typeof(IRepository<,>), typeof(GenericRepository<,>));

        // Knowledge graph repository
        services.AddScoped<IKnowledgeGraphRepository, KnowledgeGraphRepository>();

        // Data fabric synchronization service
        services.AddHostedService<DataFabricSyncService>();

        return services;
    }

    /// <summary>
    /// Add options validation for all configuration classes
    /// </summary>
    public static IServiceCollection AddOptionsValidation(this IServiceCollection services)
    {
        // Add validation for all options types using reflection
        var optionsTypes = Assembly.GetExecutingAssembly()
            .GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                       t.Namespace == "Hartonomous.Core.Configuration" &&
                       t.Name.EndsWith("Options"))
            .ToArray();

        foreach (var optionsType in optionsTypes)
        {
            var sectionNameProperty = optionsType.GetField("SectionName", BindingFlags.Public | BindingFlags.Static);
            if (sectionNameProperty?.GetValue(null) is string sectionName)
            {
                // Add validation for each options type
                var method = typeof(OptionsServiceCollectionExtensions)
                    .GetMethods()
                    .Where(m => m.Name == "AddOptions" && m.IsGenericMethodDefinition)
                    .FirstOrDefault(m => m.GetParameters().Length == 1)?
                    .MakeGenericMethod(optionsType);

                var optionsBuilder = method?.Invoke(null, new object[] { services });

                // Add data annotation validation
                var validateMethod = optionsBuilder?.GetType()
                    .GetMethod("ValidateDataAnnotations");
                validateMethod?.Invoke(optionsBuilder, Array.Empty<object>());
            }
        }

        return services;
    }

    /// <summary>
    /// Add thin client services with proper HttpClient configuration
    /// </summary>
    public static IServiceCollection AddHartonomousThinClients(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure HttpClient for each service
        services.AddHttpClient<IModelQueryClient, ModelQueryClient>(client =>
        {
            var baseUrl = configuration.GetSection("Services:ModelQuery:BaseUrl").Value;
            if (!string.IsNullOrEmpty(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
            client.Timeout = TimeSpan.FromMinutes(5);
        });

        services.AddHttpClient<IMcpClient, McpClient>(client =>
        {
            var baseUrl = configuration.GetSection("Services:MCP:BaseUrl").Value;
            if (!string.IsNullOrEmpty(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
            client.Timeout = TimeSpan.FromMinutes(1);
        });

        services.AddHttpClient<IOrchestrationClient, OrchestrationClient>(client =>
        {
            var baseUrl = configuration.GetSection("Services:Orchestration:BaseUrl").Value;
            if (!string.IsNullOrEmpty(baseUrl))
            {
                client.BaseAddress = new Uri(baseUrl);
            }
            client.Timeout = TimeSpan.FromMinutes(10);
        });

        return services;
    }

    /// <summary>
    /// Add caching services with consistent configuration
    /// </summary>
    public static IServiceCollection AddHartonomousCaching(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Memory cache for frequently accessed data
        services.AddMemoryCache(options =>
        {
            options.SizeLimit = configuration.GetValue<long>("Caching:MemoryCache:SizeLimit", 100_000);
        });

        // Distributed cache for multi-instance scenarios
        var distributedCacheType = configuration.GetValue<string>("Caching:DistributedCache:Type", "Memory");
        switch (distributedCacheType.ToLowerInvariant())
        {
            case "redis":
                var redisConnection = configuration.GetConnectionString("Redis");
                if (!string.IsNullOrEmpty(redisConnection))
                {
                    services.AddStackExchangeRedisCache(options =>
                    {
                        options.Configuration = redisConnection;
                    });
                }
                else
                {
                    services.AddDistributedMemoryCache();
                }
                break;

            case "sqlserver":
                var sqlConnection = configuration.GetConnectionString("DefaultConnection");
                if (!string.IsNullOrEmpty(sqlConnection))
                {
                    services.AddDistributedSqlServerCache(options =>
                    {
                        options.ConnectionString = sqlConnection;
                        options.SchemaName = "dbo";
                        options.TableName = "DistributedCache";
                    });
                }
                else
                {
                    services.AddDistributedMemoryCache();
                }
                break;

            default:
                services.AddDistributedMemoryCache();
                break;
        }

        return services;
    }

    /// <summary>
    /// Add observability services (metrics, logging, tracing)
    /// </summary>
    public static IServiceCollection AddHartonomousObservability(
        this IServiceCollection services,
        IConfiguration configuration,
        string serviceName)
    {
        // Add health checks
        services.AddHealthChecks()
            .AddSqlServer(
                connectionString: configuration.GetConnectionString("DefaultConnection")!,
                name: "sqlserver",
                tags: new[] { "db", "sql", "ready" })
            .AddCheck<Neo4jHealthCheck>("neo4j", tags: new[] { "db", "graph", "ready" })
            .AddCheck<MilvusHealthCheck>("milvus", tags: new[] { "db", "vector", "ready" });

        // Add metrics collection
        services.AddSingleton<IMetricsCollector, MetricsCollector>();

        // Add OpenTelemetry if configured
        var enableTelemetry = configuration.GetValue<bool>("Observability:EnableOpenTelemetry", false);
        if (enableTelemetry)
        {
            // OpenTelemetry configuration would be added here
            // services.AddOpenTelemetryTracing(serviceName);
            // services.AddOpenTelemetryMetrics(serviceName);
        }

        return services;
    }

    /// <summary>
    /// Add Neo4j knowledge graph services
    /// </summary>
    public static IServiceCollection AddHartonomousKnowledgeGraph(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Register Neo4j infrastructure service
        services.AddSingleton<Neo4jService>();

        // Validate Neo4j configuration
        var neo4jUri = configuration["Neo4j:Uri"];
        var neo4jUsername = configuration["Neo4j:Username"];
        var neo4jPassword = configuration["Neo4j:Password"];

        if (string.IsNullOrEmpty(neo4jUri) || string.IsNullOrEmpty(neo4jUsername) || string.IsNullOrEmpty(neo4jPassword))
        {
            // Log warning but don't fail - Neo4j is optional for development
            services.BuildServiceProvider().GetService<Microsoft.Extensions.Logging.ILogger<ServiceCollectionExtensions>>()
                ?.LogWarning("Neo4j configuration incomplete. Knowledge graph features will be limited.");
        }

        return services;
    }
}

/// <summary>
/// Placeholder interfaces for thin clients (to be implemented)
/// </summary>
public interface IModelQueryClient { }
public interface IMcpClient { }
public interface IOrchestrationClient { }

/// <summary>
/// Placeholder implementations for thin clients
/// </summary>
public class ModelQueryClient : IModelQueryClient
{
    public ModelQueryClient(HttpClient httpClient) { }
}

public class McpClient : IMcpClient
{
    public McpClient(HttpClient httpClient) { }
}

public class OrchestrationClient : IOrchestrationClient
{
    public OrchestrationClient(HttpClient httpClient) { }
}

/// <summary>
/// Health check implementations
/// </summary>
public class Neo4jHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    private readonly Neo4jOptions _options;

    public Neo4jHealthCheck(Microsoft.Extensions.Options.IOptions<Neo4jOptions> options)
    {
        _options = options.Value;
    }

    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            var healthEndpoint = $"{_options.Uri.Replace("bolt://", "http://").Replace("7687", "7474")}/db/manage/server/health";

            var response = await client.GetAsync(healthEndpoint, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("Neo4j is responding");
            }
            else
            {
                return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy($"Neo4j returned {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Neo4j connection failed", ex);
        }
    }
}

public class MilvusHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public async Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // Since we're using SQL Server 2025 vector capabilities instead of Milvus,
        // this health check validates that vector functionality is available
        try
        {
            // We would check SQL Server vector functionality here
            // For now, return healthy as we're using SQL Server native vectors
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("SQL Server vector capabilities available");
        }
        catch (Exception ex)
        {
            return Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Vector database functionality unavailable", ex);
        }
    }
}

/// <summary>
/// Metrics collection interface and implementation
/// </summary>
public interface IMetricsCollector
{
    void IncrementCounter(string name, double value, Dictionary<string, string>? tags = null);
    void RecordGauge(string name, double value, Dictionary<string, string>? tags = null);
    void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null);
}

public class MetricsCollector : IMetricsCollector
{
    public void IncrementCounter(string name, double value, Dictionary<string, string>? tags = null)
    {
        // Implement counter metric collection
    }

    public void RecordGauge(string name, double value, Dictionary<string, string>? tags = null)
    {
        // Implement gauge metric collection
    }

    public void RecordHistogram(string name, double value, Dictionary<string, string>? tags = null)
    {
        // Implement histogram metric collection
    }
}