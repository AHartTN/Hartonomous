/*
 * Hartonomous ModelQuery Service Library
 *
 * This service has been converted from a web application to a service library.
 * All web hosting functionality has been moved to the API Gateway.
 * This file now only contains documentation for the service configuration.
 */

using Hartonomous.ModelQuery.Interfaces;
using Hartonomous.ModelQuery.Repositories;
using Hartonomous.ModelQuery.Services;
using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.ModelQuery;

/// <summary>
/// Model Query Service Configuration
///
/// This service provides deep learning model introspection and analysis functionality including:
/// - Model architecture analysis
/// - Neural network mapping
/// - Model weight analysis
/// - Model version management
/// - Model introspection services
///
/// Service Dependencies (to be registered in API Gateway):
/// - IModelRepository -> ModelRepository
/// - INeuralMapRepository -> NeuralMapRepository
/// - IModelWeightRepository -> ModelWeightRepository
/// - IModelArchitectureRepository -> ModelArchitectureRepository
/// - IModelVersionRepository -> ModelVersionRepository
/// - IModelIntrospectionService -> ModelIntrospectionService
///
/// IMPORTANT: This service is now consumed as a library by the API Gateway.
/// All HTTP endpoints have been moved to the API Gateway controllers.
/// </summary>
public static class ModelQueryServiceInfo
{
    public const string ServiceName = "Hartonomous Model Query & Introspection";
    public const string Version = "1.0.0";

    public static readonly string[] Capabilities =
    {
        "model-introspection",
        "neural-map-analysis",
        "model-weight-analysis",
        "architecture-discovery",
        "version-management",
        "model-comparison"
    };
}

/// <summary>
/// Extension methods for configuring ModelQuery services
/// </summary>
public static class ModelQueryServiceExtensions
{
    /// <summary>
    /// Add ModelQuery services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddModelQueryServices(this IServiceCollection services)
    {
        // Add repositories
        services.AddScoped<IModelRepository, ModelRepository>();
        services.AddScoped<INeuralMapRepository, NeuralMapRepository>();
        services.AddScoped<IModelWeightRepository, ModelWeightRepository>();
        services.AddScoped<IModelArchitectureRepository, ModelArchitectureRepository>();
        services.AddScoped<IModelVersionRepository, ModelVersionRepository>();

        // Add services
        services.AddScoped<IModelIntrospectionService, ModelIntrospectionService>();

        return services;
    }
}