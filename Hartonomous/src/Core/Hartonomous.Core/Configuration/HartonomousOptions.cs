using System.ComponentModel.DataAnnotations;

namespace Hartonomous.Core.Configuration;

/// <summary>
/// Core database connection options
/// </summary>
public class DatabaseOptions
{
    public const string SectionName = "Database";

    [Required]
    public string DefaultConnection { get; set; } = string.Empty;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    public bool EnableRetryOnFailure { get; set; } = true;

    [Range(1, 10)]
    public int MaxRetryCount { get; set; } = 3;
}

/// <summary>
/// Azure AD authentication options
/// </summary>
public class AzureAdOptions
{
    public const string SectionName = "AzureAd";

    [Required]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    [Required]
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
}

/// <summary>
/// JWT token options
/// </summary>
public class JwtOptions
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = string.Empty;

    [Required]
    public string Audience { get; set; } = string.Empty;

    [Required]
    public string Key { get; set; } = string.Empty;

    [Range(1, 1440)]
    public int ExpiryMinutes { get; set; } = 60;
}

/// <summary>
/// Key Vault configuration options
/// </summary>
public class KeyVaultOptions
{
    public const string SectionName = "KeyVault";

    public string VaultUrl { get; set; } = string.Empty;

    public bool EnableInDevelopment { get; set; } = false;

    public bool EnableInTesting { get; set; } = false;
}

/// <summary>
/// Neo4j connection options
/// </summary>
public class Neo4jOptions
{
    public const string SectionName = "Neo4j";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public string Username { get; set; } = "neo4j";

    public string Password { get; set; } = string.Empty;

    public string Database { get; set; } = "hartonomous";

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// SQL Server Vector database options for SQL Server 2025 VECTOR operations
/// </summary>
public class VectorDatabaseOptions
{
    public const string SectionName = "VectorDatabase";

    [Required]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)]
    public int Port { get; set; } = 19530;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    [Range(1, 300)]
    public int TimeoutSeconds { get; set; } = 30;
}

/// <summary>
/// Model distillation engine options
/// </summary>
public class DistillationOptions
{
    public const string SectionName = "Distillation";

    [Range(1, 100)]
    public int BatchSize { get; set; } = 32;

    [Range(0.0, 1.0)]
    public double SparsityThreshold { get; set; } = 0.1;

    [Range(1, 1000)]
    public int MaxIterations { get; set; } = 100;

    public bool EnableWandaPruning { get; set; } = true;

    public bool EnableSkipTranscoders { get; set; } = true;

    public string TempDirectory { get; set; } = Path.GetTempPath();
}

/// <summary>
/// Azure App Configuration options
/// </summary>
public class AzureAppConfigOptions
{
    public const string SectionName = "AzureAppConfig";

    public string ConnectionString { get; set; } = string.Empty;

    public string Endpoint { get; set; } = string.Empty;

    public bool EnableInDevelopment { get; set; } = false;

    public string Label { get; set; } = string.Empty;

    [Range(1, 3600)]
    public int CacheExpirationSeconds { get; set; } = 300;

    public bool UseFeatureFlags { get; set; } = true;
}

/// <summary>
/// Azure Entra External ID options
/// </summary>
public class EntraExternalIdOptions
{
    public const string SectionName = "EntraExternalId";

    [Required]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    public string ClientSecret { get; set; } = string.Empty;

    [Required]
    public string Instance { get; set; } = "https://login.microsoftonline.com/";

    public string Authority { get; set; } = string.Empty;

    public string SignUpSignInPolicyId { get; set; } = string.Empty;

    public string EditProfilePolicyId { get; set; } = string.Empty;

    public string ResetPasswordPolicyId { get; set; } = string.Empty;
}

/// <summary>
/// Azure Service Principal options
/// </summary>
public class ServicePrincipalOptions
{
    public const string SectionName = "ServicePrincipal";

    [Required]
    public string TenantId { get; set; } = string.Empty;

    [Required]
    public string ClientId { get; set; } = string.Empty;

    [Required]
    public string ClientSecret { get; set; } = string.Empty;

    public string CertificateThumbprint { get; set; } = string.Empty;

    public string CertificatePath { get; set; } = string.Empty;

    public bool UseManagedIdentity { get; set; } = false;
}

/// <summary>
/// SQL Server configuration options
/// </summary>
public class SqlServerOptions
{
    public const string SectionName = "SqlServer";

    [Required]
    public string ConnectionString { get; set; } = string.Empty;

    public string Server { get; set; } = "localhost";

    public string Database { get; set; } = "HartonomousDB";

    public bool TrustedConnection { get; set; } = true;

    public bool MultipleActiveResultSets { get; set; } = true;

    public bool TrustServerCertificate { get; set; } = true;

    [Range(1, 300)]
    public int CommandTimeoutSeconds { get; set; } = 30;

    [Range(1, 1000)]
    public int MaxPoolSize { get; set; } = 100;

    public bool EnableFileStreamAccess { get; set; } = true;

    public string FileStreamDirectory { get; set; } = @"D:\HartonomousData";

    public bool EnableRetryOnFailure { get; set; } = true;

    public int MaxRetryCount { get; set; } = 3;
}

/// <summary>
/// MCP (Multi-Context Protocol) options
/// </summary>
public class McpOptions
{
    public const string SectionName = "MCP";

    [Range(1, 65535)]
    public int Port { get; set; } = 8080;

    [Range(1, 10000)]
    public int MaxAgents { get; set; } = 100;

    [Range(1, 300)]
    public int HeartbeatIntervalSeconds { get; set; } = 30;

    [Range(1, 1440)]
    public int AgentTimeoutMinutes { get; set; } = 60;

    public bool EnableDiscovery { get; set; } = true;

    public string HubPath { get; set; } = "/mcpHub";

    public bool EnableSignalR { get; set; } = true;
}