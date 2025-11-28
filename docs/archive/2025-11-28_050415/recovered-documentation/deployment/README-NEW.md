# Hartonomous Deployment System

**Enterprise-grade CI/CD pipeline using Azure App Configuration + Key Vault**

---

## Architecture

### Platform-Agnostic Deployment
- **ONE pipeline** (`azure-pipelines.yml`) - works with Azure DevOps
- **ONE deployment script** (`deploy.sh`) - works on Windows/Linux/Azure Arc/on-prem
- **Configuration stored in Azure** - Azure App Configuration + Key Vault
- **No hardcoded credentials** - Managed Identity or Service Principal authentication

### Multi-Stage Pipeline Flow

```
Build Stage (runs once)
  ? Creates api.zip artifact
  ?
Development Stage (auto-deploy on develop branch)
  ? Downloads artifact
  ? Deploys to dev environment
  ?
Staging Stage (requires dev success)
  ? Downloads same artifact
  ? Deploys to staging environment
  ?
Production Stage (requires staging success + main branch)
  ? Downloads same artifact
  ? Deploys to production environment
```

---

## Prerequisites

### Azure Resources
- Azure App Configuration (`appconfig-hartonomous`)
- Azure Key Vault (`kv-hartonomous`)
- Managed Identity or Service Principal with access to both

### Target Machine Requirements
- Python 3.x (`python3` or `python`)
- Azure CLI (`az`) - authenticated via Managed Identity
- `unzip` utility
- `curl` for health checks
- PostgreSQL client (`psql`) for database operations

---

## Setup

### 1. Populate Azure Configuration

Run the setup script to migrate local configuration to Azure:

```bash
bash deployment/setup-azure-config.sh development
bash deployment/setup-azure-config.sh staging
bash deployment/setup-azure-config.sh production
```

This creates:
- **App Configuration keys:**
  - `deployment:{env}:install_path`
  - `api:{env}:host`
  - `api:{env}:port`
- **Key Vault secrets:**
  - `database-{env}-connection-string`
  - `database-{env}-password`

### 2. Configure Azure DevOps

1. Create Service Connection:
   - Type: Azure Resource Manager
   - Authentication: Managed Identity or Service Principal
   - Name: `Azure-ServiceConnection`

2. Create Environments:
   - `development`
   - `staging` (with approval gate)
   - `production` (with approval gate)

3. Set Pipeline Variables:
   - `APP_CONFIG_NAME`: `appconfig-hartonomous`
   - `RESOURCE_GROUP`: `rg-hartonomous`

---

## Deployment

### Automatic Deployment (via Pipeline)

**Push to `develop` branch:**
```bash
git push origin develop
```
- Builds artifact
- Deploys to development environment automatically

**Push to `main` branch:**
```bash
git push origin main
```
- Builds artifact
- Deploys to development ? staging ? production (with approval gates)

### Manual Deployment (Local)

```bash
# Set environment variables
export APP_CONFIG_NAME=appconfig-hartonomous
export RESOURCE_GROUP=rg-hartonomous

# Run deployment
bash deployment/deploy.sh development /path/to/api.zip
```

---

## Configuration

All configuration is stored in Azure App Configuration with Key Vault references:

### App Configuration Keys

```
deployment:development:install_path = "D:/Hartonomous/api"
api:development:host = "0.0.0.0"
api:development:port = "8000"
database:development:connection_string = @KeyVault(database-development-connection-string)
```

### Key Vault Secrets

```
database-development-connection-string
database-development-password
database-staging-connection-string
database-production-connection-string
```

**Security:** Secrets are never stored in code. Azure CLI automatically resolves Key Vault references using Managed Identity.

---

## What the Deployment Does

1. **Loads Configuration** from Azure App Configuration
2. **Resolves Secrets** from Key Vault (automatic via Azure CLI)
3. **Extracts Artifact** to install path
4. **Creates Python venv** (cross-platform detection)
5. **Installs Dependencies** from requirements.txt
6. **Creates .env file** with connection strings
7. **Runs Database Migrations** using Alembic
8. **Restarts Service** (kills existing, starts new)
9. **Validates Health** endpoint responds

---

## Service Management

### Current Implementation
The deployment script uses process management:
```bash
pkill -f "uvicorn main:app"
nohup python3 -m uvicorn main:app --host $API_HOST --port $API_PORT &
```

### Production Recommendation
Use systemd (Linux) or Windows Service for proper service management.

**TODO:** Create systemd service file

---

## Validation

### Health Checks

```bash
# Basic health
curl http://localhost:8000/v1/health

# Readiness (includes database check)
curl http://localhost:8000/v1/ready

# Statistics
curl http://localhost:8000/v1/stats
```

### Expected Response
```json
{
  "status": "ok",
  "service": "hartonomous-api",
  "version": "0.6.0"
}
```

---

## Troubleshooting

### Deployment Failed

1. **Check Azure CLI authentication:**
   ```bash
   az account show
   ```

2. **Verify App Configuration access:**
   ```bash
   az appconfig kv list --name appconfig-hartonomous
   ```

3. **Verify Key Vault access:**
   ```bash
   az keyvault secret list --vault-name kv-hartonomous
   ```

4. **Check deployment logs:**
   - Deployment script outputs to stdout
   - Application logs: Check install path

### Service Not Starting

1. **Check if Python exists:**
   ```bash
   python3 --version
   ```

2. **Check if port is available:**
   ```bash
   netstat -tulpn | grep 8000
   ```

3. **Check database connectivity:**
   ```bash
   psql -h localhost -U postgres -d hartonomous -c "SELECT 1;"
   ```

### Health Check Failing

1. **Check if service is running:**
   ```bash
   ps aux | grep uvicorn
   ```

2. **Check logs in install directory:**
   ```bash
   cat /path/to/install/.env
   ```

3. **Test database manually:**
   ```bash
   curl http://localhost:8000/v1/ready
   ```

---

## Architecture Decisions

### Why Azure App Configuration + Key Vault?
- **Centralized configuration management** - no config files in code
- **Secure secret storage** - automatic Key Vault resolution
- **Environment isolation** - separate configs per environment
- **Audit trail** - all config changes tracked in Azure
- **Managed Identity support** - no credentials to manage

### Why Bash Script Instead of PowerShell?
- **Cross-platform** - works on Windows (Git Bash/WSL), Linux, macOS
- **Azure CLI native** - `az` commands work identically everywhere
- **Standard tooling** - bash/unzip/curl available on all platforms

### Why Single Artifact Flow?
- **Build once, deploy many** - same package tested in all environments
- **Reduced risk** - no rebuild differences between environments
- **Faster deployments** - artifact cached, no rebuild needed
- **Rollback capability** - can redeploy previous artifacts

---

## Files

```
deployment/
??? deploy.sh                    # Main deployment script
??? setup-azure-config.sh        # Migrate local config to Azure
??? README.md                    # This file
??? config/
    ??? development.json         # Local dev config (reference)
    ??? staging.json             # Staging config (reference)
    ??? production.json          # Production config (reference)

azure-pipelines.yml              # Multi-stage CI/CD pipeline
```

---

## Next Steps

### Planned Improvements
- [ ] Systemd service file for Linux
- [ ] Windows Service installer
- [ ] GitHub Actions workflow (in addition to Azure DevOps)
- [ ] Rollback capability
- [ ] Application Insights integration
- [ ] Automated validation tests
- [ ] Blue/green deployment support

---

**Copyright ⌐ 2025 Anthony Hart. All Rights Reserved.**
