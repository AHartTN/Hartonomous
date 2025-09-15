using Hartonomous.Core.Interfaces;
using Hartonomous.Core.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Core;

public static class ServiceExtensions
{
    public static IServiceCollection AddHartonomousCore(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IProjectRepository, ProjectRepository>();
        services.AddScoped<IModelRepository, ModelRepository>();

        return services;
    }
}