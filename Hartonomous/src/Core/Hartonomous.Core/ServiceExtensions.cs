using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Repositories;
using Hartonomous.Core.Services;
using Hartonomous.Infrastructure.Security.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Core;

public static class ServiceExtensions
{
    public static IServiceCollection AddHartonomousCore(this IServiceCollection services, IConfiguration configuration)
    {
        // Register domain-specific repositories (the actual working implementations)
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IModelRepository, ModelRepository>();

        // Register current user service (development version for standalone Core)
        // Production apps should use SecurityServiceExtensions.AddHartonomousAuthentication()
        services.AddScoped<ICurrentUserService, Hartonomous.Core.Services.DevelopmentCurrentUserService>();

        return services;
    }

    public static IServiceCollection AddHartonomousCore(this IServiceCollection services)
    {
        // Register domain-specific repositories (the actual working implementations)
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IModelRepository, ModelRepository>();

        // Register current user service (development version for standalone Core)
        // Production apps should use SecurityServiceExtensions.AddHartonomousAuthentication()
        services.AddScoped<ICurrentUserService, Hartonomous.Core.Services.DevelopmentCurrentUserService>();

        return services;
    }
}