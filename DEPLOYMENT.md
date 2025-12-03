# Hartonomous Deployment Guide

## Zero-Configuration Automated Deployment

This repository is configured for **completely automated deployment** via Azure Pipelines.

### For New Users (Clone & Deploy)

```bash
# 1. Clone repository
git clone https://dev.azure.com/aharttn/Hartonomous/_git/Hartonomous
cd Hartonomous

# 2. Push to trigger pipeline (or manually run in Azure DevOps)
# That's it! Everything else is automatic.
```

The pipeline will automatically:
1. ✅ Configure Zero Trust RBAC
2. ✅ Set up managed identities
3. ✅ Populate Key Vault with secrets
4. ✅ Deploy applications to Arc machines
5. ✅ Apply database migrations

**No manual configuration required!**

## Architecture

```
┌─────────────────────────────────────────────────┐
│ Azure DevOps Pipeline (Fully Automated)         │
│  1. Infrastructure Stage (Zero Trust Setup)     │
│  2. Build Stage (Docker + Tests)                │
│  3. Deploy Stage (Arc Machines)                 │
└─────────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────────┐
│ Azure Arc Machines (On-Prem)                    │
│  • hart-server (Linux - localhost/dev)          │
│  • HART-DESKTOP (Windows - staging/production)  │
└─────────────────────────────────────────────────┘
                    ↓
┌─────────────────────────────────────────────────┐
│ PostgreSQL + PostGIS + pgvector                 │
│  • 4 databases (ports 5432-5435)                │
│  • Azure AD authentication                      │
│  • Multi-tenant isolation                       │
└─────────────────────────────────────────────────┘
```

## Environments

| Environment | Branch | Arc Machine | Database Port |
|-------------|--------|-------------|---------------|
| localhost   | main   | hart-server | 5432 |
| dev         | develop| hart-server | 5433 |
| staging     | develop| HART-DESKTOP| 5434 |
| production  | main   | HART-DESKTOP| 5435 |

## Manual Deployment (Optional)

If you need to deploy manually (bypassing pipeline):

### Local Development
```powershell
# Verify prerequisites
.\scripts\verify-environment.ps1

# Start Docker stack
docker-compose up -d

# Apply migrations
.\scripts\deploy-migrations.ps1 -Environment localhost

# Run API
cd Hartonomous.Api
dotnet run
```

### Deploy to Arc Machines (Manual)
```powershell
# One-time infrastructure setup (if not using pipeline)
.\infrastructure\setup-zero-trust.ps1

# Deploy to specific environment
.\scripts\deploy-migrations.ps1 -Environment dev
```

## What Gets Deployed

### PostgreSQL Databases
- PostGIS 16 with spatial geometry support
- pgvector for embedding similarity search
- PL/Python3 for GPU compute (CuPy/CUDA)
- Azure AD authentication enabled
- Multi-tenant data isolation

### ASP.NET Core API
- .NET 10 Web API
- Entra ID JWT authentication
- Managed identity → PostgreSQL
- Role-based authorization (Reader/Writer/Admin)
- Key Vault integration

### MAUI App (Future)
- Cross-platform Blazor Hybrid
- Azure AD authentication
- Calls API with user tokens

## Security Features

**Zero Trust:**
- ✅ No passwords in code or config
- ✅ Managed identities for all service-to-service auth
- ✅ JWT tokens for user authentication
- ✅ RBAC at API and database layers
- ✅ Multi-tenant isolation (tenant_id filtering)
- ✅ All secrets in Key Vault

**Compliance:**
- ✅ pgaudit logging enabled
- ✅ Tenant tracking for data poisoning detection
- ✅ Immutable infrastructure (Bicep templates)
- ✅ Automated deployments (audit trail)

## Monitoring

**Application Insights:** `hartonomous-insights`
- API telemetry
- Database performance
- Exception tracking

**Log Analytics:** `Development` workspace
- Centralized logging
- Query across all environments

## Troubleshooting

### Pipeline Fails on Infrastructure Stage
- Check service connection has Key Vault Administrator role
- Verify `Hartonomous API (Production)` service principal exists

### Cannot Connect to Database
- Verify Arc machine has SystemAssigned identity
- Check RBAC: `az role assignment list --assignee <IDENTITY> --scope /subscriptions/.../kv-hartonomous`
- Ensure managed identity has Key Vault Secrets User role

### Migrations Fail
- Check connection string in Key Vault
- Verify PostgreSQL container is running: `docker ps | grep postgres`
- Check logs: `docker logs hartonomous-db-{environment}`

## Cost Optimization

**Current Costs:**
- Azure Arc agents: FREE
- Managed identities: FREE
- RBAC role assignments: FREE
- Key Vault (already provisioned): ~$0.03/10k operations
- App Configuration: FREE tier
- PostgreSQL: $0 (Docker on-prem)

**Total incremental cost: ~$0**

## Next Steps

1. **Customize** entity models in `Hartonomous.Db/Entities/`
2. **Add migrations** when schema changes
3. **Extend API** with new controllers
4. **Build MAUI app** for cross-platform client

Everything deploys automatically via pipeline!
