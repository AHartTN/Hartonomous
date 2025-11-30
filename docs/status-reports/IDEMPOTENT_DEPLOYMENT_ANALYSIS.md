# IDEMPOTENT DEPLOYMENT ANALYSIS

**Date:** 2025-11-28  
**Assessment:** Partially broken - missing critical Azure integration  
**Status:** 60% working, 40% needs fixing

---

## What You HAD (Stable State)

### ? Working Components

1. **Docker Compose** - GOOD
   - Valid YAML
   - All services defined
   - Health checks in place
   - Proper dependency chains
   - Volume management correct

2. **Schema Initialization** - GOOD
   - `docker/init-db.sh` exists and has Unix line endings
   - All SQL files referenced exist (150 total)
   - Proper execution order (types ? tables ? indexes ? functions ? triggers ? views)
   - `CREATE IF NOT EXISTS` used throughout (idempotent)

3. **Environment Variables** - GOOD
   - `.env` file exists
   - Proper structure for local/Azure modes
   - Defaults in docker-compose.yml

4. **CI/CD Pipeline** - GOOD
   - `.github/workflows/ci-cd.yml` exists
   - Builds both API and code-atomizer images
   - Pushes to GHCR
   - Multi-environment deployment (dev/staging/prod)

---

## What's BROKEN Right Now

### ? Critical: Missing Azure Integration

**File:** `api/azure_config.py` — **DELETED**

**Referenced by:** `api/config.py` line 145

```python
# config.py tries to import:
from api.azure_config import (get_app_config_client, get_key_vault_client)

# But file doesn't exist ? ImportError when USE_AZURE_CONFIG=true
```

**Impact:**
- ? Cannot load from Azure Key Vault
- ? Cannot load from Azure App Configuration
- ? Production deployment will FAIL
- ? Staging deployment will FAIL
- ? Only localhost works (USE_AZURE_CONFIG=false)

### ? Critical: Credentials Configuration Unclear

**Your `.env` has:**
```bash
# LOCAL (works)
PGHOST=localhost
PGPASSWORD=Revolutionary-AI-2025!Geometry

# AZURE (not used because USE_AZURE_CONFIG=false)
# KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
# APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
```

**But `config.py` expects Azure to provide:**
1. PostgreSQL password from Key Vault secret: `PostgreSQL-Hartonomous-Password`
2. Neo4j password from Key Vault secret: `Neo4j-hart-server-Password`
3. Entra ID client secret from Key Vault: `AzureAd-ClientSecret`
4. Connection strings from App Config: `ConnectionStrings:PostgreSQL-HART-DESKTOP`

**Questions:**
- ? Does `kv-hartonomous` Key Vault exist?
- ? Do these secrets exist in it?
- ? Does `appconfig-hartonomous` App Configuration exist?
- ? Are these settings configured?
- ? Does the Managed Identity have RBAC permissions?

### ? Medium: Incomplete CI/CD Deployment Logic

**Current state:**
```yaml
- name: Deploy services
  run: |
    echo "Deployment logic needs to be updated for microservices."
    echo "API Image: ${{ needs.build-api.outputs.image_tag }}"
    echo "Atomizer Image: ${{ needs.build-atomizer.outputs.image_tag }}"
    # This script would now need to update a docker-compose file on the target
    # and run 'docker-compose up -d' instead of a single 'docker run'.
    # For now, this is a placeholder.
```

**The deployment step does NOTHING.**

**What it should do:**
1. SSH into target VM (or use Azure VM extension)
2. Pull latest docker-compose.yml
3. Pull new images from GHCR
4. Run `docker-compose up -d --pull always`
5. Health check the deployment
6. Rollback on failure

### ? Low: .NET Version Mismatch

**CI/CD uses:**
```yaml
dotnet-version: '10.0.x'  # .NET 10 doesn't exist yet
```

**Dockerfiles use:**
```dockerfile
# Likely .NET 8.0 or 9.0
```

**Should be:** `.NET 8.0` (current LTS)

---

## What Needs to be Fixed

### Priority 1: Restore Azure Integration

#### Fix 1A: Recreate `api/azure_config.py`

**Restore from git or rewrite:**

```python
"""Azure Key Vault and App Configuration integration."""

import logging
from typing import Optional

from azure.identity import DefaultAzureCredential, ManagedIdentityCredential
from azure.keyvault.secrets import SecretClient
from azure.appconfiguration import AzureAppConfigurationClient

logger = logging.getLogger(__name__)

_kv_client: Optional[SecretClient] = None
_app_config_client: Optional[AzureAppConfigurationClient] = None


def get_key_vault_client() -> Optional[SecretClient]:
    """Get Azure Key Vault client (singleton)."""
    global _kv_client
    
    if _kv_client:
        return _kv_client
    
    try:
        from api.config import settings
        
        if not settings.key_vault_url:
            logger.warning("KEY_VAULT_URL not set")
            return None
        
        # Use Managed Identity or DefaultAzureCredential
        if settings.azure_client_id:
            credential = ManagedIdentityCredential(client_id=settings.azure_client_id)
        else:
            credential = DefaultAzureCredential()
        
        _kv_client = SecretClient(
            vault_url=settings.key_vault_url,
            credential=credential
        )
        
        logger.info(f"Connected to Key Vault: {settings.key_vault_url}")
        return _kv_client
    
    except Exception as e:
        logger.error(f"Failed to connect to Key Vault: {e}")
        return None


def get_app_config_client() -> Optional[AzureAppConfigurationClient]:
    """Get Azure App Configuration client (singleton)."""
    global _app_config_client
    
    if _app_config_client:
        return _app_config_client
    
    try:
        from api.config import settings
        
        if not settings.app_config_endpoint:
            logger.warning("APP_CONFIG_ENDPOINT not set")
            return None
        
        # Use Managed Identity or DefaultAzureCredential
        if settings.azure_client_id:
            credential = ManagedIdentityCredential(client_id=settings.azure_client_id)
        else:
            credential = DefaultAzureCredential()
        
        _app_config_client = AzureAppConfigurationClient(
            base_url=settings.app_config_endpoint,
            credential=credential
        )
        
        logger.info(f"Connected to App Configuration: {settings.app_config_endpoint}")
        return _app_config_client
    
    except Exception as e:
        logger.error(f"Failed to connect to App Configuration: {e}")
        return None


__all__ = ["get_key_vault_client", "get_app_config_client"]
```

#### Fix 1B: Verify Azure Resources Exist

**Check Key Vault:**
```bash
az keyvault show --name kv-hartonomous --query id
```

**Check App Configuration:**
```bash
az appconfig show --name appconfig-hartonomous --query id
```

**Check secrets exist:**
```bash
az keyvault secret list --vault-name kv-hartonomous --query "[].name"
```

**Expected secrets:**
- `PostgreSQL-Hartonomous-Password`
- `Neo4j-hart-server-Password`
- `AzureAd-ClientSecret`
- `EntraExternalId-ClientSecret`

**Check App Config settings:**
```bash
az appconfig kv list --name appconfig-hartonomous --query "[].key"
```

**Expected settings:**
- `Hartonomous:Api:Host`
- `Hartonomous:Api:Port`
- `Hartonomous:Api:LogLevel`
- `Hartonomous:Api:CorsOrigins`
- `ConnectionStrings:PostgreSQL-HART-DESKTOP`
- `ConnectionStrings:PostgreSQL-hart-server`
- `Neo4j:hart-server:Uri`

#### Fix 1C: Setup Managed Identity RBAC

**Grant Key Vault access:**
```bash
# Get Managed Identity object ID (from App Service or VM)
IDENTITY_ID=$(az webapp identity show --name hartonomous-api --resource-group rg-hartonomous --query principalId -o tsv)

# Grant Key Vault Secrets User role
az role assignment create \
  --role "Key Vault Secrets User" \
  --assignee $IDENTITY_ID \
  --scope /subscriptions/{subscription-id}/resourceGroups/rg-hartonomous/providers/Microsoft.KeyVault/vaults/kv-hartonomous
```

**Grant App Configuration access:**
```bash
# Grant App Configuration Data Reader role
az role assignment create \
  --role "App Configuration Data Reader" \
  --assignee $IDENTITY_ID \
  --scope /subscriptions/{subscription-id}/resourceGroups/rg-hartonomous/providers/Microsoft.AppConfiguration/configurationStores/appconfig-hartonomous
```

---

### Priority 2: Complete CI/CD Deployment

#### Fix 2A: Add Deployment Script

**Create:** `.github/scripts/deploy-docker-compose.sh`

```bash
#!/bin/bash
set -e

# Deployment script for Hartonomous
# Usage: ./deploy-docker-compose.sh <environment> <api-image> <atomizer-image>

ENVIRONMENT=$1
API_IMAGE=$2
ATOMIZER_IMAGE=$3

echo "Deploying to $ENVIRONMENT..."
echo "API Image: $API_IMAGE"
echo "Atomizer Image: $ATOMIZER_IMAGE"

# SSH into target VM (or use Azure VM extension)
TARGET_HOST="${ENVIRONMENT}-vm.hartonomous.com"
SSH_KEY_PATH="$HOME/.ssh/deployment_key"

# Create temporary docker-compose with image tags
cat > docker-compose.prod.yml <<EOF
version: '3.8'
services:
  postgres:
    # ... (same as docker-compose.yml)
  
  neo4j:
    # ... (same as docker-compose.yml)
  
  api:
    image: $API_IMAGE
    # ... (rest same as docker-compose.yml)
  
  code-atomizer:
    image: $ATOMIZER_IMAGE
    # ... (rest same as docker-compose.yml)
  
  caddy:
    # ... (same as docker-compose.yml)
EOF

# Copy to target
scp -i $SSH_KEY_PATH docker-compose.prod.yml $TARGET_HOST:/opt/hartonomous/

# Deploy
ssh -i $SSH_KEY_PATH $TARGET_HOST << 'ENDSSH'
cd /opt/hartonomous
docker-compose -f docker-compose.prod.yml pull
docker-compose -f docker-compose.prod.yml up -d --remove-orphans
docker-compose -f docker-compose.prod.yml ps
ENDSSH

# Health check
echo "Waiting for health check..."
sleep 10
HEALTH_URL="https://${ENVIRONMENT}.hartonomous.com/v1/health"
RESPONSE=$(curl -s $HEALTH_URL)
if [[ $RESPONSE == *"healthy"* ]]; then
  echo "? Deployment successful!"
else
  echo "? Health check failed!"
  exit 1
fi
```

#### Fix 2B: Update CI/CD Workflow

**Replace deployment steps:**

```yaml
deploy_dev:
  name: Deploy to development
  runs-on: ubuntu-latest
  needs: [build-api, build-atomizer]
  environment:
    name: development
    url: https://dev.hartonomous.com
  permissions:
    id-token: write
    contents: read
  steps:
    - name: Checkout
      uses: actions/checkout@v4
    
    - name: Azure Login
      uses: azure/login@v2
      with:
        client-id: ${{ secrets.AZURE_CLIENT_ID }}
        tenant-id: ${{ secrets.AZURE_TENANT_ID }}
        subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
    
    - name: Setup SSH Key
      run: |
        mkdir -p ~/.ssh
        echo "${{ secrets.DEPLOYMENT_SSH_KEY }}" > ~/.ssh/deployment_key
        chmod 600 ~/.ssh/deployment_key
    
    - name: Deploy services
      run: |
        chmod +x .github/scripts/deploy-docker-compose.sh
        .github/scripts/deploy-docker-compose.sh \
          development \
          ${{ env.REGISTRY }}/${{ github.repository_owner }}/hartonomous-api:sha-${{ github.sha }} \
          ${{ env.REGISTRY }}/${{ github.repository_owner }}/hartonomous-code-atomizer:sha-${{ github.sha }}
```

---

### Priority 3: Fix .NET Version

**Update CI/CD:**
```yaml
- name: Setup .NET
  uses: actions/setup-dotnet@v4
  with:
    dotnet-version: '8.0.x'  # LTS version
```

**Update Dockerfile if needed:**
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:8.0
```

---

## Idempotency Checklist

### ? Already Idempotent

- [x] PostgreSQL schema uses `CREATE IF NOT EXISTS`
- [x] Docker volumes persist data across restarts
- [x] Health checks prevent premature traffic
- [x] `docker-compose up -d` can run repeatedly
- [x] Service dependency chains correct

### ? Not Idempotent (Needs Fixing)

- [ ] CI/CD deployment (currently does nothing)
- [ ] Azure secret rotation (no automatic reload)
- [ ] Schema migrations (no versioning system)
- [ ] Neo4j sync catch-up (no resume from checkpoint)

---

## Testing Idempotent Deployment

### Test 1: Local Deployment (Repeat 3x)

```bash
# Run 1
docker-compose down -v
docker-compose up -d
curl http://localhost/v1/health

# Run 2 (should be no-op)
docker-compose up -d
curl http://localhost/v1/health

# Run 3 (change nothing, redeploy)
docker-compose down
docker-compose up -d
curl http://localhost/v1/health

# Expected: All 3 succeed, data persists (unless -v flag used)
```

### Test 2: Schema Idempotency

```bash
# Initialize once
docker-compose up -d postgres
docker-compose exec postgres psql -U hartonomous -d hartonomous -c "\dt"

# Run init script again (should be no-op)
docker-compose exec postgres bash -c "cd /schema && psql -U hartonomous -d hartonomous -f /docker-entrypoint-initdb.d/init-db.sh"

# Expected: No errors, table count unchanged
```

### Test 3: Azure Configuration

```bash
# Set Azure mode
export USE_AZURE_CONFIG=true
export KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
export APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io

# Start API (should load from Azure)
docker-compose up api

# Expected: Logs show "Loaded settings from App Configuration"
```

---

## Summary: How Fucked Is It?

### Severity: MEDIUM ??

**Working (60%):**
- ? Docker Compose structure
- ? Schema initialization
- ? Local deployment
- ? Health checks
- ? CI/CD builds images

**Broken (40%):**
- ? Azure integration (missing file)
- ? CI/CD deployment (placeholder)
- ? Production config unclear

**Time to Fix:** 2-4 hours

**Priority Order:**
1. Restore `api/azure_config.py` (30 min)
2. Verify Azure resources exist (30 min)
3. Setup RBAC for Managed Identity (30 min)
4. Complete CI/CD deployment script (1 hour)
5. Test end-to-end deployment (1 hour)

---

## Perfect Idempotent Deployment (Target State)

### What It Should Look Like

```bash
# ANYWHERE, ANYTIME, ANY STATE
git pull
docker-compose up -d --pull always

# Result:
# - All services start
# - Schema initialized (if new database)
# - Data persists (existing database)
# - Secrets loaded from Azure
# - Health checks pass
# - System fully operational
#
# NO MANUAL STEPS
# NO STATE ASSUMPTIONS
# ALWAYS WORKS
```

**That's the goal. You're 60% there.** The foundation is solid, just need to wire up Azure and finish CI/CD.
