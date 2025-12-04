# Production Environment Setup

## Create Production Environment in GitHub

1. Go to https://github.com/AHartTN/Hartonomous/settings/environments
2. Click "New environment"
3. Name: `production`
4. Click "Configure environment"

## Add Required Protection Rules

- ? **Required reviewers**: Add yourself as required reviewer
- ? **Wait timer**: 0 minutes (or set delay)
- ? **Deployment branches**: Only `main` branch

## Configure Environment Variables

Navigate to the production environment and add:

| Variable Name | Example Value | Description |
|--------------|---------------|-------------|
| `ARC_MACHINE_NAME` | `prod-server-01` | Production Arc machine name |
| `OS_TYPE` | `linux` or `windows` | Operating system type |
| `CONTAINER_NAME` | `hartonomous-api` | Docker container name |
| `API_PORT` | `8000` | Port to expose |
| `LOG_LEVEL` | `INFO` | Application log level |
| `DEPLOYMENT_URL` | `https://prod.example.com` | Production URL |
| `AZURE_RESOURCE_GROUP` | `rg-hartonomous-prod` | Azure resource group |

## Configure Environment Secrets

Add these secrets to production environment:

| Secret Name | Description |
|------------|-------------|
| `AZURE_CLIENT_ID` | Service Principal Client ID for production |
| `AZURE_TENANT_ID` | Azure AD Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID |
| `DATABASE_URL` | Production PostgreSQL connection string |
| `NEO4J_PASSWORD` | Production Neo4j password |

## Deployment Flow

```
develop branch ? Tests ? Build ? Deploy to Development
                                          ?
staging branch ? Tests ? Build ? Deploy to Staging  
                                          ?
main branch    ? Tests ? Build ? Deploy to Production (requires approval)
```

## First Production Deployment

1. Ensure production environment is configured
2. Merge develop ? staging ? main
3. Approve production deployment when prompted
4. Verify deployment succeeded
