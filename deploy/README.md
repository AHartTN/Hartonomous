# Hartonomous CI/CD Infrastructure Setup

**Complete automated setup for Azure DevOps pipelines with Arc-connected on-premises machines.**

Zero manual credential configuration. Everything managed through Azure Key Vault, App Configuration, and managed identities.

---

## 🚀 Quick Start

### Prerequisites

1. **Azure CLI** installed and authenticated:
   ```powershell
   az login
   az account set --subscription "<your-subscription-id>"
   ```

2. **Azure DevOps PAT** (only needed for deployment group setup):
   - Create at: `https://dev.azure.com/<org>/_usersSettings/tokens`
   - Scopes needed: `Deployment Group (Read & Manage)`, `Variable Groups (Read, Create & Manage)`
   - Store it:
     ```powershell
     $env:AZURE_DEVOPS_EXT_PAT = "<your-pat>"
     ```

3. **Arc-connected machines** (optional, for deployment):
   - Machines must be Azure Arc-enabled with system-assigned managed identity
   - Verify: `az connectedmachine list --resource-group rg-hartonomous -o table`
   - Note: Arc machines (hart-server, HART-DESKTOP) are in `rg-hartonomous`, not `Hartonomous-RG`

### One-Command Setup

```powershell
# Navigate to deploy directory
cd deploy

# Complete setup (everything except deployment group)
./Complete-Setup.ps1

# Or with deployment group registration
./Complete-Setup.ps1 -SetupDeploymentGroup -ArcMachineNames "hart-server,hart-desktop"
```

That's it. Seriously. **No other manual steps required.**

---

## 📦 What Gets Created

### Azure Resources

| Resource | Purpose | Authentication |
|----------|---------|----------------|
| **Key Vault** | Stores NuGet feed URLs, connection strings, service principal credentials | RBAC (Secrets User role) |
| **App Configuration** | Centralized application settings, feature flags, environment configs | RBAC (Data Reader role) |
| **Azure Artifacts Feed** | Private NuGet package repository | Managed Identity tokens |
| **Service Principal** | Pipeline authentication to Azure resources | Credentials in Key Vault |

### RBAC Assignments

Automatically configured for:
- **Pipeline service connection** → Key Vault Secrets User, App Config Data Reader
- **Arc machine managed identities** → Key Vault Secrets User, App Config Data Reader
- **Current user** → Key Vault Administrator, App Config Data Owner

### Azure DevOps Configuration

- **Variable Group**: `Hartonomous-Infrastructure` with Key Vault integration
- **Deployment Group**: `Hartonomous-Agents` with Arc machines registered (if `-SetupDeploymentGroup`)
- **Service Connection**: Uses existing `Azure-Service-Connection`

---

## Architecture Overview

**Linux Server (hart-server):**
- `/srv/www/hartonomous/api` - ASP.NET Core API
- `/srv/www/hartonomous/worker` - Background Worker
- PostgreSQL (256GB), Redis (128GB), MSSQL (128GB), Neo4j (128GB)

**Windows Desktop (hart-desktop):**
- `D:\inetpub\hartonomous\web` - Blazor Web App (IIS)

**Azure Integration:**
- Azure Arc-enabled servers (both Linux & Windows)
- Azure Key Vault for secrets (Managed Identity auth)
- Entra ID for internal authentication
- External ID for customer authentication
- Azure DevOps Pipelines with "Local Agent Pool"

---

## 🔐 How Authentication Works

### Pipeline Execution

1. Pipeline runs on Arc-connected agent (or Azure-hosted agent)
2. Uses **managed identity** to authenticate to Azure
3. Retrieves secrets from Key Vault automatically via `AzureKeyVault@2` task
4. Gets **Azure DevOps access token** via managed identity:
   ```powershell
   az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798
   ```
5. Configures NuGet authentication with token (no credential provider installation needed)
6. Publishes packages to Azure Artifacts using managed identity

### Local Development

To authenticate locally for package restore:

```powershell
# Install Azure Artifacts Credential Provider (one-time)
iex "& { $(irm https://aka.ms/install-artifacts-credprovider.ps1) }"

# Authenticate (uses Azure CLI credentials automatically)
dotnet restore
```

Or manually authenticate:
```powershell
$feedUrl = az keyvault secret show --vault-name <kv-name> --name "NuGetFeedUrl" --query value -o tsv
$token = az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv

# Use token in NuGet.config or environment variable
```

---

## 🛠️ Manual Setup Scripts (If Needed)

Individual components can be set up separately:

### 1. Azure Infrastructure Only
```powershell
./Setup-AzureInfrastructure.ps1 `
    -ResourceGroupName "rg-hartonomous" `
    -Location "eastus" `
    -ArcMachineNames "hart-server,hart-desktop"
```

Creates:
- Key Vault with secrets
- App Configuration with settings
- Azure Artifacts feed
- RBAC for Arc machines and service principal

### 2. Pipeline Variable Group Only
```powershell
./Setup-PipelineVariableGroup.ps1 `
    -KeyVaultName "kv-hart-abc123" `
    -ResourceGroupName "rg-hartonomous"
```

Links pipeline to Key Vault for automatic secret injection.

### 3. Deployment Group Registration Only
```powershell
./Register-DeploymentGroupTargets.ps1 `
    -KeyVaultName "kv-hart-abc123" `
    -MachineNames "hart-server,hart-desktop" `
    -ResourceGroupName "rg-hartonomous"
```

Registers Arc machines as deployment targets with automatic agent installation.

---

## 🔍 Verification

### Check Key Vault Secrets
```powershell
$kvName = az keyvault list --resource-group rg-hartonomous --query "[0].name" -o tsv
az keyvault secret list --vault-name $kvName --query '[].name' -o table
```

Expected secrets:
- `NuGetFeedUrl`
- `AzureDevOpsOrgUrl`
- `AzureDevOpsProject`
- `PipelineServicePrincipalAppId`
- `PipelineServicePrincipalPassword`
- `PipelineServicePrincipalTenant`

### Check App Configuration Settings
```powershell
$appConfigName = az appconfig list --resource-group rg-hartonomous --query "[0].name" -o tsv
az appconfig kv list --name $appConfigName --query '[].{Key:key, Value:value}' -o table
```

Expected settings:
- `Hartonomous:NuGet:FeedName`
- `Hartonomous:NuGet:FeedUrl`
- `Hartonomous:Azure:KeyVaultName`
- `Hartonomous:Azure:AppConfigName`
- `Hartonomous:Package:Version`

### Check Pipeline Variable Group
```powershell
az pipelines variable-group list --organization https://dev.azure.com/aharttn --project Hartonomous -o table
```

Should show: `Hartonomous-Infrastructure`

### Check Deployment Group
```powershell
az pipelines deployment-group list --organization https://dev.azure.com/aharttn --project Hartonomous -o table
```

Should show: `Hartonomous-Agents` with registered targets

---

## 🔄 Pipeline Flow

### Build Stage
1. ✓ Authenticate using agent's managed identity
2. ✓ Build native C++ libraries (Windows/Linux)
3. ✓ Build .NET projects
4. ✓ Run tests with native library binaries

### Package Stage (main branch or tags only)
1. ✓ Retrieve NuGet feed URL from Key Vault
2. ✓ Configure NuGet auth using managed identity token
3. ✓ Build and pack native library with multi-platform binaries
4. ✓ Build and pack C# libraries (Core, Data, Infrastructure)
5. ✓ **Automatically push to Azure Artifacts** (no manual publish)
6. ✓ Publish pipeline artifacts

### Deploy Stage (optional)
1. ✓ Arc machines use their managed identity
2. ✓ Download artifacts from pipeline
3. ✓ Deploy to on-premises servers via Azure Arc Run Command

**NO MANUAL STEPS. NO CREDENTIALS TO MANAGE. NO BULLSHIT.**

---

## 🐛 Troubleshooting

### "Key Vault not found" error
```powershell
# Verify Key Vault exists
az keyvault list --resource-group Hartonomous-RG -o table

# Check RBAC assignments
az role assignment list --scope /subscriptions/<sub-id>/resourceGroups/Hartonomous-RG/providers/Microsoft.KeyVault/vaults/<kv-name> -o table
```

### "Unable to authenticate to Azure Artifacts" error
```powershell
# Check if managed identity has correct permissions
$identityId = az account show --query user.name -o tsv
az role assignment list --assignee $identityId -o table

# Verify token can be obtained
az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798
```

### "Variable group not linked to Key Vault" error
```powershell
# Re-run variable group setup
./Setup-PipelineVariableGroup.ps1 -KeyVaultName <kv-name>

# Or manually link in Azure DevOps:
# Pipelines → Library → Hartonomous-Infrastructure → Link secrets from Azure Key Vault
```

### Arc machine can't access Key Vault
```powershell
# Check if system-assigned managed identity is enabled
az connectedmachine show --name hart-server --resource-group rg-hartonomous --query identity

# Enable if needed
az connectedmachine update --name hart-server --resource-group rg-hartonomous --enable-identity

# Verify RBAC
az role assignment list --assignee <managed-identity-id> -o table
```

---

## 📚 Reference

### Azure CLI Commands

```powershell
# List all resources in resource group
az resource list --resource-group rg-hartonomous -o table

# Get Key Vault secret
az keyvault secret show --vault-name <kv-name> --name <secret-name> --query value -o tsv

# Update App Configuration setting
az appconfig kv set --name <appconfig-name> --key "Hartonomous:Setting" --value "new-value" --yes

# View pipeline runs
az pipelines runs list --organization https://dev.azure.com/aharttn --project Hartonomous -o table

# Get managed identity token
az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798 --query accessToken -o tsv
```

### Key URLs

- **Azure Portal**: `https://portal.azure.com`
- **Azure DevOps**: `https://dev.azure.com/aharttn/Hartonomous`
- **Pipelines**: `https://dev.azure.com/aharttn/Hartonomous/_build`
- **Artifacts**: `https://dev.azure.com/aharttn/Hartonomous/_artifacts`
- **Variable Groups**: `https://dev.azure.com/aharttn/Hartonomous/_library`
- **Deployment Groups**: `https://dev.azure.com/aharttn/Hartonomous/_settings/deploymentgroups`

---

## 🎯 Architecture Diagram

```
┌─────────────────────────────────────────────────────────────────┐
│                     Azure DevOps Pipeline                       │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────┐   │
│  │ Build Stage  │→ │Package Stage │→ │  Deploy Stage      │   │
│  │ (Arc Agent)  │  │(Arc Agent)   │  │(Arc Run Command)   │   │
│  └──────┬───────┘  └──────┬───────┘  └──────┬─────────────┘   │
│         │                  │                  │                 │
└─────────┼──────────────────┼──────────────────┼─────────────────┘
          │                  │                  │
          │ Managed Identity │                  │
          │                  │                  │
          ↓                  ↓                  ↓
┌─────────────────────────────────────────────────────────────────┐
│                         Azure Resources                         │
│  ┌──────────────┐  ┌──────────────┐  ┌────────────────────┐   │
│  │  Key Vault   │  │App Config    │  │ Azure Artifacts    │   │
│  │  (Secrets)   │  │(Settings)    │  │  (NuGet Feed)      │   │
│  └──────────────┘  └──────────────┘  └────────────────────┘   │
│         ↑                  ↑                  ↑                 │
│         └──────────────────┴──────────────────┘                 │
│              RBAC (Key Vault Secrets User,                      │
│               App Config Data Reader)                           │
└─────────────────────────────────────────────────────────────────┘
          ↑                                      ↑
          │                                      │
          │ System-Assigned Managed Identity     │
          │                                      │
┌─────────┴──────────────────────────────────────┴─────────────────┐
│              Arc-Connected On-Premises Machines                   │
│  ┌──────────────┐                        ┌──────────────┐        │
│  │ hart-server  │                        │ hart-desktop │        │
│  │ (Production) │                        │(Development) │        │
│  └──────────────┘                        └──────────────┘        │
└───────────────────────────────────────────────────────────────────┘
```

---

## ✅ Summary

This setup provides:

✓ **Zero manual credential management** - Everything via managed identities  
✓ **Automatic NuGet authentication** - No credential provider installation  
✓ **Secure secret storage** - All credentials in Key Vault with RBAC  
✓ **Centralized configuration** - App Configuration for all settings  
✓ **Multi-platform native libraries** - Windows/Linux C++ binaries in NuGet  
✓ **Automated package publishing** - Direct to Azure Artifacts from pipeline  
✓ **On-prem deployment** - Via Azure Arc without VPN/opening firewall  

**Production-grade CI/CD with enterprise security. Not a toy POC.**

---

## 📁 Legacy Deployment Documentation

The original deployment scripts (database setup, nginx, systemd, IIS) remain available:
```bash
cd database

# Initialize PostgreSQL
./setup-postgresql.sh

# Configure Redis
./configure-redis.sh
```

### 2. Azure Key Vault
```bash
cd azure

# Store secrets in Key Vault
./setup-keyvault-secrets.sh

# Grant Arc servers access
# Edit script first to set resource group & server names
./grant-arc-keyvault-access.sh
```

### 3. Application Configuration
```bash
cd config

# Deploy appsettings (gets secrets from Key Vault)
sudo cp appsettings.Production.json /srv/www/hartonomous/api/
sudo cp appsettings.Production.json /srv/www/hartonomous/worker/
```

### 4. Systemd Services
```bash
cd systemd

# Install services
./install-services.sh

# Start services (after first deployment)
sudo systemctl start hartonomous-api
sudo systemctl start hartonomous-worker
```

### 5. Nginx Reverse Proxy
```bash
cd nginx

# Install and configure
./install-nginx.sh

# Obtain SSL certificate
sudo certbot --nginx -d api.hartonomous.local
```

## Directory Structure

```
deployment/
├── systemd/                    # Service definitions
│   ├── hartonomous-api.service
│   ├── hartonomous-worker.service
│   └── install-services.sh
├── nginx/                      # Reverse proxy
│   ├── hartonomous-api.conf
│   └── install-nginx.sh
├── config/                     # Application settings
│   ├── appsettings.Production.json
│   └── setup-config.sh
├── database/                   # Database initialization
│   ├── init-postgresql.sql
│   ├── setup-postgresql.sh
│   ├── configure-redis.sh
│   └── README.md
├── azure/                      # Azure integration
│   ├── setup-keyvault-secrets.sh
│   ├── grant-arc-keyvault-access.sh
│   └── README.md
└── README.md                   # This file
```

## Azure DevOps Pipeline

The pipeline automatically:
1. Builds all projects on "Local Agent Pool"
2. MAUI app builds on Windows agent (hart-desktop)
3. Creates NuGet packages (Core, Data, Infrastructure)
4. Deploys to "Primary Local" deployment group
   - API & Worker → Linux (hart-server)
   - Web → Windows IIS (hart-desktop)

**Pipeline file:** `.azure-pipelines/build-and-deploy.yml`

## Infrastructure Resources

### Linux (hart-server)
```
Hosting:     /srv/www (16GB XFS on vg-hosting)
Databases:
  - PostgreSQL: /var/lib/postgresql (256GB)
  - Redis:      /var/lib/redis (128GB)
  - MSSQL:      /var/opt/mssql (128GB)
  - Neo4j:      /var/lib/neo4j (128GB)
```

### Windows (hart-desktop)
```
Hosting:  D:\inetpub (2x 2TB Samsung 990 EVO NVMe)
Services: IIS, .NET 10 Runtime, MAUI Workload
```

## Security

**Zero Trust Model:**
- No passwords in config files
- Azure Key Vault for all secrets
- Managed Identity authentication (no service principals)
- Entra ID authentication for users
- External ID for customers
- SSL/TLS for all external endpoints

**Access Control:**
```bash
# Service accounts
User: www-data
Group: www-data

# Permissions
/srv/www/hartonomous: 755 (www-data:www-data)
/var/lib/postgresql:  700 (postgres:postgres)
/var/lib/redis:       755 (redis:redis)
```

## Monitoring

### Service Status
```bash
# Check services
sudo systemctl status hartonomous-api
sudo systemctl status hartonomous-worker
sudo systemctl status redis-server
sudo systemctl status postgresql

# View logs
sudo journalctl -u hartonomous-api -f
sudo journalctl -u hartonomous-worker -f
```

### Database Health
```bash
# PostgreSQL
sudo -u postgres psql -d hartonomous -c "SELECT count(*) FROM pg_stat_activity;"

# Redis
redis-cli ping
redis-cli info memory
```

### Nginx
```bash
# Test config
sudo nginx -t

# Reload
sudo systemctl reload nginx

# Logs
sudo tail -f /var/log/nginx/hartonomous-api-*.log
```

## Troubleshooting

### API won't start
```bash
# Check executable permissions
sudo chmod +x /srv/www/hartonomous/api/Hartonomous.Api

# Check dependencies
ldd /srv/www/hartonomous/api/Hartonomous.Api

# View detailed logs
sudo journalctl -u hartonomous-api -n 100 --no-pager
```

### Database connection issues
```bash
# Test PostgreSQL
psql -h localhost -U hartonomous_user -d hartonomous

# Test Redis
redis-cli ping

# Check Key Vault access
az login --identity
az keyvault secret show --vault-name hartonomous-kv --name PostgreSQL--Password
```

### Managed Identity issues
```bash
# Check Arc agent
sudo azcmagent show

# Restart agent
sudo systemctl restart himdsd

# Test token acquisition
curl -H "Metadata:true" \
  "http://localhost:40342/metadata/identity/oauth2/token?api-version=2020-06-01&resource=https://vault.azure.net"
```

## Backup Strategy

### PostgreSQL
```bash
# Daily backup (add to crontab)
0 2 * * * sudo -u postgres pg_dump hartonomous | gzip > /srv/archive/hartonomous_$(date +\%Y\%m\%d).sql.gz
```

### Redis
```bash
# Automatic via RDB + AOF
# Backup files: /var/lib/redis/dump.rdb, /var/lib/redis/appendonly.aof
```

### Application
```bash
# Backup config and binaries
tar -czf hartonomous_backup_$(date +%Y%m%d).tar.gz /srv/www/hartonomous
```

## Updates and Maintenance

### Update via Pipeline
Commits to `main` branch automatically trigger:
1. Build
2. Test
3. Deploy to Primary Local environment

### Manual Restart
```bash
# Restart services after config change
sudo systemctl restart hartonomous-api
sudo systemctl restart hartonomous-worker

# Restart Nginx
sudo systemctl reload nginx
```

### Database Migrations
```bash
# Run from API directory
cd /srv/www/hartonomous/api
./Hartonomous.Api --migrate-database
```

## Support

**Logs Location:**
- Services: `journalctl -u hartonomous-*`
- Nginx: `/var/log/nginx/hartonomous-*.log`
- PostgreSQL: `/var/log/postgresql/`
- Redis: `journalctl -u redis-server`

**Configuration:**
- App Settings: `/srv/www/hartonomous/*/appsettings.Production.json`
- Secrets: Azure Key Vault `hartonomous-kv`
- Services: `/etc/systemd/system/hartonomous-*.service`
- Nginx: `/etc/nginx/sites-available/hartonomous-*`
