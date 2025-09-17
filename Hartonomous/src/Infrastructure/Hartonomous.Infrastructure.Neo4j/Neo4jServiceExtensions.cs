using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Hartonomous.Infrastructure.Neo4j;

/// <summary>
/// Extension methods for registering Neo4j services
/// Follows Hartonomous infrastructure pattern
/// </summary>
public static class Neo4jServiceExtensions
{
    /// <summary>
    /// Add Neo4j knowledge graph services to the container
    /// </summary>
    public static IServiceCollection AddHartonomousNeo4j(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate configuration
        var uri = configuration["Neo4j:Uri"];
        var username = configuration["Neo4j:Username"];
        var password = configuration["Neo4j:Password"];

        if (string.IsNullOrEmpty(uri))
            throw new ArgumentException("Neo4j:Uri configuration is required");
        if (string.IsNullOrEmpty(username))
            throw new ArgumentException("Neo4j:Username configuration is required");
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Neo4j:Password configuration is required");

        // Register Neo4j service as singleton for connection pooling
        services.AddSingleton<Neo4jService>();

        return services;
    }
}