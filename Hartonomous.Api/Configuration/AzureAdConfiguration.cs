namespace Hartonomous.Api.Configuration;

public class AzureAdConfiguration
{
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string TenantId { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string[] Scopes { get; set; } = Array.Empty<string>();
}

public class AzureConfiguration
{
    public string KeyVaultUri { get; set; } = string.Empty;
    public string AppConfigurationUri { get; set; } = string.Empty;
    public bool UseManagedIdentity { get; set; } = true;
}
