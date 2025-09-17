using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Hartonomous.Infrastructure.Neo4j;
using Hartonomous.Infrastructure.Milvus;

namespace Hartonomous.Infrastructure.EventStreaming;

/// <summary>
/// Extension methods for registering event streaming services
/// Follows Hartonomous infrastructure pattern
/// </summary>
public static class EventStreamingServiceExtensions
{
    /// <summary>
    /// Add Kafka CDC event streaming services to the container
    /// </summary>
    public static IServiceCollection AddHartonomousEventStreaming(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate configuration
        var bootstrapServers = configuration["Kafka:BootstrapServers"];
        if (string.IsNullOrEmpty(bootstrapServers))
            throw new ArgumentException("Kafka:BootstrapServers configuration is required");

        // Register the CDC event consumer as a hosted service
        services.AddSingleton<CdcEventConsumer>();
        services.AddHostedService<CdcEventConsumer>(provider => provider.GetRequiredService<CdcEventConsumer>());

        return services;
    }

    /// <summary>
    /// Add complete Hartonomous data fabric (Neo4j + Milvus + Kafka CDC)
    /// </summary>
    public static IServiceCollection AddHartonomousDataFabric(this IServiceCollection services, IConfiguration configuration)
    {
        // Add all data fabric components
        services.AddHartonomousNeo4j(configuration);
        services.AddHartonomousMilvus(configuration);
        services.AddHartonomousEventStreaming(configuration);

        // Add data fabric orchestrator
        services.AddSingleton<DataFabricOrchestrator>();

        // Add health checks
        services.AddHealthChecks()
            .AddCheck<HealthChecks.DataFabricHealthCheck>("data_fabric");

        return services;
    }
}