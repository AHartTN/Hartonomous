using Hartonomous.Data.Abstractions;
using Hartonomous.Infrastructure.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Infrastructure;

/// <summary>
/// Dependency injection extensions for Hartonomous Infrastructure services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add in-memory data repositories.
    /// Suitable for development, testing, and single-instance scenarios.
    /// </summary>
    public static IServiceCollection AddHartonomousInMemoryStorage(this IServiceCollection services)
    {
        services.AddSingleton<InMemoryAtomRepository>();
        services.AddSingleton<InMemoryCompositionRepository>();
        services.AddSingleton<IUnitOfWorkFactory, InMemoryUnitOfWorkFactory>();
        return services;
    }
}
