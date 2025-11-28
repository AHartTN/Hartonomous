# Deployment Pipeline Audit - November 27, 2025

## ✅ Working Locally
- API: http://localhost:8000 (NO pool warnings!)
- Database: PostgreSQL 16 (954 functions)
- Text ingestion: WORKING
- Health: OK

## 📋 Deployment Files Status

### Scripts
- `scripts/init-database.sh` - Schema initialization ✅
- `scripts/fix-pg-auth.sh` - Local auth fix ✅
- `scripts/install-ollama.sh` - Ollama setup
- `scripts/setup-credentials.sh` - Credentials
- `scripts/Initialize-Database.ps1` - Windows version

### Docker
- `docker/Dockerfile` - Main PostgreSQL image
- `docker/Dockerfile.api` - FastAPI image
- `docker/init-db.sh` - DB init for container
- `docker-compose.yml` - Full stack orchestration

### GitHub Actions
- `.github/workflows/ci-cd.yml` - CI/CD pipeline

## 🔍 Files to Update

### 1. Docker Compose (docker-compose.yml)
**Issues:**
- Uses PostgreSQL 15, we deployed 16
- References old schema structure
- CODE_ATOMIZER_URL hardcoded
- Neo4j password in plain text

**Updates Needed:**
```yaml
postgres:
  image: postgis/postgis:16-3.4  # CHANGE FROM 15-3.4
  environment:
    POSTGRES_PASSWORD: ${PGPASSWORD}  # From .env
```

### 2. Dockerfile Updates
**docker/Dockerfile** - PostgreSQL image
- Update to Postgres 16
- Add PG-Strom if GPU needed
- Optimize schema loading

**docker/Dockerfile.api** - API image
- Verify requirements.txt complete
- Add document parser deps (pdfplumber, python-docx)
- Add healthcheck

### 3. CI/CD Pipeline
**Issues:**
- Placeholder deployment logic
- No actual Azure deployment commands
- Missing environment-specific configs

### 4. New Files Needed
- `.env.production` - Production config template
- `docker/Dockerfile.code-atomizer` - C# atomizer image
- `scripts/deploy-azure.sh` - Azure deployment script
- `k8s/` - Kubernetes manifests (future)

## 🚀 Priority Updates

1. **Update docker-compose.yml** (PostgreSQL 16, correct ports)
2. **Update Dockerfiles** (add new dependencies)
3. **Create .env templates** for each environment
4. **Fix CI/CD deployment** (actual Azure commands)
5. **Add document parser deps** to requirements.txt

## Next: Make these updates and commit for deployment testing
