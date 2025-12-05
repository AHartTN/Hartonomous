# Azure Integration for Hartonomous

## Architecture

**Zero Trust Infrastructure:**
- Azure Arc-enabled servers (Linux & Windows)
- Managed Identity for authentication
- Azure Key Vault for secrets
- Entra ID (Azure AD) for user authentication
- External ID (B2C replacement) for customer authentication

## Setup Steps

### 1. Azure Key Vault Setup

```bash
cd deployment/azure
chmod +x setup-keyvault-secrets.sh grant-arc-keyvault-access.sh

# Create secrets in Key Vault
./setup-keyvault-secrets.sh

# Grant Arc server access
# Edit the script first to set your resource group and server names
./grant-arc-keyvault-access.sh
```

### 2. Application Configuration

The application automatically uses Azure Managed Identity when running on Arc-enabled servers.

**Key Vault Integration:**
- Vault URI: `https://hartonomous-kv.vault.azure.net/`
- Authentication: System-assigned Managed Identity (automatic)
- Secrets accessed: `PostgreSQL--Password`, `JWT--SecretKey`, etc.

### 3. Entra ID Integration

**API Authentication (Internal):**
```json
{
  "AzureAd": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<your-tenant-id>",
    "ClientId": "<api-app-registration-id>",
    "Audience": "api://hartonomous-api"
  }
}
```

**External ID (Customer Auth):**
```json
{
  "ExternalId": {
    "Instance": "https://login.microsoftonline.com/",
    "TenantId": "<external-id-tenant-id>",
    "ClientId": "<customer-app-registration-id>",
    "Authority": "https://<tenant>.ciamlogin.com/<tenant>.onmicrosoft.com/v2.0"
  }
}
```

### 4. Configure App Registrations

#### API App Registration
```bash
# Create API app registration
az ad app create \
    --display-name "Hartonomous API" \
    --identifier-uris "api://hartonomous-api" \
    --sign-in-audience AzureADMyOrg

# Expose API scope
az ad app permission add \
    --id <api-app-id> \
    --api <api-app-id> \
    --api-permissions access_as_user=Scope
```

#### Web App Registration
```bash
# Create Web app registration
az ad app create \
    --display-name "Hartonomous Web" \
    --web-redirect-uris "https://web.hartonomous.local/signin-oidc" \
    --sign-in-audience AzureADMyOrg

# Grant API permissions
az ad app permission add \
    --id <web-app-id> \
    --api <api-app-id> \
    --api-permissions access_as_user=Scope

# Admin consent
az ad app permission grant \
    --id <web-app-id> \
    --api <api-app-id>
```

### 5. Arc Server Configuration

Verify Arc connectivity:
```bash
# Check Arc agent status
sudo azcmagent show

# View managed identity
az connectedmachine show \
    --resource-group <rg-name> \
    --name hart-server \
    --query identity

# Test Key Vault access (from server)
az login --identity
az keyvault secret show \
    --vault-name hartonomous-kv \
    --name PostgreSQL--Password
```

## Secrets in Key Vault

| Secret Name | Description |
|------------|-------------|
| `PostgreSQL--Password` | Database user password |
| `PostgreSQL--ConnectionString` | Full connection string |
| `JWT--SecretKey` | JWT signing key |
| `DataProtection--Key` | ASP.NET Core data protection |
| `Redis--Password` | Redis password (if enabled) |
| `AzureAd--ClientSecret` | Service principal secret (if needed) |

## Accessing Secrets in Code

Secrets are automatically loaded via Azure Key Vault configuration provider:

```csharp
// Program.cs or Startup.cs
builder.Configuration.AddAzureKeyVault(
    new Uri(builder.Configuration["Azure:KeyVault:VaultUri"]),
    new DefaultAzureCredential());

// Access secrets
var connectionString = builder.Configuration["PostgreSQL--ConnectionString"];
var jwtSecret = builder.Configuration["JWT--SecretKey"];
```

## Security Best Practices

1. **Managed Identity Only**: Never use service principals with secrets on Arc servers
2. **Least Privilege**: Grant only "Get" and "List" permissions on Key Vault
3. **Secret Rotation**: Rotate secrets regularly via Key Vault
4. **Audit Logging**: Enable Key Vault diagnostic logs
5. **Network Security**: Use Private Endpoints for Key Vault access
6. **RBAC**: Use Azure RBAC instead of access policies (newer approach)

## Monitoring

**Key Vault Access:**
```bash
# View recent access logs
az monitor activity-log list \
    --resource-group <rg-name> \
    --resource-type Microsoft.KeyVault/vaults \
    --start-time 2025-12-04T00:00:00Z
```

**Arc Agent Status:**
```bash
# Agent logs
sudo journalctl -u himdsd -n 100

# Managed identity token test
curl -H "Metadata:true" \
    "http://localhost:40342/metadata/identity/oauth2/token?api-version=2020-06-01&resource=https://vault.azure.net"
```

## Troubleshooting

**Managed Identity not working:**
```bash
# Restart Arc agent
sudo systemctl restart himdsd

# Check identity configuration
az connectedmachine show \
    --resource-group <rg-name> \
    --name hart-server \
    --query identity
```

**Key Vault access denied:**
```bash
# Verify access policy
az keyvault show \
    --name hartonomous-kv \
    --query properties.accessPolicies

# Re-grant access
az keyvault set-policy \
    --name hartonomous-kv \
    --object-id <principal-id> \
    --secret-permissions get list
```

**Application can't load secrets:**
- Verify Key Vault URI in appsettings.Production.json
- Check that Azure.Identity NuGet package is installed
- Ensure DefaultAzureCredential() is used (supports managed identity)
- Check application logs for authentication errors
