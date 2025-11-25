# Hartonomous Deployment - Quick Start Guide

**Get your first deployment running in 5 minutes!**

---

## 🚀 Prerequisites

### HART-DESKTOP (Windows - Development)
- ✅ Windows 11 Pro
- ✅ Python 3.10+
- ✅ PostgreSQL 15+
- ✅ Neo4j Desktop
- ✅ Azure CLI
- ✅ Git
- ✅ PowerShell 7+

### hart-server (Linux - Staging/Production)
- ✅ Ubuntu 22.04 LTS
- ✅ Python 3.10+
- ✅ PostgreSQL 15+
- ✅ Neo4j Community Edition
- ✅ Azure CLI
- ✅ Git
- ✅ Bash 5+

---

## 📋 Step 1: Environment Variables

Create a `.env` file or set environment variables:

### Development (HART-DESKTOP)
```powershell
$env:DEPLOYMENT_ENVIRONMENT = "development"
$env:DEPLOYMENT_TARGET = "HART-DESKTOP"
$env:AZURE_TENANT_ID = "6c9c44c4-f04b-4b5f-bea0-f1069179799c"
$env:AZURE_CLIENT_ID = "66a37c0f-5666-450b-b61f-c9e33b56115e"  # GitHub Actions Dev SP
$env:AZURE_CLIENT_SECRET = "<from-github-secrets>"
$env:AZURE_SUBSCRIPTION_ID = "ed614e1a-7d8b-4608-90c8-66e86c37080b"
$env:KEY_VAULT_URL = "https://kv-hartonomous.vault.azure.net/"
$env:LOG_LEVEL = "DEBUG"
```

### Staging/Production (hart-server)
```bash
export DEPLOYMENT_ENVIRONMENT="staging"  # or "production"
export DEPLOYMENT_TARGET="hart-server"
export AZURE_TENANT_ID="6c9c44c4-f04b-4b5f-bea0-f1069179799c"
export AZURE_CLIENT_ID="<production-sp-client-id>"
export AZURE_CLIENT_SECRET="<from-key-vault>"
export AZURE_SUBSCRIPTION_ID="ed614e1a-7d8b-4608-90c8-66e86c37080b"
export KEY_VAULT_URL="https://kv-hartonomous.vault.azure.net/"
export LOG_LEVEL="INFO"
```

---

## ✅ Step 2: Run Preflight Checks

### Windows (PowerShell)
```powershell
cd D:\Repositories\Hartonomous
.\deployment\scripts\preflight\check-prerequisites.ps1
```

**Expected Output**:
```
═══════════════════════════════════════════════════════
  Preflight Checks - Prerequisites
═══════════════════════════════════════════════════════

[2025-11-25 13:00:00] [INFO] Environment: development
[2025-11-25 13:00:00] [INFO] Target: HART-DESKTOP

═══════════════════════════════════════════════════════
  Checking Disk Space
═══════════════════════════════════════════════════════
✅ Disk space: 150.5GB available

═══════════════════════════════════════════════════════
  Checking Python Installation
═══════════════════════════════════════════════════════
✅ Python: Python 3.11.0

... (more checks) ...

✅ All critical prerequisites validated
```

### Linux (Bash)
```bash
cd /path/to/Hartonomous
chmod +x ./deployment/scripts/preflight/check-prerequisites.sh
./deployment/scripts/preflight/check-prerequisites.sh
```

---

## 🏥 Step 3: Test Health Checks

### Start Your API First
```bash
# Development
cd api
python -m uvicorn main:app --reload --port 8000
```

### Run Health Checks

**Windows**:
```powershell
.\deployment\scripts\validation\health-check.ps1
```

**Linux**:
```bash
chmod +x ./deployment/scripts/validation/health-check.sh
./deployment/scripts/validation/health-check.sh
```

**Expected Output**:
```
═══════════════════════════════════════════════════════
  Health Check Summary
═══════════════════════════════════════════════════════

Results: 4/4 checks passed

✅ API Health: PASS
✅ Database: PASS
✅ Neo4j: PASS
⏭️ Metrics: SKIP

✅ All health checks passed
```

---

## 🎯 Step 4: Deploy to Development

### Manual Deployment (Local Testing)

**Windows**:
```powershell
# 1. Preflight checks
.\deployment\scripts\preflight\check-prerequisites.ps1

# 2. Database deployment (placeholder - to be created)
# .\deployment\scripts\database\deploy-schema.ps1

# 3. Application deployment (placeholder - to be created)
# .\deployment\scripts\application\deploy-api.ps1

# 4. Health checks
.\deployment\scripts\validation\health-check.ps1
```

### GitHub Actions Deployment

1. **Push to `develop` branch**:
```bash
git checkout -b develop
git add .
git commit -m "feat: Add deployment infrastructure"
git push origin develop
```

2. **Watch GitHub Actions**:
   - Go to: https://github.com/AHartTN/Hartonomous/actions
   - Watch "CD - Deploy to Development" workflow
   - Monitor logs in real-time

3. **Self-Hosted Runner Setup** (if not already done):
```powershell
# On HART-DESKTOP
cd C:\actions-runner
.\run.cmd

# Verify runner is online in GitHub
```

---

## 📁 What Was Created

### GitHub Actions Workflows (11 files)
```
.github/workflows/
├── ci-lint.yml                      # Code linting (Python, YAML, Markdown, SQL)
├── ci-test.yml                      # Unit & integration tests
├── ci-security.yml                  # Security scanning (secrets, dependencies, SAST)
├── cd-deploy-development.yml        # Deploy to HART-DESKTOP
├── cd-deploy-staging.yml            # Deploy to hart-server (staging)
├── cd-deploy-production.yml         # Deploy to hart-server (production, with approval)
├── community-sponsor.yml            # GitHub Sponsors automation
├── community-coffee.yml             # Ko-fi/Buy Me a Coffee
├── community-issue-management.yml   # Issue auto-labeling & welcome
├── community-pr-management.yml      # PR auto-labeling & reviewer assignment
└── ciam-user-provisioning.yml       # Azure B2C/CIAM user management
```

### Deployment Scripts (Agnostic PowerShell/Bash)
```
deployment/scripts/
├── common/
│   ├── logger.ps1 & logger.sh           # Logging framework
│   ├── azure-auth.ps1 & azure-auth.sh   # Azure authentication
│   └── config-loader.ps1 & config-loader.sh  # Configuration management
├── preflight/
│   └── check-prerequisites.{ps1,sh}     # System validation
└── validation/
    └── health-check.{ps1,sh}            # Post-deployment health checks
```

### Configuration Files (Environment-Specific)
```
deployment/config/
├── development.json   # HART-DESKTOP settings
├── staging.json       # hart-server staging settings
└── production.json    # hart-server production settings
```

### Documentation
```
docs/deployment/
├── DEPLOYMENT-ARCHITECTURE.md  # Complete architecture guide
└── QUICK-START.md             # This file
```

---

## 🧪 Testing the Deployment System

### Test 1: Preflight Checks
```powershell
# Should complete successfully
.\deployment\scripts\preflight\check-prerequisites.ps1
```

**Validates**:
- ✅ Disk space (>10GB)
- ✅ Python 3.10+
- ✅ PostgreSQL accessible
- ✅ Neo4j running
- ✅ Azure CLI installed
- ✅ Network connectivity
- ✅ Environment variables set

### Test 2: Configuration Loading
```powershell
# Test configuration loading
. .\deployment\scripts\common\config-loader.ps1
$config = Get-DeploymentConfig -Environment "development"
$config | ConvertTo-Json
```

**Expected**: JSON configuration for development environment

### Test 3: Azure Authentication
```powershell
# Test Azure login
. .\deployment\scripts\common\azure-auth.ps1
Connect-AzureWithServicePrincipal `
    -TenantId $env:AZURE_TENANT_ID `
    -ClientId $env:AZURE_CLIENT_ID `
    -ClientSecret $env:AZURE_CLIENT_SECRET
```

**Expected**: Successful Azure login with subscription details

### Test 4: Health Checks
```powershell
# Start API first
cd api
python -m uvicorn main:app --reload

# In another terminal
.\deployment\scripts\validation\health-check.ps1
```

**Expected**: All health checks pass

---

## 🔧 Troubleshooting

### Issue: Preflight check fails on Python version
```
Python 3.10+ required, found: Python 3.9.0
```

**Solution**: Install Python 3.10+ from python.org

---

### Issue: Azure authentication fails
```
Azure authentication failed: AADSTS700016
```

**Solution**:
1. Verify Service Principal exists:
   ```powershell
   az ad sp show --id $env:AZURE_CLIENT_ID
   ```
2. Check client secret is correct
3. Verify SP has "Key Vault Secrets User" role:
   ```powershell
   az role assignment list --assignee $env:AZURE_CLIENT_ID
   ```

---

### Issue: Neo4j health check fails
```
Neo4j health check failed
```

**Solution**:
1. Start Neo4j Desktop
2. Verify port 7687 is open:
   ```powershell
   Test-NetConnection -ComputerName localhost -Port 7687
   ```
3. Check credentials match configuration

---

### Issue: Permission denied on Linux scripts
```
bash: ./deployment/scripts/preflight/check-prerequisites.sh: Permission denied
```

**Solution**:
```bash
chmod +x ./deployment/scripts/**/*.sh
```

---

## 🎯 Next Steps

### 1. Complete Remaining Deployment Scripts

Create these missing scripts (following the existing patterns):
- `deployment/scripts/database/deploy-schema.{ps1,sh}`
- `deployment/scripts/application/deploy-api.{ps1,sh}`
- `deployment/scripts/neo4j/deploy-neo4j-worker.{ps1,sh}`
- `deployment/scripts/validation/smoke-test.{ps1,sh}`
- `deployment/scripts/rollback/rollback-deployment.{ps1,sh}`

### 2. Configure GitHub Secrets

Add these secrets to your GitHub repository:
```
Settings → Secrets and variables → Actions → New repository secret
```

**Required Secrets**:
- `AZURE_TENANT_ID`
- `AZURE_CLIENT_ID` (Development SP)
- `AZURE_CLIENT_SECRET` (Development SP)
- `AZURE_CLIENT_ID_PROD` (Production SP)
- `AZURE_CLIENT_SECRET_PROD` (Production SP)
- `AZURE_SUBSCRIPTION_ID`
- `KEY_VAULT_URL`

### 3. Set Up Self-Hosted Runners

#### HART-DESKTOP Runner
```powershell
# Download runner
cd C:\
mkdir actions-runner
cd actions-runner
Invoke-WebRequest -Uri https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-win-x64-2.311.0.zip -OutFile actions-runner-win-x64-2.311.0.zip
Expand-Archive -Path actions-runner-win-x64-2.311.0.zip -DestinationPath .

# Configure
.\config.cmd --url https://github.com/AHartTN/Hartonomous --token <YOUR_TOKEN> --labels windows,HART-DESKTOP

# Run
.\run.cmd
```

#### hart-server Runner
```bash
# Download runner
mkdir actions-runner && cd actions-runner
curl -o actions-runner-linux-x64-2.311.0.tar.gz -L https://github.com/actions/runner/releases/download/v2.311.0/actions-runner-linux-x64-2.311.0.tar.gz
tar xzf ./actions-runner-linux-x64-2.311.0.tar.gz

# Configure
./config.sh --url https://github.com/AHartTN/Hartonomous --token <YOUR_TOKEN> --labels linux,hart-server

# Run as service
sudo ./svc.sh install
sudo ./svc.sh start
```

### 4. Test Full CI/CD Pipeline

```bash
# Create a test branch
git checkout -b test-deployment

# Make a small change
echo "# Test" >> README.md

# Commit and push
git add README.md
git commit -m "test: Trigger deployment pipeline"
git push origin test-deployment

# Merge to develop to trigger deployment
gh pr create --base develop --title "Test deployment pipeline"
gh pr merge --auto --squash
```

---

## 📚 Additional Resources

- **Full Architecture**: [DEPLOYMENT-ARCHITECTURE.md](DEPLOYMENT-ARCHITECTURE.md)
- **Neo4j Implementation**: [../development/NEO4J-IMPLEMENTATION.md](../development/NEO4J-IMPLEMENTATION.md)
- **Security Audit**: [../../SECURITY-AUDIT-REPORT.md](../../SECURITY-AUDIT-REPORT.md)

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
