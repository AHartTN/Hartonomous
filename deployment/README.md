# Hartonomous Deployment System

**Complete orchestration system for deploying Hartonomous to any environment.**

---

## ?? **Quick Start**

### **Single Command Deployment**

```bash
# Deploy to development
./deployment/orchestrate.sh deploy development

# Deploy to production (with confirmation)
./deployment/orchestrate.sh deploy production

# Deploy to staging (skip tests)
./deployment/orchestrate.sh deploy staging --skip-tests
```

---

## ?? **Architecture**

```
deployment/
??? orchestrate.sh              ? MASTER ORCHESTRATOR (single entry point)
??? config/
?   ??? development.json        ? Dev environment config
?   ??? staging.json            ? Staging environment config
?   ??? production.json         ? Production environment config
??? scripts/
    ??? preflight/              ? Pre-deployment checks
    ?   ??? check-prerequisites.sh
    ??? database/               ? Database operations
    ?   ??? backup-database.sh
    ?   ??? deploy-schema.sh
    ??? application/            ? Application deployment
    ?   ??? backup-application.sh
    ?   ??? deploy-api.sh
    ??? neo4j/                  ? Neo4j worker
    ?   ??? deploy-neo4j-worker.sh
    ??? validation/             ? Post-deployment validation
    ?   ??? health-check.sh
    ?   ??? smoke-test.sh
    ?   ??? run-security-scan.sh
    ??? common/                 ? Shared utilities
        ??? logger.sh
        ??? config-loader.sh
        ??? azure-auth.sh
```

---

## ?? **Commands**

### **Full Deployment**
```bash
./deployment/orchestrate.sh deploy <environment> [options]

Environments:
  development, dev    - Local/Dev environment
  staging, stage      - Staging environment
  production, prod    - Production environment

Options:
  --skip-tests       - Skip smoke tests
  --skip-backup      - Skip backup creation
  --force            - Skip confirmation prompts
```

### **Partial Deployments**
```bash
# Database only
./deployment/orchestrate.sh db-only production

# API only
./deployment/orchestrate.sh api-only production
```

### **Validation**
```bash
# Run preflight checks
./deployment/orchestrate.sh preflight production

# Run validation tests
./deployment/orchestrate.sh validate production

# Check deployment status
./deployment/orchestrate.sh status production
```

### **Monitoring**
```bash
# View logs
./deployment/orchestrate.sh logs production
```

---

## ?? **Deployment Flow**

```
orchestrate.sh deploy <env>
    ?
1. Preflight Checks
   ?? Prerequisites (Python, psql, az cli)
   ?? Azure connectivity
   ?? Neo4j availability
   ?? Environment variables
    ?
2. Backup
   ?? Database backup
   ?? Application backup
    ?
3. Database Deployment
   ?? Load extensions
   ?? Create tables
   ?? Load functions
   ?? Create triggers
    ?
4. API Deployment
   ?? Install dependencies
   ?? Configure .env
   ?? Retrieve secrets from Key Vault
   ?? Start service
    ?
5. Neo4j Worker
   ?? Verify connectivity
   ?? Check driver installed
   ?? Test connection
    ?
6. Validation
   ?? Health checks
   ?? Smoke tests
   ?? Log verification
```

---

## ?? **Configuration**

### **Environment Files**

Each environment has a JSON configuration file:

```json
{
  "environment": "production",
  "target": {
    "machine": "hart-server",
    "os": "linux"
  },
  "database": {
    "host": "localhost",
    "port": 5432,
    "name": "hartonomous",
    "user": "ai_architect"
  },
  "api": {
    "host": "0.0.0.0",
    "port": 8000,
    "workers": 4,
    "reload": false
  },
  "neo4j": {
    "uri": "bolt://localhost:7687",
    "user": "neo4j",
    "database": "neo4j"
  },
  "features": {
    "neo4j_enabled": true,
    "age_worker_enabled": false,
    "auth_enabled": true
  },
  "azure": {
    "key_vault_url": "https://kv-hartonomous.vault.azure.net/",
    "app_config_endpoint": "https://appconfig-hartonomous.azconfig.io",
    "tenant_id": "6c9c44c4-f04b-4b5f-bea0-f1069179799c"
  },
  "logging": {
    "level": "INFO",
    "format": "json"
  }
}
```

### **Environment Variables**

Required for deployment:

```bash
export DEPLOYMENT_ENVIRONMENT=production
export AZURE_TENANT_ID=6c9c44c4-f04b-4b5f-bea0-f1069179799c
export AZURE_CLIENT_ID=<from-keyvault>
export AZURE_CLIENT_SECRET=<from-keyvault>
export LOG_LEVEL=INFO
```

---

## ?? **Security**

### **Secrets Management**
- All secrets stored in Azure Key Vault
- Accessed via Managed Identity (Arc machines)
- Never committed to Git

### **Credentials Flow**
```
1. Arc Machine Managed Identity
    ?
2. Key Vault (RBAC: Secrets User)
    ?
3. Retrieve secrets at deployment time
    ?
4. Populate .env file (ephemeral)
    ?
5. API loads from .env
```

---

## ?? **GitHub Actions Integration**

The orchestrator is called by GitHub Actions workflows:

```yaml
# .github/workflows/cd-deploy-production.yml
jobs:
  deploy:
    runs-on: [self-hosted, linux, hart-server]
    steps:
      - uses: actions/checkout@v4
      
      - name: Deploy
        env:
          DEPLOYMENT_ENVIRONMENT: production
        run: |
          chmod +x ./deployment/orchestrate.sh
          ./deployment/orchestrate.sh deploy production --force
```

---

## ?? **Testing Deployment Locally**

### **Development Environment**
```bash
# Run full deployment locally
./deployment/orchestrate.sh deploy development

# Expected output:
# ? Preflight checks passed
# ? Database backup created
# ? Schema deployed
# ? API deployed
# ? Neo4j worker configured
# ? Validation passed
```

### **Dry Run (Preflight Only)**
```bash
./deployment/orchestrate.sh preflight development
```

---

## ?? **Troubleshooting**

### **Prerequisites Failed**
```bash
# Check what's missing
./deployment/orchestrate.sh preflight development

# Common issues:
# - Python not installed: apt-get install python3
# - Azure CLI not installed: curl -L https://aka.ms/InstallAzureCli | bash
# - psql not installed: apt-get install postgresql-client
```

### **Database Deployment Failed**
```bash
# Check PostgreSQL
psql -h localhost -U hartonomous -d hartonomous -c "SELECT version();"

# Re-run database only
./deployment/orchestrate.sh db-only development
```

### **API Deployment Failed**
```bash
# Check logs
tail -f /var/log/hartonomous/api-*.log

# Re-run API only
./deployment/orchestrate.sh api-only development
```

### **Validation Failed**
```bash
# Check API health manually
curl http://localhost:8000/v1/health

# Check specific service
./deployment/orchestrate.sh status development
```

---

## ?? **Deployment Checklist**

### **Before Deployment**
- [ ] All code changes committed and pushed
- [ ] Tests passing locally
- [ ] Configuration reviewed
- [ ] Secrets verified in Key Vault
- [ ] Backup verified (production only)

### **During Deployment**
- [ ] Monitor logs: `./deployment/orchestrate.sh logs <env>`
- [ ] Watch for errors
- [ ] Verify each step completes

### **After Deployment**
- [ ] Run validation: `./deployment/orchestrate.sh validate <env>`
- [ ] Check health: `curl http://localhost:8000/v1/health`
- [ ] Test key endpoints
- [ ] Verify Neo4j sync: `python verify-provenance.py`
- [ ] Monitor for 15 minutes

---

## ?? **Rollback Procedure**

```bash
# Automatic rollback (not yet implemented)
./deployment/orchestrate.sh rollback production

# Manual rollback:
1. Stop API service
2. Restore database from backup
3. Restore previous API version
4. Restart services
5. Validate
```

---

## ?? **Additional Documentation**

- [Azure Configuration](../docs/azure/AZURE-CONFIGURATION.md)
- [Security Guide](../docs/security/CREDENTIALS.md)
- [Neo4j Provenance](../docs/architecture/neo4j-provenance.md)
- [API Documentation](http://localhost:8000/docs)

---

## ?? **Support**

### **Logs Location**
```
/var/log/hartonomous/
??? preflight-*.log
??? deploy-*.log
??? api-*.log
??? neo4j-worker-*.log
```

### **Configuration Files**
```
deployment/config/<environment>.json
api/.env (ephemeral, created during deployment)
```

### **Health Endpoints**
```
http://localhost:8000/v1/health    - Basic health
http://localhost:8000/v1/ready     - Readiness (database connected)
http://localhost:8000/v1/stats     - Statistics
http://localhost:8000/docs         - API documentation
```

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
