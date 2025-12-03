# Azure Pipeline Setup for Zero Trust Deployment

## One-Time Azure DevOps Configuration

### 1. Create Service Connection

In Azure DevOps:
1. Go to **Project Settings** → **Service Connections**
2. Click **New Service Connection** → **Azure Resource Manager**
3. Select **Service Principal (automatic)**
4. Configure:
   - **Connection name**: `AzureArcConnection`
   - **Subscription**: Select your Azure Developer Subscription
   - **Resource group**: `rg-hartonomous` (or leave blank for subscription-level)
5. Grant access permission to all pipelines

### 2. Create Pipeline

1. Go to **Pipelines** → **New Pipeline**
2. Select **Azure Repos Git**
3. Select your **Hartonomous** repository
4. Select **Existing Azure Pipelines YAML file**
5. Path: `/azure-pipelines.yml`
6. Click **Run**

### 3. Configure Environments (Optional - for approvals)

Create these environments in Azure DevOps for deployment gates:
1. Go to **Pipelines** → **Environments**
2. Create:
   - `localhost` (auto-deploy)
   - `dev` (auto-deploy)
   - `staging` (add approval if desired)
   - `production` (add approval - recommended!)

## What the Pipeline Does Automatically

### Stage 1: Infrastructure (Always Runs First)
✅ **Idempotent** - Safe to run every time
- Populates Key Vault with secrets (auto-discovers Tenant/Client IDs)
- Deploys RBAC role assignments
- Grants Arc machines access to Key Vault & App Configuration
- Verifies all required secrets exist

### Stage 2: Build & Test
- Restores packages
- Builds .NET 10 solution
- Runs xUnit tests with coverage
- Builds & pushes Docker images

### Stage 3-6: Deploy (Localhost/Dev/Staging/Production)
- Deploys containers to Arc machines via `az connectedmachine run-command`
- Applies EF Core migrations (idempotent)
- Health checks

## Zero Configuration Deployment

**For a new developer/client:**
```bash
git clone https://dev.azure.com/aharttn/Hartonomous/_git/Hartonomous
# Push to trigger pipeline - that's it!
```

Pipeline automatically:
1. ✅ Sets up Zero Trust RBAC
2. ✅ Configures managed identities
3. ✅ Populates Key Vault
4. ✅ Deploys applications
5. ✅ Applies database migrations

**No manual steps required!**

## Pipeline Triggers

Automatically runs on:
- Push to `main` → Deploys to localhost & production
- Push to `develop` → Deploys to dev & staging
- Changes to `infrastructure/**` → Re-runs infrastructure setup
- Pull requests → Build & test only

## Required Azure Resources

Must exist before first run:
- ✅ Resource Group: `rg-hartonomous`
- ✅ Key Vault: `kv-hartonomous`
- ✅ App Configuration: `appconfig-hartonomous`
- ✅ Arc Machines: `hart-server`, `HART-DESKTOP` (with SystemAssigned identity)
- ✅ Service Principal: `Hartonomous API (Production)`

All present in your subscription ✓

## Troubleshooting

**"AzureArcConnection not found"**
- Create service connection as described above

**"Permission denied on Key Vault"**
- Service connection service principal needs:
  - `Key Vault Administrator` role on `kv-hartonomous`
  - Or run: `az role assignment create --role "Key Vault Administrator" --assignee <SP_OBJECT_ID> --scope /subscriptions/.../kv-hartonomous`

**"Cannot find Hartonomous API service principal"**
- Verify exists: `az ad sp list --display-name "Hartonomous API (Production)"`
- Create if missing via Azure Portal → App Registrations

## Security Notes

- ✅ No secrets in code or pipeline YAML
- ✅ All secrets in Key Vault (managed identities access)
- ✅ Service connection uses managed identity where possible
- ✅ RBAC enforced at every layer
- ✅ Tenant isolation via JWT claims
