using Azure.Identity;
using Azure.Security.KeyVault.Secrets;

namespace Hartonomous.Infrastructure.Configuration;

/// <summary>
/// Zero-trust configuration using Azure Key Vault with Managed Identity.
/// NO PASSWORDS IN CODE OR CONFIG FILES.
/// </summary>
public static class SecureConfiguration
{
    private static SecretClient? _secretClient;
    private static readonly Dictionary<string, string> _cache = new();

    /// <summary>
    /// Initialize Key Vault client with Managed Identity (zero credentials)
    /// </summary>
    public static void Initialize(string keyVaultName)
    {
        var keyVaultUri = new Uri($"https://{keyVaultName}.vault.azure.net/");
        
        // DefaultAzureCredential automatically uses:
        // 1. Environment variables (local dev)
        // 2. Managed Identity (Azure/production)
        // 3. Visual Studio/Azure CLI (local dev)
        // 4. Azure PowerShell (local dev)
        var credential = new DefaultAzureCredential();
        
        _secretClient = new SecretClient(keyVaultUri, credential);
    }

    /// <summary>
    /// Get secret from Key Vault (cached for performance)
    /// </summary>
    public static async Task<string> GetSecretAsync(string secretName)
    {
        if (_secretClient == null)
            throw new InvalidOperationException("SecureConfiguration not initialized. Call Initialize() first.");

        // Check cache
        if (_cache.TryGetValue(secretName, out var cachedValue))
            return cachedValue;

        // Retrieve from Key Vault
        var secret = await _secretClient.GetSecretAsync(secretName);
        var value = secret.Value.Value;

        // Cache for performance (secrets rarely change)
        _cache[secretName] = value;

        return value;
    }

    /// <summary>
    /// Get connection string from Key Vault based on environment
    /// </summary>
    public static async Task<string> GetConnectionStringAsync(string environment)
    {
        var secretName = environment switch
        {
            "Local" => "PostgreSQL-Local",
            "Development" => "PostgreSQL-Dev",
            "Staging" => "PostgreSQL-Staging",
            "Production" => "PostgreSQL-Production",
            _ => throw new ArgumentException($"Unknown environment: {environment}")
        };

        return await GetSecretAsync(secretName);
    }

    /// <summary>
    /// Get Redis connection string from Key Vault
    /// </summary>
    public static async Task<string> GetRedisConnectionStringAsync(string environment)
    {
        var secretName = environment switch
        {
            "Local" => "Redis-Local",
            "Development" => "Redis-Dev",
            "Staging" => "Redis-Staging",
            "Production" => "Redis-Production",
            _ => throw new ArgumentException($"Unknown environment: {environment}")
        };

        return await GetSecretAsync(secretName);
    }

    /// <summary>
    /// Clear cache (useful for testing or when secrets are rotated)
    /// </summary>
    public static void ClearCache()
    {
        _cache.Clear();
    }
}
