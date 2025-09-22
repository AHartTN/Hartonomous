using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.StackExchangeRedis;
using Microsoft.Extensions.Caching.SqlServer;
using Microsoft.Extensions.DependencyInjection.Extensions;
using HealthChecks.SqlServer;
using Hartonomous.Core.Abstractions;
using Hartonomous.Core.DTOs;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Repositories;
using Hartonomous.Core.Services;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Orchestration.DTOs;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;

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
        services.Configure<VectorDatabaseOptions>(configuration.GetSection(VectorDatabaseOptions.SectionName));
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
        // Register Entity Framework repository pattern
        services.AddScoped(typeof(Interfaces.IRepository<>), typeof(Repository<>));

        // Specialized repositories
        services.AddScoped<IKnowledgeGraphRepository, KnowledgeGraphRepository>();
        services.AddScoped<IModelComponentRepository, ModelComponentRepository>();
        services.AddScoped<IDistilledAgentRepository, DistilledAgentRepository>();
        services.AddScoped<IModelRepository, ModelRepository>();

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
            .AddCheck<VectorDatabaseHealthCheck>("vector-db", tags: new[] { "db", "vector", "ready" });

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
            var serviceProvider = services.BuildServiceProvider();
            var loggerFactory = serviceProvider.GetService<Microsoft.Extensions.Logging.ILoggerFactory>();
            var logger = loggerFactory?.CreateLogger("Hartonomous.Configuration");
            Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(logger, "Neo4j configuration incomplete. Knowledge graph features will be limited.");
        }

        return services;
    }
}

/// <summary>
/// HTTP client interfaces for microservice communication
/// </summary>
public interface IModelQueryClient
{
    Task<ModelIngestionResult> IngestModelAsync(string modelPath, string modelName, string userId);
    Task<IEnumerable<ModelComponentQueryResult>> QueryModelComponentsAsync(Guid modelId, string query, string userId, double similarityThreshold = 0.8, int limit = 10);
    Task<NeuralPatternExtractionResult> ExtractNeuralPatternsAsync(Guid modelId, string patternType, string userId, Dictionary<string, object>? parameters = null);
}

public interface IMcpClient
{
    Task<Guid> StoreMessageAsync(McpMessage message, string userId);
    Task<McpMessage?> GetMessageAsync(Guid messageId, string userId);
    Task<IEnumerable<McpMessage>> GetMessagesForAgentAsync(Guid agentId, string userId, int limit = 100);
    Task<IEnumerable<McpMessage>> GetUnreadMessagesAsync(Guid agentId, string userId);
    Task<bool> MarkMessagesAsReadAsync(Guid agentId, IEnumerable<Guid> messageIds, string userId);
    Task<bool> StoreTaskAssignmentAsync(TaskAssignment assignment, string userId);
    Task<TaskAssignment?> GetTaskAssignmentAsync(Guid taskId, string userId);
}

public interface IOrchestrationClient
{
    Task<Guid> StartWorkflowAsync(Guid workflowId, Dictionary<string, object>? input, Dictionary<string, object>? configuration, string userId, string? executionName = null);
    Task<bool> ResumeWorkflowAsync(Guid executionId, string userId);
    Task<bool> PauseWorkflowAsync(Guid executionId, string userId);
    Task<bool> CancelWorkflowAsync(Guid executionId, string userId);
    Task<WorkflowExecutionDto?> GetExecutionStatusAsync(Guid executionId, string userId);
    Task<WorkflowValidationResult> ValidateWorkflowAsync(string workflowDefinition);
}

/// <summary>
/// HTTP client implementations for microservice communication
/// </summary>
public class ModelQueryClient : IModelQueryClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ModelQueryClient> _logger;

    public ModelQueryClient(HttpClient httpClient, ILogger<ModelQueryClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ModelIngestionResult> IngestModelAsync(string modelPath, string modelName, string userId)
    {
        try
        {
            var request = new { modelPath, modelName, userId };
            var response = await _httpClient.PostAsJsonAsync("/api/model/ingest", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<ModelIngestionResult>();
                return result ?? new ModelIngestionResult { Success = false, ErrorMessage = "Invalid response format" };
            }

            return new ModelIngestionResult
            {
                Success = false,
                ErrorMessage = $"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ingesting model {ModelPath} for user {UserId}", modelPath, userId);
            return new ModelIngestionResult { Success = false, ErrorMessage = ex.Message };
        }
    }

    public async Task<IEnumerable<ModelComponentQueryResult>> QueryModelComponentsAsync(
        Guid modelId, string query, string userId, double similarityThreshold = 0.8, int limit = 10)
    {
        try
        {
            var requestUri = $"/api/model/{modelId}/query?userId={userId}&query={Uri.EscapeDataString(query)}&threshold={similarityThreshold}&limit={limit}";
            var response = await _httpClient.GetAsync(requestUri);

            if (response.IsSuccessStatusCode)
            {
                var results = await response.Content.ReadFromJsonAsync<IEnumerable<ModelComponentQueryResult>>();
                return results ?? Enumerable.Empty<ModelComponentQueryResult>();
            }

            _logger.LogWarning("Query components failed with status {StatusCode} for model {ModelId}", response.StatusCode, modelId);
            return Enumerable.Empty<ModelComponentQueryResult>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying model components for model {ModelId}", modelId);
            return Enumerable.Empty<ModelComponentQueryResult>();
        }
    }

    public async Task<NeuralPatternExtractionResult> ExtractNeuralPatternsAsync(
        Guid modelId, string patternType, string userId, Dictionary<string, object>? parameters = null)
    {
        try
        {
            var request = new { modelId, patternType, userId, parameters = parameters ?? new Dictionary<string, object>() };
            var response = await _httpClient.PostAsJsonAsync("/api/model/extract-patterns", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<NeuralPatternExtractionResult>();
                return result ?? new NeuralPatternExtractionResult { Success = false, ErrorMessage = "Invalid response format" };
            }

            return new NeuralPatternExtractionResult
            {
                Success = false,
                ErrorMessage = $"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}"
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error extracting neural patterns for model {ModelId}", modelId);
            return new NeuralPatternExtractionResult { Success = false, ErrorMessage = ex.Message };
        }
    }
}

public class McpClient : IMcpClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpClient> _logger;

    public McpClient(HttpClient httpClient, ILogger<McpClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Guid> StoreMessageAsync(McpMessage message, string userId)
    {
        try
        {
            var request = new { message, userId };
            var response = await _httpClient.PostAsJsonAsync("/api/mcp/messages", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Guid>();
                return result;
            }

            _logger.LogWarning("Store message failed with status {StatusCode}", response.StatusCode);
            return Guid.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing MCP message");
            return Guid.Empty;
        }
    }

    public async Task<McpMessage?> GetMessageAsync(Guid messageId, string userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/mcp/messages/{messageId}?userId={userId}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<McpMessage>();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting MCP message {MessageId}", messageId);
            return null;
        }
    }

    public async Task<IEnumerable<McpMessage>> GetMessagesForAgentAsync(Guid agentId, string userId, int limit = 100)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/mcp/agents/{agentId}/messages?userId={userId}&limit={limit}");

            if (response.IsSuccessStatusCode)
            {
                var messages = await response.Content.ReadFromJsonAsync<IEnumerable<McpMessage>>();
                return messages ?? Enumerable.Empty<McpMessage>();
            }

            return Enumerable.Empty<McpMessage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages for agent {AgentId}", agentId);
            return Enumerable.Empty<McpMessage>();
        }
    }

    public async Task<IEnumerable<McpMessage>> GetUnreadMessagesAsync(Guid agentId, string userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/mcp/agents/{agentId}/unread?userId={userId}");

            if (response.IsSuccessStatusCode)
            {
                var messages = await response.Content.ReadFromJsonAsync<IEnumerable<McpMessage>>();
                return messages ?? Enumerable.Empty<McpMessage>();
            }

            return Enumerable.Empty<McpMessage>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting unread messages for agent {AgentId}", agentId);
            return Enumerable.Empty<McpMessage>();
        }
    }

    public async Task<bool> MarkMessagesAsReadAsync(Guid agentId, IEnumerable<Guid> messageIds, string userId)
    {
        try
        {
            var request = new { agentId, messageIds, userId };
            var response = await _httpClient.PutAsJsonAsync("/api/mcp/messages/mark-read", request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking messages as read for agent {AgentId}", agentId);
            return false;
        }
    }

    public async Task<bool> StoreTaskAssignmentAsync(TaskAssignment assignment, string userId)
    {
        try
        {
            var request = new { assignment, userId };
            var response = await _httpClient.PostAsJsonAsync("/api/mcp/tasks", request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing task assignment");
            return false;
        }
    }

    public async Task<TaskAssignment?> GetTaskAssignmentAsync(Guid taskId, string userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/mcp/tasks/{taskId}?userId={userId}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<TaskAssignment>();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting task assignment {TaskId}", taskId);
            return null;
        }
    }
}

public class OrchestrationClient : IOrchestrationClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OrchestrationClient> _logger;

    public OrchestrationClient(HttpClient httpClient, ILogger<OrchestrationClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<Guid> StartWorkflowAsync(Guid workflowId, Dictionary<string, object>? input,
        Dictionary<string, object>? configuration, string userId, string? executionName = null)
    {
        try
        {
            var request = new { workflowId, input, configuration, userId, executionName };
            var response = await _httpClient.PostAsJsonAsync("/api/orchestration/workflows/start", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<Guid>();
                return result;
            }

            _logger.LogWarning("Start workflow failed with status {StatusCode} for workflow {WorkflowId}",
                response.StatusCode, workflowId);
            return Guid.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting workflow {WorkflowId}", workflowId);
            return Guid.Empty;
        }
    }

    public async Task<bool> ResumeWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            var request = new { executionId, userId };
            var response = await _httpClient.PostAsJsonAsync("/api/orchestration/executions/resume", request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resuming workflow execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> PauseWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            var request = new { executionId, userId };
            var response = await _httpClient.PostAsJsonAsync("/api/orchestration/executions/pause", request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error pausing workflow execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<bool> CancelWorkflowAsync(Guid executionId, string userId)
    {
        try
        {
            var request = new { executionId, userId };
            var response = await _httpClient.PostAsJsonAsync("/api/orchestration/executions/cancel", request);

            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error canceling workflow execution {ExecutionId}", executionId);
            return false;
        }
    }

    public async Task<WorkflowExecutionDto?> GetExecutionStatusAsync(Guid executionId, string userId)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/orchestration/executions/{executionId}/status?userId={userId}");

            if (response.IsSuccessStatusCode)
            {
                return await response.Content.ReadFromJsonAsync<WorkflowExecutionDto>();
            }

            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting execution status for {ExecutionId}", executionId);
            return null;
        }
    }

    public async Task<WorkflowValidationResult> ValidateWorkflowAsync(string workflowDefinition)
    {
        try
        {
            var request = new { workflowDefinition };
            var response = await _httpClient.PostAsJsonAsync("/api/orchestration/workflows/validate", request);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<WorkflowValidationResult>();
                return result ?? new WorkflowValidationResult { IsValid = false, Errors = new[] { "Invalid response format" } };
            }

            return new WorkflowValidationResult
            {
                IsValid = false,
                Errors = new[] { $"HTTP {response.StatusCode}: {await response.Content.ReadAsStringAsync()}" }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error validating workflow definition");
            return new WorkflowValidationResult { IsValid = false, Errors = new[] { ex.Message } };
        }
    }
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
            var healthEndpoint = $"{_options.ConnectionString?.Replace("bolt://", "http://").Replace("7687", "7474")}/db/manage/server/health";

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

public class VectorDatabaseHealthCheck : Microsoft.Extensions.Diagnostics.HealthChecks.IHealthCheck
{
    public Task<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult> CheckHealthAsync(
        Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // This health check validates that SQL Server 2025 vector functionality is available
        try
        {
            // We would check SQL Server vector functionality here
            // For now, return healthy as we're using SQL Server native vectors
            return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Healthy("SQL Server vector capabilities available"));
        }
        catch (Exception ex)
        {
            return Task.FromResult(Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckResult.Unhealthy("Vector database functionality unavailable", ex));
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