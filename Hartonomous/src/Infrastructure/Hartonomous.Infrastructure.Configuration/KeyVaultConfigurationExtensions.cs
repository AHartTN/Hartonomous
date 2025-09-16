using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Hartonomous.Infrastructure.Configuration;

public static class KeyVaultConfigurationExtensions
{
    public static IConfigurationBuilder AddHartonomousKeyVault(
        this IConfigurationBuilder builder,
        IHostEnvironment environment)
    {
        try
        {
            // Only use Key Vault in production or when explicitly configured
            var keyVaultUrl = builder.Build()["KeyVault:VaultUrl"];

            if (string.IsNullOrEmpty(keyVaultUrl))
            {
                // Fallback for development - Key Vault URL should be in local config
                return builder;
            }

            // Skip Key Vault in development environment unless explicitly enabled
            if (environment.IsDevelopment())
            {
                var enableKeyVault = builder.Build()["KeyVault:EnableInDevelopment"];
                if (string.IsNullOrEmpty(enableKeyVault) || !bool.Parse(enableKeyVault))
                {
                    return builder;
                }
            }

            var credential = new DefaultAzureCredential();
            var secretClient = new SecretClient(new Uri(keyVaultUrl), credential);

            // Add Key Vault configuration with secret refresh options
            builder.AddAzureKeyVault(secretClient, new KeyVaultSecretManager());
        }
        catch (Exception)
        {
            // Silently fail in development - Key Vault access is optional
            if (!environment.IsProduction())
            {
                // Continue without Key Vault configuration
            }
            else
            {
                throw; // Re-throw in production environments
            }
        }

        return builder;
    }
}

public class KeyVaultSecretManager : Azure.Extensions.AspNetCore.Configuration.Secrets.KeyVaultSecretManager
{
    private readonly Dictionary<string, string> _secretMappings = new()
    {
        { "Azure-AD-ClientSecret", "Azure:AzureAD:ClientSecret" },
        { "Entra-ExternalId-ClientSecret", "Azure:EntraExternalId:ClientSecret" },
        { "SqlServer-ConnectionString", "ConnectionStrings:DefaultConnection" },
        { "Neo4j-Password", "Neo4j:Password" },
        { "Milvus-Token", "Milvus:Token" },
        { "Milvus-Password", "Milvus:Password" }
    };

    public override string GetKey(KeyVaultSecret secret)
    {
        // Map Key Vault secret names to configuration keys
        if (_secretMappings.TryGetValue(secret.Name, out var configKey))
        {
            return configKey;
        }

        // Default behavior for unmapped secrets
        return base.GetKey(secret);
    }

    public override bool Load(SecretProperties secret)
    {
        // Only load secrets that we explicitly map
        return _secretMappings.ContainsKey(secret.Name);
    }
}