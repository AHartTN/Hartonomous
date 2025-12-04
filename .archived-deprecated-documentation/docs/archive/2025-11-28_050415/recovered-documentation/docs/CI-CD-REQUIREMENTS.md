# COMPLETE CI/CD DEPLOYMENT REQUIREMENTS - FULLY DOCUMENTED

## EXECUTIVE SUMMARY
**Build once, deploy everywhere** - Single Docker image tagged with `${{ github.sha }}`, promoted through development ? staging environments without rebuilding.

---

## 1. SOURCE & BUILD REQUIREMENTS

### 1.1 Repository Configuration
**Confirmed by**: [Deploy ARM templates by using GitHub Actions - MS Docs](https://learn.microsoft.com/en-us/azure/azure-resource-manager/templates/deploy-github-actions)
- ? GitHub repository with code
- ? Branch `develop` triggers deployment to development environment  
- ? Branch `staging` triggers deployment to staging environment (future)
- ? Workflow file: `.github/workflows/ci-cd.yml`

### 1.2 Build Pipeline Steps
**Confirmed by**: [Deploy to Azure Container Apps with GitHub Actions](https://learn.microsoft.com/en-us/azure/container-apps/github-actions)
- ? Install Python 3.12 dependencies
- ? Run linting (pylint)
- ? Run security scans (bandit, safety)
- ? Run unit tests (pytest)
- ? Build Docker image from `docker/Dockerfile`
- ? Tag with unique identifier: `${{ github.sha }}` (**REQUIRED - not `latest`**)

### 1.3 Image Tagging Strategy
**Confirmed by**: [Container image tagging best practices - MS Docs](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-image-tag-version)
- ? Use `${{ github.sha }}` for unique, traceable tags
- ? **NEVER use `latest` for deployments** (causes cache issues)
- ? Tag format: `ghcr.io/ahartn/hartonomous:develop-sha-abc1234`

---

## 2. ARTIFACT STORAGE (PRIVATE CONTAINER REGISTRY)

### Option A: Azure Container Registry (ACR) - RECOMMENDED
**Confirmed by**: [Push images to ACR - MS Docs](https://learn.microsoft.com/en-us/azure/container-registry/container-registry-get-started-docker-cli)

**Requirements:**
- ? ACR instance provisioned
- ? Service Principal has `AcrPush` role
- ? GitHub workflow authenticates with `az acr login`
- ? Images pushed: `<registry>.azurecr.io/hartonomous:<sha>`

**Authentication:**
```yaml
- uses: azure/login@v2
  with:
    client-id: ${{ secrets.AZURE_CLIENT_ID }}
    tenant-id: ${{ secrets.AZURE_TENANT_ID }}
    subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
```

### Option B: Build Locally on Arc Machines - ALTERNATIVE
**Confirmed by**: No registry needed - deploy source + Dockerfile to Arc machine, build locally

**Requirements:**
- ? Send Dockerfile + source code to Arc machine via run-command
- ? Build Docker image on target machine
- ? No external registry authentication needed
- ? Slower (builds on each machine)
- ? No artifact reuse between environments

---

## 3. GITHUB ENVIRONMENTS & SECRETS

### 3.1 Environment Configuration
**Confirmed by**: [GitHub environment secrets - MS Docs](https://learn.microsoft.com/en-us/azure/azure-resource-manager/templates/deploy-github-actions#configure-the-github-secrets)

**Required Environments:**
- ? `development` 
- ? `staging`
- ?? `production` (future)

### 3.2 Environment Variables (per environment)
**Stored in**: GitHub Repo ? Settings ? Environments ? [env name] ? Variables

| Variable | Example Value | Purpose |
|----------|--------------|---------|
| `ARC_MACHINE_NAME` | `hart-server` | Azure Arc machine name |
| `OS_TYPE` | `linux` or `windows` | Determines script syntax |
| `DOCKER_IMAGE` | `<registry>/hartonomous` | Base image path |
| `CONTAINER_NAME` | `hartonomous-api` | Docker container name |
| `API_PORT` | `8000` | Port to expose |
| `LOG_LEVEL` | `DEBUG` or `INFO` | Application log level |
| `DEPLOYMENT_URL` | `http://hart-server:8000` | App URL |
| `AZURE_RESOURCE_GROUP` | `rg-hartonomous` | Resource group |
| `NEO4J_URI` | `bolt://localhost:7687` | Neo4j connection |

### 3.3 Environment Secrets (per environment)
**Stored in**: GitHub Repo ? Settings ? Environments ? [env name] ? Secrets

| Secret | Purpose |
|--------|---------|
| `AZURE_CLIENT_ID` | Service Principal Client ID |
| `AZURE_TENANT_ID` | Azure AD Tenant ID |
| `AZURE_SUBSCRIPTION_ID` | Azure Subscription ID |
| `DATABASE_URL` | PostgreSQL connection string |
| `NEO4J_PASSWORD` | Neo4j password |

---

## 4. AZURE ARC CONNECTED MACHINES

### 4.1 Arc Agent Requirements
**Confirmed by**: [Run command prerequisites - MS Docs](https://learn.microsoft.com/en-us/azure/azure-arc/servers/run-command)

- ? **Agent version 1.33 or higher** (REQUIRED for run-command)
- ? Agent running and connected to Azure
- ? Machine shows "Connected" status in Azure Portal

**Verification:**
```bash
# On Arc machine
azcmagent show
# Check: Status = Connected
# Check: Agent Version >= 1.33
```

### 4.2 Machine Configuration

**hart-server (Linux):**
- ? OS: Linux (Ubuntu/RHEL/etc)
- ? Docker installed and running
- ? Can execute bash scripts
- ? Port 8000 available
- ? PostgreSQL accessible (localhost or remote)
- ? Neo4j accessible (optional)

**HART-DESKTOP (Windows):**
- ? OS: Windows Server or Windows 10/11
- ? Docker Desktop installed and running
- ? Can execute PowerShell scripts
- ? Port 8000 available
- ? PostgreSQL accessible
- ? Neo4j accessible (optional)

### 4.3 Docker Requirements (Both Machines)
- ? Docker daemon running
- ? Can pull images from registry (or build locally)
- ? Can run containers
- ? Sufficient disk space for images

---

## 5. AZURE RBAC & PERMISSIONS

### 5.1 Service Principal Roles
**Confirmed by**: [Deploy to Azure Container Apps - MS Docs](https://learn.microsoft.com/en-us/azure/container-apps/github-actions#configuration)

**Required:**
- ? `Contributor` role on resource group `rg-hartonomous`
- ? `AcrPush` role on Azure Container Registry (if using ACR)

**Current Status:**
```bash
# Service Principal: 53214c3b-7103-4f44-8822-4c905562271b
# Has Contributor on rg-hartonomous ?
```

### 5.2 Federated Identity Credentials
**Confirmed by**: [OIDC with GitHub Actions - MS Docs](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure)

**Required for each environment:**
```bash
az ad app federated-credential create \
  --id <AZURE_CLIENT_ID> \
  --parameters '{
    "name": "github-development",
    "issuer": "https://token.actions.githubusercontent.com",
    "subject": "repo:AHartTN/Hartonomous:environment:development",
    "audiences": ["api://AzureADTokenExchange"]
  }'
```

**Current Status:**
- ? Federated credential exists for development
- ?? Need to create for staging

---

## 6. DEPLOYMENT WORKFLOW ARCHITECTURE

### 6.1 Workflow Structure
**Confirmed by**: [Multi-environment deployment - MS Docs](https://learn.microsoft.com/en-us/devops/deliver/iac-github-actions)

```
[Push to develop]
       ?
[Build Job] ? Build Docker image once, tag with SHA
       ?
[Deploy Job (Matrix)]
       ??? [Development] ? Pull SHA image, deploy to hart-server
       ??? [Staging] ? Pull SAME SHA image, deploy to HART-DESKTOP
```

### 6.2 Critical Pattern
**THE SAME DOCKER IMAGE (tagged with SHA) is deployed to ALL environments**

---

## 7. DEPLOYMENT EXECUTION

### 7.1 Azure Arc Run Command
**Confirmed by**: [Run command CLI - MS Docs](https://learn.microsoft.com/en-us/azure/azure-arc/servers/run-command-cli)

**Command Structure:**
```bash
az connectedmachine run-command create \
  --resource-group $AZURE_RESOURCE_GROUP \
  --machine-name $ARC_MACHINE_NAME \
  --run-command-name deploy-$BUILD_ID \
  --script "$DEPLOYMENT_SCRIPT" \
  --timeout-in-seconds 180
```

### 7.2 Deployment Script (Linux)
```bash
docker pull <registry>/hartonomous:<sha>
docker stop hartonomous-api || true
docker rm hartonomous-api || true
docker run -d \
  --name hartonomous-api \
  --restart unless-stopped \
  -p 8000:8000 \
  -e DATABASE_URL='$DATABASE_URL' \
  -e LOG_LEVEL=$LOG_LEVEL \
  <registry>/hartonomous:<sha>
```

### 7.3 Deployment Script (Windows)
```powershell
docker pull <registry>/hartonomous:<sha>
docker stop hartonomous-api 2>$null
docker rm hartonomous-api 2>$null
docker run -d `
  --name hartonomous-api `
  --restart unless-stopped `
  -p 8000:8000 `
  -e DATABASE_URL='$DATABASE_URL' `
  -e LOG_LEVEL=$LOG_LEVEL `
  <registry>/hartonomous:<sha>
```

---

## 8. SUCCESS CRITERIA

### 8.1 Build Stage
- ? All tests pass
- ? Docker image builds successfully
- ? Image tagged with `${{ github.sha }}`
- ? Image pushed to registry (or source ready for local build)

### 8.2 Deploy Stage (Each Environment)
- ? Azure login succeeds with OIDC
- ? Arc extension installed
- ? Run-command executes successfully
- ? Container starts and runs
- ? Application responds on configured port
- ? Health check passes

### 8.3 Complete Pipeline
- ? **GREEN** checkmark on all jobs in GitHub Actions
- ? Same SHA deployed to both environments
- ? Both applications running and healthy

---

## 9. CURRENT GAPS & NEXT STEPS

### Gaps:
1. ? No container registry configured (ACR or GHCR setup needed)
2. ? Staging federated credential not created
3. ? Workflow currently saves artifact as tar (needs registry push)

### Next Actions:
1. ? Decide: Use ACR or build locally on machines
2. ? If ACR: Provision ACR, configure authentication
3. ? If local build: Modify workflow to send source to Arc machines
4. ? Create staging federated credential
5. ? Test complete pipeline end-to-end
6. ? Verify GREEN run for both environments
