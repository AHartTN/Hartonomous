# Hartonomous Deployment Architecture

**Enterprise-Grade CI/CD System**

> **Philosophy**: Agnostic, Idempotent, Composable, Secure

---

## ≡ƒÅù∩╕Å Architecture Overview

### Design Principles

1. **Platform Agnostic**: Scripts work on Windows (PowerShell) AND Linux (Bash)
2. **Fully Idempotent**: Safe to run multiple times, same result
3. **Separation of Concerns**: Each script has ONE responsibility
4. **No Inline Scripts**: YAML files reference scripts only
5. **SOLID Principles**: Single responsibility, Open/Closed, Liskov substitution, Interface segregation, Dependency inversion
6. **DRY**: Shared logic in common modules
7. **Security First**: Secrets via Key Vault, least privilege, audit trails

---

## ≡ƒôü Directory Structure

```
hartonomous/
Γö£ΓöÇΓöÇ .github/
Γöé   Γö£ΓöÇΓöÇ workflows/                    # GitHub Actions workflows
Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-development.yml
Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-staging.yml
Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-production.yml
Γöé   Γöé   Γö£ΓöÇΓöÇ lint.yml
Γöé   Γöé   ΓööΓöÇΓöÇ test.yml
Γöé   ΓööΓöÇΓöÇ scripts/                      # GitHub-specific scripts
Γöé       ΓööΓöÇΓöÇ setup-environment.ps1
Γöé
Γö£ΓöÇΓöÇ .azuredevops/
Γöé   Γö£ΓöÇΓöÇ pipelines/                    # Azure Pipelines YAML
Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-development.yml
Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-staging.yml
Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-production.yml
Γöé   Γöé   Γö£ΓöÇΓöÇ lint.yml
Γöé   Γöé   ΓööΓöÇΓöÇ test.yml
Γöé   ΓööΓöÇΓöÇ templates/                    # Reusable YAML templates
Γöé       Γö£ΓöÇΓöÇ build-template.yml
Γöé       Γö£ΓöÇΓöÇ deploy-template.yml
Γöé       ΓööΓöÇΓöÇ test-template.yml
Γöé
Γö£ΓöÇΓöÇ deployment/
Γöé   Γö£ΓöÇΓöÇ scripts/                      # Cross-platform deployment scripts
Γöé   Γöé   Γö£ΓöÇΓöÇ common/                   # Shared utilities
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ logger.ps1            # PowerShell logging
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ logger.sh             # Bash logging
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ azure-auth.ps1        # Azure authentication
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ azure-auth.sh
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ config-loader.ps1     # Configuration management
Γöé   Γöé   Γöé   ΓööΓöÇΓöÇ config-loader.sh
Γöé   Γöé   Γöé
Γöé   Γöé   Γö£ΓöÇΓöÇ preflight/                # Pre-deployment checks
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ check-prerequisites.ps1
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ check-prerequisites.sh
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ validate-secrets.ps1
Γöé   Γöé   Γöé   ΓööΓöÇΓöÇ validate-secrets.sh
Γöé   Γöé   Γöé
Γöé   Γöé   Γö£ΓöÇΓöÇ database/                 # Database deployment
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-schema.ps1
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-schema.sh
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ migrate-data.ps1
Γöé   Γöé   Γöé   ΓööΓöÇΓöÇ migrate-data.sh
Γöé   Γöé   Γöé
Γöé   Γöé   Γö£ΓöÇΓöÇ application/              # Application deployment
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-api.ps1
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-api.sh
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ configure-service.ps1
Γöé   Γöé   Γöé   ΓööΓöÇΓöÇ configure-service.sh
Γöé   Γöé   Γöé
Γöé   Γöé   Γö£ΓöÇΓöÇ neo4j/                    # Neo4j deployment
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-neo4j-worker.ps1
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ deploy-neo4j-worker.sh
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ configure-neo4j.ps1
Γöé   Γöé   Γöé   ΓööΓöÇΓöÇ configure-neo4j.sh
Γöé   Γöé   Γöé
Γöé   Γöé   Γö£ΓöÇΓöÇ validation/               # Post-deployment validation
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ health-check.ps1
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ health-check.sh
Γöé   Γöé   Γöé   Γö£ΓöÇΓöÇ smoke-test.ps1
Γöé   Γöé   Γöé   ΓööΓöÇΓöÇ smoke-test.sh
Γöé   Γöé   Γöé
Γöé   Γöé   ΓööΓöÇΓöÇ rollback/                 # Rollback procedures
Γöé   Γöé       Γö£ΓöÇΓöÇ rollback-deployment.ps1
Γöé   Γöé       ΓööΓöÇΓöÇ rollback-deployment.sh
Γöé   Γöé
Γöé   Γö£ΓöÇΓöÇ config/                       # Environment configurations
Γöé   Γöé   Γö£ΓöÇΓöÇ development.json
Γöé   Γöé   Γö£ΓöÇΓöÇ staging.json
Γöé   Γöé   ΓööΓöÇΓöÇ production.json
Γöé   Γöé
Γöé   ΓööΓöÇΓöÇ templates/                    # Infrastructure as Code
Γöé       Γö£ΓöÇΓöÇ bicep/                    # Azure Bicep templates
Γöé       Γöé   Γö£ΓöÇΓöÇ main.bicep
Γöé       Γöé   Γö£ΓöÇΓöÇ networking.bicep
Γöé       Γöé   Γö£ΓöÇΓöÇ compute.bicep
Γöé       Γöé   ΓööΓöÇΓöÇ monitoring.bicep
Γöé       ΓööΓöÇΓöÇ arm/                      # ARM templates (legacy)
Γöé
ΓööΓöÇΓöÇ docs/
    ΓööΓöÇΓöÇ deployment/
        Γö£ΓöÇΓöÇ DEPLOYMENT-ARCHITECTURE.md  # This file
        Γö£ΓöÇΓöÇ DEPLOYMENT-GUIDE.md         # Step-by-step guide
        Γö£ΓöÇΓöÇ TROUBLESHOOTING.md          # Common issues
        ΓööΓöÇΓöÇ RUNBOOK.md                  # Operations runbook
```

---

## ≡ƒöä Deployment Flow

### GitHub Actions Flow

```mermaid
graph TB
    A[Git Push] --> B[Lint & Test]
    B --> C{Branch?}
    C -->|develop| D[Deploy to Development]
    C -->|staging| E[Deploy to Staging]
    C -->|main| F[Manual Approval]
    F --> G[Deploy to Production]

    D --> H[Preflight Checks]
    E --> H
    G --> H

    H --> I[Database Migration]
    I --> J[Application Deployment]
    J --> K[Neo4j Worker Deployment]
    K --> L[Health Checks]
    L --> M{Pass?}
    M -->|Yes| N[Success]
    M -->|No| O[Rollback]
```

### Azure Pipelines Flow

```mermaid
graph TB
    A[Pipeline Trigger] --> B[Build Stage]
    B --> C[Test Stage]
    C --> D{Environment?}
    D -->|Development| E[Deploy Dev]
    D -->|Staging| F[Deploy Staging]
    D -->|Production| G[Approval Gate]

    G --> H[Deploy Prod]

    E --> I[Validation Stage]
    F --> I
    H --> I

    I --> J{Healthy?}
    J -->|Yes| K[Complete]
    J -->|No| L[Rollback]
```

---

## ≡ƒÄ» Deployment Targets

### Development Environment

**Targets**:
- HART-DESKTOP (Windows 11, Arc-enabled)
- Development database: `Hartonomous-DEV-development`
- Neo4j Desktop local instance

**Deployment Method**:
- GitHub Actions self-hosted runner (HART-DESKTOP)
- OR Azure Pipeline agent (HART-DESKTOP)

**Trigger**:
- Automatic on push to `develop` branch

**Approval**: None (auto-deploy)

---

### Staging Environment

**Targets**:
- hart-server (Ubuntu 22.04, Arc-enabled)
- Staging database: TBD
- Neo4j Community Edition

**Deployment Method**:
- GitHub Actions self-hosted runner (hart-server)
- OR Azure Pipeline deployment group (hart-server)

**Trigger**:
- Automatic on push to `staging` branch

**Approval**: Optional (can enable)

---

### Production Environment

**Targets**:
- hart-server (Ubuntu 22.04, Arc-enabled)
- Production database: `Hartonomous` (PostgreSQL on Arc SQL)
- Neo4j Community Edition

**Deployment Method**:
- GitHub Actions with manual approval
- OR Azure Pipeline with approval gate

**Trigger**:
- Manual workflow dispatch
- OR tag push (`v*.*.*`)

**Approval**: **REQUIRED** (manual approval before deploy)

---

## ≡ƒöÉ Security Architecture

### Secret Management Hierarchy

```
Level 1: Azure Key Vault (Source of Truth)
   Γö£ΓöÇΓöÇ PostgreSQL-Hartonomous-Password
   Γö£ΓöÇΓöÇ Neo4j-hart-server-Password
   Γö£ΓöÇΓöÇ AzureAd-ClientSecret
   ΓööΓöÇΓöÇ EntraExternalId-ClientSecret

Level 2: GitHub Secrets (CI/CD credentials)
   Γö£ΓöÇΓöÇ AZURE_CLIENT_ID
   Γö£ΓöÇΓöÇ AZURE_CLIENT_SECRET
   Γö£ΓöÇΓöÇ AZURE_TENANT_ID
   ΓööΓöÇΓöÇ AZURE_SUBSCRIPTION_ID

Level 3: GitHub Environments (Environment-specific)
   Γö£ΓöÇΓöÇ Development
   Γöé   Γö£ΓöÇΓöÇ DEPLOYMENT_TARGET=HART-DESKTOP
   Γöé   ΓööΓöÇΓöÇ ENVIRONMENT=development
   Γö£ΓöÇΓöÇ Staging
   Γöé   Γö£ΓöÇΓöÇ DEPLOYMENT_TARGET=hart-server
   Γöé   ΓööΓöÇΓöÇ ENVIRONMENT=staging
   ΓööΓöÇΓöÇ Production
       Γö£ΓöÇΓöÇ DEPLOYMENT_TARGET=hart-server
       Γö£ΓöÇΓöÇ ENVIRONMENT=production
       ΓööΓöÇΓöÇ APPROVAL_REQUIRED=true
```

### Authentication Flow

```
1. Workflow/Pipeline starts
   Γåô
2. Authenticate to Azure using Service Principal
   (GitHub Secret: AZURE_CLIENT_ID, AZURE_CLIENT_SECRET)
   Γåô
3. Access Key Vault using Service Principal
   (Role: Key Vault Secrets User)
   Γåô
4. Retrieve application secrets
   (PostgreSQL password, Neo4j password, etc.)
   Γåô
5. Deploy to target using Arc-enabled machine
   (Via SSH for Linux, WinRM for Windows)
   Γåô
6. Configure application with secrets
   (Environment variables, config files)
```

---

## ≡ƒôª Deployment Stages

### Stage 1: Preflight Checks

**Purpose**: Validate environment before deployment

**Scripts**:
- `deployment/scripts/preflight/check-prerequisites.{ps1,sh}`
- `deployment/scripts/preflight/validate-secrets.{ps1,sh}`

**Checks**:
- Γ£à Target machine is online and accessible
- Γ£à Required services are installed (PostgreSQL, Neo4j, Python)
- Γ£à Disk space available (>10GB)
- Γ£à Azure connectivity (Arc agent connected)
- Γ£à Secrets exist in Key Vault
- Γ£à Service Principal has required permissions

**Output**: `PASS` or `FAIL` (exit code 0 or 1)

---

### Stage 2: Database Migration

**Purpose**: Deploy schema changes and data migrations

**Scripts**:
- `deployment/scripts/database/deploy-schema.{ps1,sh}`
- `deployment/scripts/database/migrate-data.{ps1,sh}`

**Steps**:
1. Backup current database
2. Apply schema changes (idempotent SQL scripts)
3. Run data migrations (if needed)
4. Verify schema integrity
5. Update migration version

**Rollback**: Restore from backup if any step fails

---

### Stage 3: Application Deployment

**Purpose**: Deploy API and worker services

**Scripts**:
- `deployment/scripts/application/deploy-api.{ps1,sh}`
- `deployment/scripts/neo4j/deploy-neo4j-worker.{ps1,sh}`

**Steps**:
1. Stop existing services (graceful shutdown)
2. Backup current application files
3. Deploy new application files
4. Update configuration files (environment-specific)
5. Install Python dependencies
6. Start services
7. Verify services are running

**Rollback**: Restore backup and restart previous version

---

### Stage 4: Validation

**Purpose**: Verify deployment was successful

**Scripts**:
- `deployment/scripts/validation/health-check.{ps1,sh}`
- `deployment/scripts/validation/smoke-test.{ps1,sh}`

**Tests**:
- Γ£à API responds to `/health` endpoint
- Γ£à Database connection successful
- Γ£à Neo4j worker connected
- Γ£à Sample data ingest works
- Γ£à Provenance graph updates
- Γ£à No errors in logs

**Output**: `PASS` or `FAIL`

**Action on Fail**: Automatic rollback

---

## ≡ƒöº Script Design Patterns

### Idempotency Pattern

```powershell
# Γ¥î BAD: Not idempotent
Install-Service "Hartonomous-API"

# Γ£à GOOD: Idempotent
if (-not (Get-Service "Hartonomous-API" -ErrorAction SilentlyContinue)) {
    Install-Service "Hartonomous-API"
    Write-Log "Service installed"
} else {
    Write-Log "Service already exists, skipping installation"
}
```

### Error Handling Pattern

```powershell
# Strict error handling
$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

try {
    # Deployment logic
    Deploy-Application
    Write-Log "Deployment successful"
    exit 0
} catch {
    Write-Log "ERROR: $($_.Exception.Message)" -Level Error
    Invoke-Rollback
    exit 1
}
```

### Logging Pattern

```powershell
# Centralized logging
. "$PSScriptRoot/../common/logger.ps1"

Write-Log "Starting deployment" -Level Info
Write-Log "Configuration loaded" -Level Debug
Write-Log "WARNING: Using development secrets" -Level Warning
Write-Log "ERROR: Deployment failed" -Level Error
```

### Configuration Pattern

```powershell
# Configuration loading
. "$PSScriptRoot/../common/config-loader.ps1"

$config = Get-Configuration -Environment $env:DEPLOYMENT_ENVIRONMENT
$dbHost = $config.database.host
$dbPort = $config.database.port
```

---

## ≡ƒîÉ Environment Variables

### Required Environment Variables

| Variable | Description | Source |
|----------|-------------|--------|
| `DEPLOYMENT_ENVIRONMENT` | Environment name (development/staging/production) | GitHub/Azure |
| `DEPLOYMENT_TARGET` | Target machine (HART-DESKTOP/hart-server) | GitHub/Azure |
| `AZURE_TENANT_ID` | Azure AD tenant ID | GitHub Secret |
| `AZURE_CLIENT_ID` | Service Principal client ID | GitHub Secret |
| `AZURE_CLIENT_SECRET` | Service Principal secret | GitHub Secret |
| `AZURE_SUBSCRIPTION_ID` | Azure subscription ID | GitHub Secret |
| `KEY_VAULT_URL` | Key Vault URL | Config file |
| `APP_CONFIG_ENDPOINT` | App Configuration endpoint | Config file |

### Optional Environment Variables

| Variable | Description | Default |
|----------|-------------|---------|
| `DEPLOYMENT_VERSION` | Version to deploy | `latest` |
| `SKIP_TESTS` | Skip validation tests | `false` |
| `ENABLE_ROLLBACK` | Enable auto-rollback on failure | `true` |
| `LOG_LEVEL` | Logging verbosity (DEBUG/INFO/WARNING/ERROR) | `INFO` |

---

## ≡ƒôè Monitoring & Observability

### Deployment Metrics

**Tracked Metrics**:
- Deployment duration
- Success/failure rate
- Rollback frequency
- Time to rollback
- Health check response time

**Storage**: Azure Application Insights

### Logging

**Log Levels**:
- `DEBUG`: Detailed diagnostic information
- `INFO`: General informational messages
- `WARNING`: Warning messages (deployment continues)
- `ERROR`: Error messages (deployment fails)

**Log Destinations**:
- Console (stdout/stderr)
- File (`/var/log/hartonomous/deployment.log`)
- Azure Application Insights

### Alerting

**Alert Triggers**:
- Deployment failure
- Rollback triggered
- Health check failure
- Service stopped unexpectedly

**Alert Channels**:
- Email (aharttn@gmail.com)
- GitHub issue (auto-created)
- Azure Monitor alerts

---

## ≡ƒöä Rollback Strategy

### Automatic Rollback

**Triggers**:
- Health check failure
- Smoke test failure
- Service won't start
- Database migration error

**Process**:
1. Log rollback trigger
2. Stop new services
3. Restore application backup
4. Restore database backup (if migration failed)
5. Start previous services
6. Verify health checks pass
7. Create incident report

### Manual Rollback

**Command**:
```bash
# GitHub Actions
gh workflow run rollback.yml -f environment=production -f version=v1.2.3

# Azure Pipelines
az pipelines run --name rollback-pipeline --parameters environment=production version=v1.2.3
```

---

## ≡ƒÄô Best Practices

### Do's Γ£à

- Γ£à Use environment-specific configuration files
- Γ£à Store secrets in Azure Key Vault only
- Γ£à Test deployments in development first
- Γ£à Always backup before migration
- Γ£à Use semantic versioning (v1.2.3)
- Γ£à Log every deployment action
- Γ£à Validate after every deployment
- Γ£à Use idempotent scripts

### Don'ts Γ¥î

- Γ¥î Never hardcode secrets in scripts
- Γ¥î Never commit secrets to git
- Γ¥î Never deploy without preflight checks
- Γ¥î Never skip validation tests
- Γ¥î Never deploy manually without CI/CD
- Γ¥î Never inline scripts in YAML
- Γ¥î Never deploy during business hours (production)
- Γ¥î Never skip approval gates (production)

---

## ≡ƒôÜ Related Documentation

- [Deployment Guide](DEPLOYMENT-GUIDE.md) - Step-by-step deployment instructions
- [Troubleshooting](TROUBLESHOOTING.md) - Common issues and solutions
- [Runbook](RUNBOOK.md) - Operations runbook
- [Neo4j Implementation](../development/NEO4J-IMPLEMENTATION.md) - Neo4j deployment specifics

---

**Copyright ┬⌐ 2025 Anthony Hart. All Rights Reserved.**
