using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Milvus;

/// <summary>
/// Extension methods for registering SQL Server vector services
/// Implements NinaDB vector capabilities using SQL Server 2025 native VECTOR type
/// </summary>
public static class MilvusServiceExtensions
{
    /// <summary>
    /// Add SQL Server 2025 native vector database services to the container
    /// Replaces legacy Milvus dependency with integrated SQL Server solution
    /// Maintains backward compatibility with existing AddHartonomousMilvus calls
    /// </summary>
    public static IServiceCollection AddHartonomousMilvus(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate SQL Server connection string
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("DefaultConnection string is required for SQL Server vector operations");

        // Register SQL Server vector service as singleton for connection pooling
        // This replaces the old MilvusService registration
        services.AddSingleton<SqlServerVectorService>();

        // Note: Code must be updated to use SqlServerVectorService instead of MilvusService

        // Initialize vector tables on startup
        services.AddHostedService<VectorTableInitializationService>();

        return services;
    }

    /// <summary>
    /// Add SQL Server 2025 native vector database services to the container
    /// New method name for future use
    /// </summary>
    public static IServiceCollection AddHartonomousVectors(this IServiceCollection services, IConfiguration configuration)
    {
        return AddHartonomousMilvus(services, configuration);
    }
}

/// <summary>
/// Background service to initialize vector tables on application startup
/// </summary>
public class VectorTableInitializationService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly SqlServerVectorService _vectorService;
    private readonly Microsoft.Extensions.Logging.ILogger<VectorTableInitializationService> _logger;

    public VectorTableInitializationService(
        SqlServerVectorService vectorService,
        Microsoft.Extensions.Logging.ILogger<VectorTableInitializationService> logger)
    {
        _vectorService = vectorService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Initializing SQL Server vector tables...");
            await _vectorService.EnsureVectorTablesExistAsync();
            _logger.LogInformation("SQL Server vector tables initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SQL Server vector tables");
            throw;
        }
    }
}