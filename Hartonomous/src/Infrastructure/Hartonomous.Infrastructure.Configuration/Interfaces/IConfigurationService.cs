using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Hartonomous.Infrastructure.Configuration.Interfaces;

/// <summary>
/// Interface for secure configuration management
/// Provides abstraction over configuration storage and secret management
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// Get connection string securely
    /// </summary>
    Task<string> GetConnectionStringAsync(string name);

    /// <summary>
    /// Get secret value from secure storage (Azure Key Vault, etc.)
    /// </summary>
    Task<string> GetSecretAsync(string secretName);

    /// <summary>
    /// Get configuration value with optional default
    /// </summary>
    Task<string?> GetConfigurationValueAsync(string key, string? defaultValue = null);

    /// <summary>
    /// Get strongly-typed configuration section
    /// </summary>
    Task<T> GetConfigurationSectionAsync<T>(string sectionName) where T : class, new();

    /// <summary>
    /// Set configuration value (for administrative operations)
    /// </summary>
    Task SetConfigurationValueAsync(string key, string value);

    /// <summary>
    /// Validate configuration integrity
    /// </summary>
    Task<ConfigurationValidationResult> ValidateConfigurationAsync();

    /// <summary>
    /// Reload configuration from all sources
    /// </summary>
    Task ReloadConfigurationAsync();

    /// <summary>
    /// Get database configuration for specific service
    /// </summary>
    Task<DatabaseConfiguration> GetDatabaseConfigurationAsync(string serviceName);

    /// <summary>
    /// Get external service configuration with credentials
    /// </summary>
    Task<ExternalServiceConfiguration> GetExternalServiceConfigurationAsync(string serviceName);
}

/// <summary>
/// Database configuration with secure connection details
/// </summary>
public class DatabaseConfiguration
{
    public string ConnectionString { get; set; } = string.Empty;
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int ConnectionTimeout { get; set; } = 30;
    public int CommandTimeout { get; set; } = 120;
    public bool EnableRetryOnFailure { get; set; } = true;
    public int MaxRetryCount { get; set; } = 3;
}

/// <summary>
/// External service configuration with secure credentials
/// </summary>
public class ExternalServiceConfiguration
{
    public string ServiceUrl { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public string? Username { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
    public int MaxRetries { get; set; } = 3;
    public Dictionary<string, string> CustomHeaders { get; set; } = new();
    public Dictionary<string, string> CustomProperties { get; set; } = new();
}

/// <summary>
/// Configuration validation result
/// </summary>
public class ConfigurationValidationResult
{
    public bool IsValid { get; set; }
    public List<string> Errors { get; set; } = new();
    public List<string> Warnings { get; set; } = new();
    public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
}