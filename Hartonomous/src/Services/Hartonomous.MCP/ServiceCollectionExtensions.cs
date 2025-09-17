using Hartonomous.Core.Interfaces;
using Hartonomous.MCP.Repositories;

namespace Hartonomous.MCP;

/// <summary>
/// Extension methods for configuring MCP services
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Add Hartonomous MCP services to the dependency injection container
    /// </summary>
    public static IServiceCollection AddHartonomousMcp(this IServiceCollection services)
    {
        // Register repositories
        services.AddScoped<IAgentRepository, AgentRepository>();
        services.AddScoped<IMessageRepository, MessageRepository>();
        services.AddScoped<IWorkflowRepository, WorkflowRepository>();

        // Add SignalR
        services.AddSignalR(options =>
        {
            options.EnableDetailedErrors = true;
            options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
            options.StreamBufferCapacity = 10;
        });

        return services;
    }
}