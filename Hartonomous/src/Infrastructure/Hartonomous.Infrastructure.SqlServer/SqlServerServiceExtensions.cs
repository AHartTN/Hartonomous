using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Hartonomous.DataFabric.Abstractions;

namespace Hartonomous.Infrastructure.SqlServer;

/// <summary>
/// Extension methods for registering SQL Server services
/// Implements vector capabilities and FILESTREAM operations for large model storage
/// </summary>
public static class SqlServerServiceExtensions
{
    /// <summary>
    /// Add SQL Server 2025 native vector database services to the container
    /// Implements native VECTOR data type capabilities for modern vector operations
    /// </summary>
    public static IServiceCollection AddHartonomousSqlServerVector(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate SQL Server connection string
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("DefaultConnection string is required for SQL Server vector operations");

        // Register SQL Server vector service as singleton for connection pooling
        services.AddSingleton<SqlServerVectorService>();

        // Register modern EF Core SqlVector query service for Database.SqlQuery<T> operations
        services.AddScoped<SqlVectorQueryService>();

        // Initialize vector tables on startup
        services.AddHostedService<VectorTableInitializationService>();

        return services;
    }

    /// <summary>
    /// Add SQL Server FILESTREAM services for large model storage
    /// Provides efficient storage and streaming access to multi-GB model files
    /// </summary>
    public static IServiceCollection AddHartonomousSqlServerFileStream(this IServiceCollection services, IConfiguration configuration)
    {
        // Validate SQL Server connection string
        var connectionString = configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            throw new ArgumentException("DefaultConnection string is required for SQL Server FILESTREAM operations");

        // Register FILESTREAM service implementation
        services.AddScoped<IModelDataService, SqlServerFileStreamService>();

        // Initialize FILESTREAM tables on startup
        services.AddHostedService<FileStreamTableInitializationService>();

        return services;
    }

    /// <summary>
    /// Add complete SQL Server data fabric services (Vector + FILESTREAM)
    /// Recommended for full Hartonomous data operations
    /// </summary>
    public static IServiceCollection AddHartonomousDataFabric(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHartonomousSqlServerVector(configuration);
        services.AddHartonomousSqlServerFileStream(configuration);
        return services;
    }

    /// <summary>
    /// Add SQL Server 2025 native vector database services to the container
    /// Modern method name for vector operations
    /// </summary>
    public static IServiceCollection AddHartonomousVectors(this IServiceCollection services, IConfiguration configuration)
    {
        return AddHartonomousSqlServerVector(services, configuration);
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

/// <summary>
/// Background service to initialize FILESTREAM tables on application startup
/// </summary>
public class FileStreamTableInitializationService : Microsoft.Extensions.Hosting.BackgroundService
{
    private readonly IConfiguration _configuration;
    private readonly Microsoft.Extensions.Logging.ILogger<FileStreamTableInitializationService> _logger;

    public FileStreamTableInitializationService(
        IConfiguration configuration,
        Microsoft.Extensions.Logging.ILogger<FileStreamTableInitializationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Initializing SQL Server FILESTREAM tables...");
            await EnsureFileStreamTablesExistAsync();
            _logger.LogInformation("SQL Server FILESTREAM tables initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize SQL Server FILESTREAM tables");
            throw;
        }
    }

    private async Task EnsureFileStreamTablesExistAsync()
    {
        var connectionString = _configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrEmpty(connectionString))
            throw new InvalidOperationException("DefaultConnection string is required");

        using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
        await connection.OpenAsync();

        // Check if FILESTREAM is enabled
        var checkFileStreamQuery = @"
            SELECT SERVERPROPERTY('FilestreamConfiguredLevel') AS ConfiguredLevel,
                   SERVERPROPERTY('FilestreamEffectiveLevel') AS EffectiveLevel";

        using var checkCommand = new Microsoft.Data.SqlClient.SqlCommand(checkFileStreamQuery, connection);
        using var reader = await checkCommand.ExecuteReaderAsync();

        if (await reader.ReadAsync())
        {
            var configuredLevel = reader.GetValue("ConfiguredLevel");
            var effectiveLevel = reader.GetValue("EffectiveLevel");

            if (configuredLevel.ToString() == "0" || effectiveLevel.ToString() == "0")
            {
                _logger.LogWarning("FILESTREAM is not enabled on SQL Server instance. Model file storage will use regular storage.");
                return;
            }
        }

        reader.Close();

        // Check if FILESTREAM tables exist
        var checkTablesQuery = @"
            SELECT COUNT(*) FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_NAME IN ('ModelFiles', 'ModelProcessingCache', 'ModelStreamingSessions')";

        using var tablesCommand = new Microsoft.Data.SqlClient.SqlCommand(checkTablesQuery, connection);
        var tableCount = (int)await tablesCommand.ExecuteScalarAsync();

        if (tableCount < 3)
        {
            _logger.LogInformation("FILESTREAM tables not found. Please run the filestream_setup.sql script to create required tables.");
        }
        else
        {
            _logger.LogInformation("FILESTREAM tables verified successfully");
        }
    }
}