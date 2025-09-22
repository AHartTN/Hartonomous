using Azure.Extensions.AspNetCore.Configuration.Secrets;
using Azure.Identity;
using Azure.Security.KeyVault.Secrets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.AzureAppConfiguration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Hartonomous.Infrastructure.Configuration;

public static class KeyVaultConfigurationExtensions
{
    /// <summary>
    /// Add Azure App Configuration with Key Vault integration for Hartonomous services
    /// </summary>
    public static IConfigurationBuilder AddHartonomousAzureConfiguration(
        this IConfigurationBuilder builder,
        IHostEnvironment environment)
    {
        try
        {
            // Always add Azure App Configuration in production and staging
            if (!environment.IsDevelopment())
            {
                builder.AddAzureAppConfiguration(options =>
                {
                    // Use read-only connection string for security
                    options.Connect("Endpoint=https://appconfig-hartonomous.azconfig.io;Id=l4P/;Secret=DOlsqWfUXC3XlKXAMfJ1RANELSl1nM88NbCSN77L0PvqqrqbJkt8JQQJ99BHACYeBjFvJCJLAABAZAC2PBB")
                           .ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential()))
                           .ConfigureRefresh(refreshOptions =>
                           {
                               // Refresh configuration every 30 seconds for dynamic updates
                               refreshOptions.SetRefreshInterval(TimeSpan.FromSeconds(30));
                           });
                });
            }
            else
            {
                // In development, optionally use Azure App Configuration if available
                var useAppConfig = builder.Build()["UseAzureAppConfiguration"];
                if (bool.TryParse(useAppConfig, out var shouldUse) && shouldUse)
                {
                    builder.AddAzureAppConfiguration(options =>
                    {
                        options.Connect("Endpoint=https://appconfig-hartonomous.azconfig.io;Id=l4P/;Secret=DOlsqWfUXC3XlKXAMfJ1RANELSl1nM88NbCSN77L0PvqqrqbJkt8JQQJ99BHACYeBjFvJCJLAABAZAC2PBB")
                               .ConfigureKeyVault(kv => kv.SetCredential(new DefaultAzureCredential()));
                    });
                }
            }

            // Fallback to Key Vault for development or if App Config fails
            return AddHartonomousKeyVault(builder, environment);
        }
        catch (Exception ex)
        {
            // Log error but continue - fallback to local configuration
            Console.WriteLine($"Warning: Failed to configure Azure App Configuration: {ex.Message}");
            return AddHartonomousKeyVault(builder, environment);
        }
    }

    public static IConfigurationBuilder AddHartonomousKeyVault(
        this IConfigurationBuilder builder,
        IHostEnvironment environment)
    {
        try
        {
            // Always skip Key Vault in development
            if (environment.IsDevelopment())
            {
                return builder;
            }

            // Only use Key Vault in production or when explicitly configured
            var keyVaultUrl = builder.Build()["KeyVault:VaultUrl"];

            if (string.IsNullOrEmpty(keyVaultUrl))
            {
                // Fallback for development - Key Vault URL should be in local config
                return builder;
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
        { "Neo4j-Password", "Neo4j:Password" }
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