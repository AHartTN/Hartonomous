# Zero-Trust Secret Management

## Philosophy

**Humans should NEVER see, store, or manage credentials.**

All secrets managed by:
- Azure Key Vault (storage)
- Managed Identities (access)
- Workload Identity Federation (pipeline)
- RBAC (permissions)

## One-Time Setup

Run this script **once** on either machine:

### Windows (HART-DESKTOP):
```powershell
.\scripts\setup-zero-trust.ps1
```

### Linux (HART-SERVER):
```bash
./scripts/setup-zero-trust.sh  # Coming soon - same functionality
```

### What It Creates:

1. **Azure Key Vault** (`hartonomous-kv`)
   - RBAC-enabled (no access policies)
   - Stores all secrets
   - Auto-rotation for certificates

2. **Managed Identity** (`hartonomous-identity`)
   - User-assigned identity
   - Granted Key Vault Secrets User role
   - Used by pipelines and applications

3. **Federated Credentials**
   - Azure DevOps workload identity
   - No passwords or service principals
   - Automatic token exchange

4. **Secrets** (Auto-Generated):
   - Database connection strings (all environments)
   - Redis connection strings (all environments)
   - JWT secrets
   - API keys
   - Encryption keys
   - Code signing certificate

## Application Integration

### Program.cs (ASP.NET Core):

```csharp
using Hartonomous.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add zero-trust configuration
builder.Services.AddZeroTrustConfiguration(
    builder.Configuration, 
    builder.Environment
);

// Get database connection from Key Vault
var connectionString = await builder.Configuration
    .GetDatabaseConnectionStringAsync(builder.Environment);

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString)
);

// Get Redis connection from Key Vault
var redisConnection = await builder.Configuration
    .GetRedisConnectionStringAsync(builder.Environment);

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnection;
});

var app = builder.Build();
app.Run();
```

### Local Development:

**No Key Vault required!** Uses connection strings from `appsettings.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Host=localhost;Port=5432;Database=hartonomous_local;Username=postgres;Password=postgres",
    "Redis": "localhost:6379"
  }
}
```

### Deployed Environments:

**Automatically uses Key Vault** with managed identity:
- DEV ? `PostgreSQL-Dev` secret
- Staging ? `PostgreSQL-Staging` secret
- Production ? `PostgreSQL-Production` secret

## Pipeline Integration

Pipeline automatically uses workload identity federation:

```yaml
- task: AzureKeyVault@2
  displayName: 'Get Secrets from Key Vault'
  inputs:
    azureSubscription: 'Azure-Service-Connection'  # Uses managed identity!
    KeyVaultName: 'hartonomous-kv'
    SecretsFilter: '*'  # Get all secrets
    RunAsPreJob: true
```

Secrets available as environment variables:
```yaml
- script: |
    echo "Database: $(PostgreSQL-Production)"
    dotnet run
  env:
    ConnectionStrings__DefaultConnection: $(PostgreSQL-Production)
```

## Secret Rotation

### Automatic (Certificates):
- Code signing cert auto-rotates 90 days before expiry
- No manual intervention

### Manual (Connection Strings):

```powershell
# Generate new password
$newPassword = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 32 | ForEach-Object {[char]$_})

# Update secret in Key Vault
az keyvault secret set \
    --vault-name hartonomous-kv \
    --name PostgreSQL-Production \
    --value "Host=prod-db.postgres.database.azure.com;Port=5432;Database=hartonomous;Username=hart_admin;Password=$newPassword;SSL Mode=Require"

# Update database password
psql -h prod-db.postgres.database.azure.com -U postgres -c "ALTER USER hart_admin PASSWORD '$newPassword';"

# Applications pick up new secret on next restart (cached for performance)
```

## Azure DevOps Service Connection Setup

### One-Time Manual Step:

1. Navigate to: https://dev.azure.com/aharttn/Hartonomous/_settings/adminservices

2. Click **"New service connection"** ? **"Azure Resource Manager"**

3. Select **"Workload Identity federation (automatic)"**

4. Fill in:
   - **Subscription:** Your Azure subscription
   - **Resource Group:** Hartonomous-RG
   - **Service connection name:** Azure-Service-Connection
   - ? **Grant access permission to all pipelines**

5. Click **"Save"**

**That's it!** Pipeline now uses managed identity with zero passwords.

## Security Architecture

```
???????????????????????????????????????????????????????
?           Azure Key Vault (hartonomous-kv)          ?
?                                                     ?
?  ????????????????????????????????????????????????  ?
?  ? Secrets:                                     ?  ?
?  ?  - PostgreSQL-Production (connection string)?  ?
?  ?  - Redis-Production (connection string)     ?  ?
?  ?  - JWT-Secret (auth tokens)                 ?  ?
?  ?  - API-Key-Internal (service-to-service)    ?  ?
?  ?  - Encryption-Key (data encryption)         ?  ?
?  ?  - HartIndustries-CodeSigning (certificate) ?  ?
?  ????????????????????????????????????????????????  ?
?                                                     ?
?  RBAC Permissions:                                  ?
?   ? Managed Identity: Key Vault Secrets User       ?
?   ? You: Key Vault Administrator                   ?
?   ? Pipeline: Automatic via Workload Identity      ?
???????????????????????????????????????????????????????
                        ?
                        ? Workload Identity Federation
                        ? (No passwords, automatic)
                        ?
???????????????????????????????????????????????????????
?        Azure DevOps Pipeline                        ?
?  - Uses service connection                          ?
?  - Federates with managed identity                  ?
?  - Retrieves secrets on demand                      ?
?  - Never stores secrets                             ?
???????????????????????????????????????????????????????
                        ?
                        ?
???????????????????????????????????????????????????????
?        Deployed Application                         ?
?  - Uses DefaultAzureCredential                      ?
?  - Automatically finds managed identity             ?
?  - Retrieves secrets at runtime                     ?
?  - Caches for performance                           ?
???????????????????????????????????????????????????????
```

## Benefits

### For Developers:
? Never see production credentials  
? Local dev works without Key Vault  
? Same code works everywhere  
? No credential management  

### For Operations:
? Centralized secret management  
? Audit logs for all access  
? Easy rotation (no code changes)  
? RBAC for fine-grained control  

### For Security:
? Zero credentials in code/config  
? No service principal passwords  
? Workload identity federation  
? Automatic compliance  

## Troubleshooting

### Local Development: "Unable to authenticate"

**Cause:** Not logged in to Azure CLI

**Fix:**
```bash
az login
az account set --subscription <your-subscription>
```

### Pipeline: "Secret not found"

**Cause:** Service connection not configured or lacks permissions

**Fix:**
1. Verify service connection exists: Azure-Service-Connection
2. Check managed identity has "Key Vault Secrets User" role
3. Verify secret name matches exactly (case-sensitive)

### Application: "Key Vault connection failed"

**Cause:** Managed identity not assigned or misconfigured

**Fix:**
```bash
# For App Service
az webapp identity assign --name hartonomous-api --resource-group Hartonomous-RG

# Grant Key Vault access
az role assignment create \
    --role "Key Vault Secrets User" \
    --assignee <managed-identity-principal-id> \
    --scope /subscriptions/<sub-id>/resourceGroups/Hartonomous-RG/providers/Microsoft.KeyVault/vaults/hartonomous-kv
```

## Cost

**Azure Key Vault:**
- Secret operations: $0.03 per 10,000
- Estimated: **$0.10 - $0.50/month**

**Managed Identity:**
- **FREE** (no additional cost)

**Workload Identity Federation:**
- **FREE** (no additional cost)

**Total: ~$0.50/month for enterprise-grade secret management**

