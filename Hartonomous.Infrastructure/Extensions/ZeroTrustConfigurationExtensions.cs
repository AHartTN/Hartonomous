using Hartonomous.Infrastructure.Configuration;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hartonomous.Infrastructure.Extensions;

/// <summary>
/// Extension methods for adding zero-trust configuration to services
/// </summary>
public static class ZeroTrustConfigurationExtensions
{
    /// <summary>
    /// Add zero-trust Key Vault configuration
    /// </summary>
    public static IServiceCollection AddZeroTrustConfiguration(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Get Key Vault name from configuration
        var keyVaultName = configuration["KeyVault:Name"] ?? "hartonomous-kv";
        
        // Initialize secure configuration
        SecureConfiguration.Initialize(keyVaultName);
        
        return services;
    }
    
    /// <summary>
    /// Configure database connection using Key Vault
    /// </summary>
    public static async Task<string> GetDatabaseConnectionStringAsync(
        this IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Use Key Vault for non-local environments
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Local"))
        {
            return await SecureConfiguration.GetConnectionStringAsync(environment.EnvironmentName);
        }
        
        // Fall back to configuration for local development
        return configuration.GetConnectionString("DefaultConnection") 
            ?? "Host=localhost;Port=5432;Database=hartonomous_local;Username=postgres;Password=postgres";
    }
    
    /// <summary>
    /// Configure Redis connection using Key Vault
    /// </summary>
    public static async Task<string> GetRedisConnectionStringAsync(
        this IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Use Key Vault for non-local environments
        if (!environment.IsDevelopment() && !environment.IsEnvironment("Local"))
        {
            return await SecureConfiguration.GetRedisConnectionStringAsync(environment.EnvironmentName);
        }
        
        // Fall back to configuration for local development
        return configuration.GetConnectionString("Redis") ?? "localhost:6379";
    }
}
