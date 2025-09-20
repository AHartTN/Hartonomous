using Hartonomous.Orchestration.DSL;
using Hartonomous.Orchestration.Engines;
using Hartonomous.Orchestration.Interfaces;
using Hartonomous.Orchestration.Repositories;
using Hartonomous.Orchestration.Services;

namespace Hartonomous.Orchestration;

/// <summary>
/// Service collection extensions for registering orchestration services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add orchestration services to the service collection
    /// </summary>
    public static IServiceCollection AddOrchestrationServices(this IServiceCollection services, IConfiguration configuration)
    {
        // Register repositories
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();
        services.AddScoped<IWorkflowTemplateRepository, WorkflowTemplateRepository>();

        // Register services
        services.AddScoped<IWorkflowStateManager, WorkflowStateManager>();
        services.AddScoped<IWorkflowTemplateService, WorkflowTemplateService>();
        services.AddScoped<IWorkflowDSLParser, WorkflowDSLParser>();

        // Register execution engine
        services.AddScoped<IWorkflowExecutionEngine, LangGraphWorkflowEngine>();

        // Register configuration
        services.Configure<OrchestrationOptions>(configuration.GetSection("Orchestration"));

        // Add HTTP client for external integrations
        services.AddHttpClient();

        return services;
    }
}

/// <summary>
/// Configuration options for orchestration service
/// </summary>
public class OrchestrationOptions
{
    public const string SectionName = "Orchestration";

    /// <summary>
    /// Maximum concurrent workflow executions per user
    /// </summary>
    public int MaxConcurrentExecutions { get; set; } = 10;

    /// <summary>
    /// Default workflow execution timeout in minutes
    /// </summary>
    public int DefaultTimeoutMinutes { get; set; } = 60;

    /// <summary>
    /// Maximum workflow definition size in KB
    /// </summary>
    public int MaxWorkflowSizeKB { get; set; } = 1024;

    /// <summary>
    /// Enable workflow execution metrics collection
    /// </summary>
    public bool EnableMetrics { get; set; } = true;

    /// <summary>
    /// Enable workflow debugging features
    /// </summary>
    public bool EnableDebugging { get; set; } = true;

    /// <summary>
    /// Workflow state retention days
    /// </summary>
    public int StateRetentionDays { get; set; } = 30;

    /// <summary>
    /// Maximum number of workflow versions to keep
    /// </summary>
    public int MaxVersionsToKeep { get; set; } = 10;

    /// <summary>
    /// Enable workflow template sharing
    /// </summary>
    public bool EnableTemplateSharing { get; set; } = true;

    /// <summary>
    /// LangGraph execution settings
    /// </summary>
    public LangGraphOptions LangGraph { get; set; } = new();
}

/// <summary>
/// LangGraph-specific configuration options
/// </summary>
public class LangGraphOptions
{
    /// <summary>
    /// Maximum nodes per workflow
    /// </summary>
    public int MaxNodesPerWorkflow { get; set; } = 100;

    /// <summary>
    /// Enable parallel node execution
    /// </summary>
    public bool EnableParallelExecution { get; set; } = true;

    /// <summary>
    /// Maximum parallel node executions
    /// </summary>
    public int MaxParallelNodes { get; set; } = 5;

    /// <summary>
    /// Node execution timeout in seconds
    /// </summary>
    public int NodeTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// Enable automatic retry for failed nodes
    /// </summary>
    public bool EnableAutoRetry { get; set; } = true;

    /// <summary>
    /// Maximum retry attempts per node
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// Retry delay in seconds
    /// </summary>
    public int RetryDelaySeconds { get; set; } = 5;
}