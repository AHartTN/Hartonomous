# GitHub Secrets Setup - Enterprise Azure Architecture

This document describes the **actual deployed infrastructure** and required GitHub Secrets for CI/CD.

## Deployed Azure Infrastructure

### Azure Arc Connected Machines

**Development:** `hart-server` (Azure Arc-enabled Linux server)
- Status: Connected (Agent v1.58)
- Extensions: AADSSHLogin, LinuxAgent.SqlServer
- Deployment path: `/opt/hartonomous`
- PostgreSQL: Uses `PostgreSQL-hart-server-hartonomous-Password` from Key Vault
- Neo4j: Local instance

**Staging/Production:** `HART-DESKTOP` (Azure Arc-enabled Windows/WSL machine)  
- Status: Connected
- SQL Server: HART-DESKTOP\MSAS17_MSSQLSERVER
- Databases: Hartonomous, Hartonomous-DEV-development
- Staging path: `/opt/hartonomous-staging`
- Production path: `/opt/hartonomous-production` (blue-green deployment)
- PostgreSQL: Uses `PostgreSQL-HART-DESKTOP-hartonomous-Password` from Key Vault

### Azure Key Vault: `kv-hartonomous`

**Existing Secrets:**
- `ApplicationInsights-ConnectionString` - Azure Monitor telemetry
- `AzureAd-ClientSecret` - Microsoft Entra ID (internal auth)
- `EntraExternalId-ClientSecret` - External ID / CIAM (customer auth)
- `Neo4j--Password` - Neo4j graph database password
- `PostgreSQL-HART-DESKTOP-hartonomous-Password` - Staging/production DB
- `PostgreSQL-hart-server-hartonomous-Password` - Development DB
- `PostgreSQL-Hartonomous-Password` - Legacy/fallback password

### Azure App Configuration: `appconfig-hartonomous`

**Endpoint:** `https://appconfig-hartonomous.azconfig.io`

**Current Configuration:**
- `.appconfig.featureflag/RevolutionaryAI` - Feature flag

**Expected Configuration Keys** (referenced in `api/config.py`):
- `Hartonomous:Api:Host` - API bind address
- `Hartonomous:Api:Port` - API port
- `Hartonomous:Api:LogLevel` - Logging verbosity
- `Hartonomous:Api:CorsOrigins` - Allowed CORS origins (comma-separated)
- `Hartonomous:Api:AuthEnabled` - Enable authentication (true/false)
- `Hartonomous:Api:PoolMinSize` - Database connection pool minimum
- `Hartonomous:Database:PoolMaxSize` - Database connection pool maximum
- `ConnectionStrings:PostgreSQL-HART-DESKTOP` - Staging/prod connection string
- `ConnectionStrings:PostgreSQL-hart-server` - Dev connection string
- `Neo4j:hart-server:Uri` - Neo4j bolt URI for hart-server

## Required GitHub Secrets

## Required GitHub Secrets

### CI Test Environment Secrets

These are used ONLY for GitHub Actions CI runners (ephemeral PostgreSQL/Neo4j services):

| Secret Name | Description | Generate With | Required |
|-------------|-------------|---------------|----------|
| `TEST_POSTGRES_USER` | CI PostgreSQL username | `hartonomous_ci` | Yes |
| `TEST_POSTGRES_PASSWORD` | CI PostgreSQL password | `openssl rand -base64 32` | Yes |
| `TEST_POSTGRES_DB` | CI PostgreSQL database | `hartonomous_test` | Yes |
| `TEST_NEO4J_USER` | CI Neo4j username | `neo4j` | Yes |
| `TEST_NEO4J_PASSWORD` | CI Neo4j password | `openssl rand -base64 32` | Yes |

**Note:** These are NOT used in deployment. Production credentials come from Azure Key Vault via Managed Identity.

### Azure Deployment Secrets (Federated Identity)

| Secret Name | Description | How to Get | Required |
|-------------|-------------|------------|----------|
| `AZURE_CLIENT_ID` | Service Principal Client ID for GitHub Actions | Azure Portal → Entra ID → App registrations | Yes |
| `AZURE_TENANT_ID` | Microsoft Entra Tenant ID | `az account show --query tenantId -o tsv` | Yes |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID | `az account show --query id -o tsv` | Yes |

**Authentication Method:** OpenID Connect (OIDC) federated credentials
- No client secrets stored in GitHub
- GitHub Actions authenticates via OIDC token exchange
- Service Principal must have:
  - **Contributor** role on `rg-hartonomous` resource group
  - **Key Vault Secrets User** role on `kv-hartonomous`
  - **App Configuration Data Reader** role on `appconfig-hartonomous`
  - **Azure Connected Machine Resource Administrator** role for Arc deployments

## Setup Instructions

### 1. Configure Azure Service Principal for GitHub Actions

```bash
# Variables
APP_NAME="github-actions-hartonomous"
RESOURCE_GROUP="rg-hartonomous"
SUBSCRIPTION_ID=$(az account show --query id -o tsv)
REPO="AHartTN/Hartonomous"  # Your GitHub repo

# Create App Registration with federated credential
az ad app create --display-name "$APP_NAME"
APP_ID=$(az ad app list --display-name "$APP_NAME" --query [0].appId -o tsv)

# Create Service Principal
az ad sp create --id "$APP_ID"
SP_OBJECT_ID=$(az ad sp show --id "$APP_ID" --query id -o tsv)

# Add federated credential for GitHub Actions (main branch)
az ad app federated-credential create \
  --id "$APP_ID" \
  --parameters "{
    \"name\": \"github-actions-main\",
    \"issuer\": \"https://token.actions.githubusercontent.com\",
    \"subject\": \"repo:${REPO}:ref:refs/heads/main\",
    \"audiences\": [\"api://AzureADTokenExchange\"]
  }"

# Assign RBAC roles
az role assignment create \
  --assignee "$APP_ID" \
  --role "Contributor" \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}"

az role assignment create \
  --assignee "$APP_ID" \
  --role "Azure Connected Machine Resource Administrator" \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}"

# Key Vault access
az keyvault set-policy \
  --name kv-hartonomous \
  --object-id "$SP_OBJECT_ID" \
  --secret-permissions get list

# App Configuration access
az role assignment create \
  --assignee "$APP_ID" \
  --role "App Configuration Data Reader" \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.AppConfiguration/configurationStores/appconfig-hartonomous"

echo "AZURE_CLIENT_ID: $APP_ID"
echo "AZURE_TENANT_ID: $(az account show --query tenantId -o tsv)"
echo "AZURE_SUBSCRIPTION_ID: $SUBSCRIPTION_ID"
```

### 2. Configure Arc Machines with Managed Identity

```bash
# Enable system-assigned managed identity on Arc machines
az connectedmachine update \
  --name hart-server \
  --resource-group rg-hartonomous \
  --enable-identity

az connectedmachine update \
  --name HART-DESKTOP \
  --resource-group rg-hartonomous \
  --enable-identity

# Get principal IDs
HART_SERVER_PRINCIPAL=$(az connectedmachine show --name hart-server --resource-group rg-hartonomous --query identity.principalId -o tsv)
HART_DESKTOP_PRINCIPAL=$(az connectedmachine show --name HART-DESKTOP --resource-group rg-hartonomous --query identity.principalId -o tsv)

# Grant Key Vault access to both machines
az keyvault set-policy \
  --name kv-hartonomous \
  --object-id "$HART_SERVER_PRINCIPAL" \
  --secret-permissions get list

az keyvault set-policy \
  --name kv-hartonomous \
  --object-id "$HART_DESKTOP_PRINCIPAL" \
  --secret-permissions get list

# Grant App Configuration access
az role assignment create \
  --assignee "$HART_SERVER_PRINCIPAL" \
  --role "App Configuration Data Reader" \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.AppConfiguration/configurationStores/appconfig-hartonomous"

az role assignment create \
  --assignee "$HART_DESKTOP_PRINCIPAL" \
  --role "App Configuration Data Reader" \
  --scope "/subscriptions/${SUBSCRIPTION_ID}/resourceGroups/${RESOURCE_GROUP}/providers/Microsoft.AppConfiguration/configurationStores/appconfig-hartonomous"
```

### 3. Populate App Configuration

### 3. Populate App Configuration

```bash
# Add application settings to App Configuration
az appconfig kv set --name appconfig-hartonomous --key "Hartonomous:Api:Host" --value "0.0.0.0" --yes
az appconfig kv set --name appconfig-hartonomous --key "Hartonomous:Api:Port" --value "8000" --yes
az appconfig kv set --name appconfig-hartonomous --key "Hartonomous:Api:LogLevel" --value "INFO" --yes
az appconfig kv set --name appconfig-hartonomous --key "Hartonomous:Api:CorsOrigins" --value "https://hartonomous.com,https://www.hartonomous.com" --yes
az appconfig kv set --name appconfig-hartonomous --key "Hartonomous:Api:AuthEnabled" --value "true" --yes
az appconfig kv set --name appconfig-hartonomous --key "Hartonomous:Api:PoolMinSize" --value "5" --yes
az appconfig kv set --name appconfig-hartonomous --key "Hartonomous:Database:PoolMaxSize" --value "20" --yes

# Connection strings (format: "Host=host;Port=5432;Database=db;Username=user")
az appconfig kv set --name appconfig-hartonomous \
  --key "ConnectionStrings:PostgreSQL-HART-DESKTOP" \
  --value "Host=localhost;Port=5432;Database=Hartonomous;Username=hartonomous" \
  --yes

az appconfig kv set --name appconfig-hartonomous \
  --key "ConnectionStrings:PostgreSQL-hart-server" \
  --value "Host=localhost;Port=5432;Database=hartonomous;Username=hartonomous" \
  --yes

# Neo4j configuration
az appconfig kv set --name appconfig-hartonomous \
  --key "Neo4j:hart-server:Uri" \
  --value "bolt://localhost:7687" \
  --yes
```

### 4. Set GitHub Repository Secrets

#### Using GitHub CLI

```bash
# Generate test environment passwords
TEST_POSTGRES_PASSWORD=$(openssl rand -base64 32)
TEST_NEO4J_PASSWORD=$(openssl rand -base64 32)

# Set test secrets
gh secret set TEST_POSTGRES_USER --body "hartonomous_ci" --repo AHartTN/Hartonomous
gh secret set TEST_POSTGRES_PASSWORD --body "$TEST_POSTGRES_PASSWORD" --repo AHartTN/Hartonomous
gh secret set TEST_POSTGRES_DB --body "hartonomous_test" --repo AHartTN/Hartonomous
gh secret set TEST_NEO4J_USER --body "neo4j" --repo AHartTN/Hartonomous
gh secret set TEST_NEO4J_PASSWORD --body "$TEST_NEO4J_PASSWORD" --repo AHartTN/Hartonomous

# Set Azure deployment secrets (from Step 1 output)
gh secret set AZURE_CLIENT_ID --body "$APP_ID" --repo AHartTN/Hartonomous
gh secret set AZURE_TENANT_ID --body "$(az account show --query tenantId -o tsv)" --repo AHartTN/Hartonomous
gh secret set AZURE_SUBSCRIPTION_ID --body "$(az account show --query id -o tsv)" --repo AHartTN/Hartonomous
```

#### Using GitHub Web UI

1. Go to `https://github.com/AHartTN/Hartonomous/settings/secrets/actions`
2. Click **New repository secret**
3. Add each secret with name and value
4. Click **Add secret**

### 5. Configure GitHub Environments

The CI/CD pipeline uses three environments with approvals:

```bash
# Create environments (requires GitHub repo admin)
# Via GitHub UI: Settings → Environments → New environment

# Development: No approval required
#   - URL: http://hart-server.local:8000

# Staging: Requires approval
#   - URL: http://hart-desktop.local:8000
#   - Reviewers: Add yourself

# Production: Requires approval
#   - URL: https://hartonomous.com
#   - Reviewers: Add yourself + senior engineer
```

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ GitHub Actions CI/CD Pipeline                               │
├─────────────────────────────────────────────────────────────┤
│                                                              │
│  1. Test → Build → Push to GHCR                            │
│  2. Azure Login (OIDC federated credential, no secrets)    │
│  3. Deploy via Azure Arc run-command                       │
│                                                              │
└────────────────────┬────────────────────────────────────────┘
                     │
                     ├──────────────────┬──────────────────┐
                     ▼                  ▼                  ▼
            ┌──────────────┐   ┌──────────────┐  ┌──────────────┐
            │ hart-server  │   │ HART-DESKTOP │  │ HART-DESKTOP │
            │  (Dev/Arc)   │   │(Staging/Arc) │  │ (Prod/Arc)   │
            ├──────────────┤   ├──────────────┤  ├──────────────┤
            │ Managed ID   │   │ Managed ID   │  │ Blue/Green   │
            │ ↓            │   │ ↓            │  │ Deployment   │
            │ Key Vault    │   │ Key Vault    │  │ ↓            │
            │ App Config   │   │ App Config   │  │ Key Vault    │
            │              │   │              │  │ App Config   │
            │ PostgreSQL   │   │ PostgreSQL   │  │              │
            │ Neo4j        │   │ Neo4j        │  │ PostgreSQL   │
            │ Docker       │   │ Docker       │  │ Neo4j        │
            └──────────────┘   └──────────────┘  │ Docker       │
                                                  │ Nginx/Caddy  │
                                                  └──────────────┘
```

## Security Model

### Zero Secrets in Code/Config
- ✅ No passwords in `.env` files (for deployment)
- ✅ No credentials in `docker-compose.yml`
- ✅ No API keys in source code
- ✅ All secrets retrieved from Azure Key Vault at runtime

### Authentication Flow
1. **GitHub Actions** → Azure (OIDC federated credential, no client secret)
2. **Arc Machines** → Azure Key Vault (system-assigned managed identity)
3. **Application** → Databases (passwords from Key Vault)
4. **Users** → API (Microsoft Entra ID / External ID)

### Rotation Policy
1. **Test credentials:** Rotate quarterly (automated via CI)
2. **Service Principal:** Rotate federated credential annually
3. **Database passwords:** Rotate bi-annually via Key Vault
4. **Application secrets:** Rotate after security incident or personnel change

## Verification

### Verify Service Principal

```bash
APP_ID="<your-client-id>"

# Check app registration
az ad app show --id "$APP_ID" --query "{displayName:displayName, appId:appId}" -o table

# Check federated credentials
az ad app federated-credential list --id "$APP_ID" -o table

# Check RBAC assignments
az role assignment list --assignee "$APP_ID" --all -o table
```

### Verify Arc Machine Managed Identity

```bash
# Check identity enabled
az connectedmachine show --name hart-server --resource-group rg-hartonomous \
  --query "{name:name, identity:identity.type, principalId:identity.principalId}"

# Test Key Vault access (run on Arc machine)
az keyvault secret show --vault-name kv-hartonomous --name PostgreSQL-hart-server-hartonomous-Password --query value -o tsv
```

### Verify GitHub Secrets

```bash
# List all secrets (values hidden)
gh secret list --repo AHartTN/Hartonomous

# Expected output:
# TEST_POSTGRES_USER       Updated YYYY-MM-DD
# TEST_POSTGRES_PASSWORD   Updated YYYY-MM-DD
# TEST_POSTGRES_DB         Updated YYYY-MM-DD
# TEST_NEO4J_USER         Updated YYYY-MM-DD
# TEST_NEO4J_PASSWORD     Updated YYYY-MM-DD
# AZURE_CLIENT_ID         Updated YYYY-MM-DD
# AZURE_TENANT_ID         Updated YYYY-MM-DD
# AZURE_SUBSCRIPTION_ID   Updated YYYY-MM-DD
```

## Troubleshooting

### "Error: Cannot perform an interactive login from a non TTY device"

**Cause:** Deployment script trying to run `az login` on Arc machine

**Solution:** Arc machines use system-assigned managed identity automatically
- Remove any `az login` calls from deployment scripts
- Azure CLI automatically authenticates via managed identity when run on Arc machines
- Key Vault access works via `az keyvault secret show` without explicit login

### "Federated credential authentication failed"

**Cause:** GitHub Actions OIDC token not trusted by Azure

**Solution:**
```bash
# Verify federated credential exists
az ad app federated-credential list --id "$AZURE_CLIENT_ID"

# Check subject matches: repo:AHartTN/Hartonomous:ref:refs/heads/main
# Check issuer is: https://token.actions.githubusercontent.com
# Check audience includes: api://AzureADTokenExchange
```

### "Unable to retrieve secret from Key Vault"

**Cause:** Managed identity lacks Key Vault permissions

**Solution:**
```bash
# Get machine's principal ID
PRINCIPAL_ID=$(az connectedmachine show --name hart-server --resource-group rg-hartonomous --query identity.principalId -o tsv)

# Grant access
az keyvault set-policy --name kv-hartonomous --object-id "$PRINCIPAL_ID" --secret-permissions get list
```

### "Arc run-command failed with timeout"

**Cause:** Docker operations taking too long (image pulls, health checks)

**Solution:**
- Increase `--timeout-in-seconds` to 600 for production deployments
- Pre-pull images on Arc machines to reduce deployment time
- Check Docker disk space on target machines

## Additional Resources

- [GitHub OIDC with Azure](https://docs.github.com/en/actions/deployment/security-hardening-your-deployments/configuring-openid-connect-in-azure)
- [Azure Arc Overview](https://learn.microsoft.com/en-us/azure/azure-arc/overview)
- [Azure Key Vault Best Practices](https://learn.microsoft.com/en-us/azure/key-vault/general/best-practices)
- [Azure App Configuration](https://learn.microsoft.com/en-us/azure/azure-app-configuration/overview)
- [Managed Identity for Arc Machines](https://learn.microsoft.com/en-us/azure/azure-arc/servers/managed-identity-authentication)
