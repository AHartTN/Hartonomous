using Hartonomous.Core.Services.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Core.Services;

/// <summary>
/// Dependency injection extensions for Hartonomous Core services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Hartonomous Core services with the default database service.
    /// </summary>
    public static IServiceCollection AddHartonomousCoreServices(this IServiceCollection services)
    {
        // Register the singleton database service
        services.AddSingleton<IDatabaseService>(_ => DatabaseService.Instance);
        
        // Register other services
        services.AddSingleton<ISpatialQueryService, SpatialQueryService>();
        services.AddSingleton<IRelationshipService, RelationshipService>();
        services.AddSingleton<ITrajectoryService, TrajectoryService>();
        services.AddSingleton<IContainmentService, ContainmentService>();
        
        return services;
    }

    /// <summary>
    /// Add Hartonomous Core services with a custom database service.
    /// </summary>
    public static IServiceCollection AddHartonomousCoreServices<TDatabaseService>(this IServiceCollection services)
        where TDatabaseService : class, IDatabaseService
    {
        services.AddSingleton<IDatabaseService, TDatabaseService>();
        services.AddSingleton<ISpatialQueryService, SpatialQueryService>();
        services.AddSingleton<IRelationshipService, RelationshipService>();
        services.AddSingleton<ITrajectoryService, TrajectoryService>();
        services.AddSingleton<IContainmentService, ContainmentService>();
        
        return services;
    }
}
