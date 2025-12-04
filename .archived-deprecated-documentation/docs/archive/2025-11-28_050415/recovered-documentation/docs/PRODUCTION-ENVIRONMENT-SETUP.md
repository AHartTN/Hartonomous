# Production Environment Setup

## Required Configuration in GitHub

Navigate to: https://github.com/AHartTN/Hartonomous/settings/environments/production

### Environment Secrets

Add these secrets to the `production` environment:

| Secret Name | Value Source | Description |
|------------|--------------|-------------|
| `AZURE_CLIENT_ID` | Azure Service Principal | Service Principal Application (client) ID for production |
| `AZURE_TENANT_ID` | Azure AD | Azure Active Directory Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure Portal | Production subscription ID |
| `GHCR_USERNAME` | GitHub | Your GitHub username (lowercase: `ahartn`) |
| `GHCR_TOKEN` | GitHub PAT | Personal Access Token with `read:packages` scope |

### Environment Variables

Add these variables to the `production` environment:

| Variable Name | Example Value | Description |
|--------------|---------------|-------------|
| `ARC_MACHINE_NAME` | `prod-server-01` | Azure Arc machine name for production |
| `OS_TYPE` | `linux` or `windows` | Operating system type |
| `CONTAINER_NAME` | `hartonomous-api-prod` | Docker container name |
| `API_PORT` | `8000` | Port to expose API |
| `LOG_LEVEL` | `INFO` | Application log level |
| `DEPLOYMENT_URL` | `https://prod.example.com` | Production URL |
| `AZURE_RESOURCE_GROUP` | `rg-hartonomous-prod` | Azure resource group name |

## Quick Setup Commands

Run these commands to set production environment variables via GitHub CLI:

```bash
# Set secrets (replace with actual values)
gh secret set AZURE_CLIENT_ID --env production --body "YOUR_PROD_CLIENT_ID"
gh secret set AZURE_TENANT_ID --env production --body "YOUR_TENANT_ID"
gh secret set AZURE_SUBSCRIPTION_ID --env production --body "YOUR_PROD_SUBSCRIPTION_ID"
gh secret set GHCR_USERNAME --env production --body "ahartn"
gh secret set GHCR_TOKEN --env production --body "YOUR_GITHUB_PAT"

# Set variables (replace with actual values)
gh variable set ARC_MACHINE_NAME --env production --body "YOUR_PROD_MACHINE_NAME"
gh variable set OS_TYPE --env production --body "linux"
gh variable set CONTAINER_NAME --env production --body "hartonomous-api-prod"
gh variable set API_PORT --env production --body "8000"
gh variable set LOG_LEVEL --env production --body "INFO"
gh variable set DEPLOYMENT_URL --env production --body "https://prod.example.com"
gh variable set AZURE_RESOURCE_GROUP --env production --body "YOUR_PROD_RG"
```

## Current Configuration Status

- ? Production environment exists in GitHub
- ? Secrets not configured
- ? Variables not configured

**Until secrets and variables are configured, production deployments will be skipped.**

## Deployment Flow

On `main` branch:
1. Build image ? Push to GHCR
2. Deploy to staging
3. Deploy to production (requires environment configuration)

Once configured, push to `main` will trigger: Build ? Staging ? Production with health checks.
