# GitHub Environment Configuration Guide

This document defines the required environment variables and secrets for each deployment environment.

## Required GitHub Environments

Create these environments in GitHub repo settings:
- `development`
- `staging` 
- `production`

## Environment Variables (Public)

These are set per environment in GitHub Settings ? Environments ? [environment name] ? Variables

### Development Environment

| Variable Name | Value | Description |
|--------------|-------|-------------|
| `ARC_MACHINE_NAME` | `hart-server` | Azure Arc machine name |
| `OS_TYPE` | `linux` | Operating system (linux/windows) |
| `DOCKER_IMAGE` | `ghcr.io/ahartn/hartonomous` | Docker image path |
| `CONTAINER_NAME` | `hartonomous-api` | Docker container name |
| `API_PORT` | `8000` | Port to expose API |
| `NEO4J_URI` | `bolt://localhost:7687` | Neo4j connection URI |
| `LOG_LEVEL` | `DEBUG` | Logging level |
| `DEPLOYMENT_URL` | `http://hart-server:8000` | Deployment URL |
| `AZURE_RESOURCE_GROUP` | `rg-hartonomous` | Azure resource group |

### Staging Environment

| Variable Name | Value | Description |
|--------------|-------|-------------|
| `ARC_MACHINE_NAME` | `HART-DESKTOP` | Azure Arc machine name |
| `OS_TYPE` | `windows` | Operating system (linux/windows) |
| `DOCKER_IMAGE` | `ghcr.io/ahartn/hartonomous` | Docker image path |
| `CONTAINER_NAME` | `hartonomous-api` | Docker container name |
| `API_PORT` | `8000` | Port to expose API |
| `NEO4J_URI` | `bolt://localhost:7687` | Neo4j connection URI |
| `LOG_LEVEL` | `INFO` | Logging level |
| `DEPLOYMENT_URL` | `http://HART-DESKTOP:8000` | Deployment URL |
| `AZURE_RESOURCE_GROUP` | `rg-hartonomous` | Azure resource group |

### Production Environment

| Variable Name | Value | Description |
|--------------|-------|-------------|
| `ARC_MACHINE_NAME` | `TBD` | Azure Arc machine or Container App |
| `OS_TYPE` | `linux` | Operating system |
| `DOCKER_IMAGE` | `ghcr.io/ahartn/hartonomous` | Docker image path |
| `CONTAINER_NAME` | `hartonomous-api` | Docker container name |
| `API_PORT` | `8000` | Port to expose API |
| `NEO4J_URI` | `bolt://prod-neo4j:7687` | Neo4j connection URI |
| `LOG_LEVEL` | `INFO` | Logging level |
| `DEPLOYMENT_URL` | `https://api.hartonomous.com` | Deployment URL |
| `AZURE_RESOURCE_GROUP` | `rg-hartonomous` | Azure resource group |

## Environment Secrets (Private)

These are set per environment in GitHub Settings ? Environments ? [environment name] ? Secrets

### All Environments

| Secret Name | Description | How to Get |
|------------|-------------|------------|
| `AZURE_CLIENT_ID` | Azure App Registration Client ID | From Azure Portal ? App Registrations |
| `AZURE_TENANT_ID` | Azure Tenant ID | From Azure Portal ? Microsoft Entra ID |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID | From Azure Portal ? Subscriptions |
| `DATABASE_URL` | PostgreSQL connection string | `postgresql://user:pass@host:port/db` |
| `NEO4J_PASSWORD` | Neo4j database password | From Neo4j configuration |

## Setup Instructions

### 1. Create Environments

```bash
# Go to GitHub repo
# Settings ? Environments ? New environment
# Create: development, staging, production
```

### 2. Add Variables to Each Environment

```bash
# For each environment:
# Settings ? Environments ? [env name] ? Add variable
# Add all variables from the tables above
```

### 3. Add Secrets to Each Environment

```bash
# For each environment:
# Settings ? Environments ? [env name] ? Add secret
# Add all secrets from the table above
```

### 4. Configure Azure Federated Credentials

For each environment, create a federated credential in the Azure App Registration:

```bash
# Development
az ad app federated-credential create \
  --id <AZURE_CLIENT_ID> \
  --parameters '{
    "name": "github-development",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:AHartTN/Hartonomous:environment:development",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Staging
az ad app federated-credential create \
  --id <AZURE_CLIENT_ID> \
  --parameters '{
    "name": "github-staging",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:AHartTN/Hartonomous:environment:staging",
    "audiences": ["api://AzureADTokenExchange"]
  }'

# Production
az ad app federated-credential create \
  --id <AZURE_CLIENT_ID> \
  --parameters '{
    "name": "github-production",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:AHartTN/Hartonomous:environment:production",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

## Workflow Behavior

- **Push to `develop`** ? Deploys to `development` environment
- **Push to `staging`** ? Deploys to `staging` environment
- **Push to `main`** ? Deploys to `production` environment
- **Manual trigger** ? Choose any environment

## Adding New Environments

To add a new environment:

1. Create environment in GitHub
2. Add all variables/secrets
3. Configure Azure federated credential
4. Update workflow matrix in `.github/workflows/ci-cd.yml`
