using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Infrastructure.Milvus;

/// <summary>
/// Extension methods for registering Milvus services
/// Follows Hartonomous infrastructure pattern
/// </summary>
public static class MilvusServiceExtensions
{
    /// <summary>
    /// Add Milvus vector database services to the container
    /// </summary>
    public static IServiceCollection AddHartonomousMilvus(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate configuration
        var host = configuration["Milvus:Host"];
        var port = configuration["Milvus:Port"];

        if (string.IsNullOrEmpty(host))
            throw new ArgumentException("Milvus:Host configuration is required");
        if (string.IsNullOrEmpty(port))
            throw new ArgumentException("Milvus:Port configuration is required");

        // Register Milvus service as singleton for connection pooling
        services.AddSingleton<MilvusService>();

        return services;
    }
}