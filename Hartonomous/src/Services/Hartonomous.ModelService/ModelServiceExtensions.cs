using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Hartonomous.ModelService.Services;
using Hartonomous.ModelService.Abstractions;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.ModelService;

/// <summary>
/// Extension methods for registering unified ModelService components
/// Consolidates all model processing, circuit discovery, and agent synthesis functionality
/// </summary>
public static class ModelServiceExtensions
{
    /// <summary>
    /// Register unified ModelService with all processing pipeline components
    /// Provides centralized model ingestion, circuit discovery, and agent synthesis
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configuration">Application configuration</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddHartonomousModelService(this IServiceCollection services, IConfiguration configuration)
    {
        // Register core model processing services
        services.AddScoped<ModelDistillationEngine>();
        services.AddScoped<ModelQueryEngineService>();
        services.AddScoped<MechanisticInterpretabilityService>();
        services.AddScoped<AgentDistillationService>();
        services.AddScoped<ModelIntrospectionService>();

        // Register model repositories with generic patterns
        services.AddScoped<IModelArchitectureRepository, ModelArchitectureRepository>();
        services.AddScoped<IModelVersionRepository, ModelVersionRepository>();
        services.AddScoped<IModelWeightRepository, ModelWeightRepository>();
        services.AddScoped<INeuralMapRepository, NeuralMapRepository>();

        // Register unified model processing pipeline
        services.AddScoped<IModelProcessingPipeline, ModelProcessingPipeline>();

        // Configure model service options
        services.Configure<ModelServiceOptions>(configuration.GetSection("ModelService"));

        return services;
    }

    /// <summary>
    /// Register model processing pipeline with custom configuration
    /// Allows for advanced pipeline customization and stage configuration
    /// </summary>
    /// <param name="services">Service collection</param>
    /// <param name="configureOptions">Pipeline configuration delegate</param>
    /// <returns>Service collection for chaining</returns>
    public static IServiceCollection AddModelProcessingPipeline(this IServiceCollection services,
        Action<ModelProcessingPipelineOptions> configureOptions)
    {
        services.Configure(configureOptions);
        services.AddScoped<IModelProcessingPipeline, ModelProcessingPipeline>();

        return services;
    }
}

/// <summary>
/// Configuration options for ModelService
/// </summary>
public class ModelServiceOptions
{
    /// <summary>
    /// Maximum number of concurrent model processing operations
    /// </summary>
    public int MaxConcurrentProcessing { get; set; } = 3;

    /// <summary>
    /// Model storage base path for FILESTREAM operations
    /// </summary>
    public string ModelStoragePath { get; set; } = "/app/models";

    /// <summary>
    /// Vector embedding dimensions for component analysis
    /// </summary>
    public int VectorDimensions { get; set; } = 1536;

    /// <summary>
    /// Default similarity threshold for component discovery
    /// </summary>
    public double DefaultSimilarityThreshold { get; set; } = 0.8;

    /// <summary>
    /// Enable circuit discovery during model ingestion
    /// </summary>
    public bool EnableCircuitDiscovery { get; set; } = true;

    /// <summary>
    /// Enable activation tracing for mechanistic interpretability
    /// </summary>
    public bool EnableActivationTracing { get; set; } = true;

    /// <summary>
    /// Maximum model file size in bytes (default 50GB)
    /// </summary>
    public long MaxModelFileSizeBytes { get; set; } = 50L * 1024 * 1024 * 1024;
}

/// <summary>
/// Configuration options for model processing pipeline
/// </summary>
public class ModelProcessingPipelineOptions
{
    /// <summary>
    /// Enable parallel processing of pipeline stages
    /// </summary>
    public bool EnableParallelProcessing { get; set; } = true;

    /// <summary>
    /// Timeout for individual pipeline stages
    /// </summary>
    public TimeSpan StageTimeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Enable detailed logging for pipeline execution
    /// </summary>
    public bool EnableDetailedLogging { get; set; } = false;

    /// <summary>
    /// Batch size for component processing
    /// </summary>
    public int ComponentProcessingBatchSize { get; set; } = 1000;
}