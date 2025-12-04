# Hartonomous Deployment Guide

Complete guide for deploying Hartonomous to localhost, development, staging, and production environments.

---

## ?? Table of Contents

- [Prerequisites](#prerequisites)
- [Quick Start (Localhost)](#quick-start-localhost)
- [Environment Setup](#environment-setup)
- [Deployment Workflows](#deployment-workflows)
- [Troubleshooting](#troubleshooting)
- [Monitoring & Health Checks](#monitoring--health-checks)

---

## ?? Prerequisites

### Required Software

| Software | Version | Purpose |
|----------|---------|---------|
| **PostgreSQL** | 15+ | Primary database |
| **PostGIS** | 3.4+ | Spatial extensions |
| **Python** | 3.14+ | Application runtime |
| **Docker** | 24+ | Containerization (optional) |
| **Docker Compose** | 2+ | Multi-container orchestration (optional) |
| **Neo4j** | 5.15+ | Provenance graph (recommended) |

### Optional Tools

- **Azure CLI** - For Azure deployments
- **psql** - PostgreSQL command-line client
- **git** - Version control

---

## ?? Quick Start (Localhost)

### Option 1: Docker Compose (Recommended)

```bash
# 1. Clone repository
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous

# 2. Copy environment template
cp .env.example .env
# Edit .env with your PostgreSQL password

# 3. Start all services
docker-compose up -d

# 4. View logs
docker-compose logs -f api

# 5. Access API
open http://localhost:8000/docs
```

**Services started:**
- PostgreSQL: `localhost:5432`
- Neo4j: `localhost:7687` (UI: `http://localhost:7474`)
- API: `localhost:8000`

### Option 2: Manual Setup

#### Step 1: Install PostgreSQL + PostGIS

**Windows:**
```powershell
# Install PostgreSQL from https://www.postgresql.org/download/windows/
# Or use winget
winget install PostgreSQL.PostgreSQL

# Install PostGIS
# Download from https://postgis.net/windows_downloads/
```

**macOS:**
```bash
brew install postgresql@15 postgis
brew services start postgresql@15
```

**Linux (Ubuntu/Debian):**
```bash
sudo apt-get update
sudo apt-get install -y postgresql-15 postgresql-15-postgis-3
sudo systemctl start postgresql
```

#### Step 2: Create Database

```bash
# Create database
createdb -U postgres hartonomous

# Create user
psql -U postgres -c "CREATE USER hartonomous WITH PASSWORD 'your-password';"
psql -U postgres -c "GRANT ALL PRIVILEGES ON DATABASE hartonomous TO hartonomous;"
```

#### Step 3: Initialize Schema

**Linux/macOS:**
```bash
# Make script executable
chmod +x scripts/init-database.sh

# Run initialization
./scripts/init-database.sh localhost
```

**Windows (PowerShell):**
```powershell
# Run initialization
.\scripts\Initialize-Database.ps1 -Environment localhost
```

#### Step 4: Install Python Dependencies

```bash
# Create virtual environment
cd api
python -m venv .venv

# Activate virtual environment
# Windows:
.venv\Scripts\activate
# Linux/macOS:
source .venv/bin/activate

# Install dependencies
pip install -r requirements.txt
```

#### Step 5: Configure Environment

Create `.env` file in project root:

```ini
# Database
PGHOST=localhost
PGPORT=5432
PGUSER=hartonomous
PGPASSWORD=your-password
PGDATABASE=hartonomous

# API
API_HOST=0.0.0.0
API_PORT=8000
API_RELOAD=true
LOG_LEVEL=DEBUG

# Neo4j (optional but recommended)
NEO4J_ENABLED=true
NEO4J_URI=bolt://localhost:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=your-neo4j-password

# Authentication (disabled for local)
AUTH_ENABLED=false

# Azure config (disabled for local)
USE_AZURE_CONFIG=false
```

#### Step 6: Start API

```bash
# From project root
uvicorn api.main:app --reload --port 8000

# Or with hot reload
python -m api.main
```

#### Step 7: Verify Deployment

```bash
# Health check
curl http://localhost:8000/v1/health

# Readiness check (validates database)
curl http://localhost:8000/v1/ready

# API documentation
open http://localhost:8000/docs
```

---

## ?? Environment Setup

### Localhost
- **Purpose:** Local development, testing
- **Database:** Local PostgreSQL
- **Auth:** Disabled
- **Logging:** DEBUG

### Development (HART-DESKTOP)
- **Purpose:** CI/CD, feature testing
- **Database:** HART-DESKTOP PostgreSQL (Azure Arc)
- **Auth:** Optional
- **Logging:** DEBUG
- **Deployment:** GitHub Actions ? Azure Arc

### Staging (hart-server)
- **Purpose:** Pre-production validation
- **Database:** hart-server PostgreSQL (Azure Arc)
- **Auth:** Entra ID enabled
- **Logging:** INFO
- **Deployment:** GitHub Actions ? Azure Arc

### Production (Azure)
- **Purpose:** Production workloads
- **Database:** Azure Database for PostgreSQL Flexible Server
- **Auth:** Entra ID + CIAM (external users)
- **Logging:** INFO
- **Deployment:** GitHub Actions ? Azure Container Apps

---

## ?? Deployment Workflows

### Automated Deployment (GitHub Actions)

The CI/CD pipeline automatically deploys based on branch:

```
develop branch  ? Development (HART-DESKTOP)
staging branch  ? Staging (hart-server)
main branch     ? Production (Azure Container Apps)
```

**Workflow steps:**
1. **Validate** - Code quality, linting, security scan
2. **Test** - Unit tests, integration tests
3. **Build** - Docker image build & push to GHCR
4. **Deploy** - Environment-specific deployment
5. **Verify** - Health checks, smoke tests

**Manual trigger:**
```bash
# Trigger workflow manually (requires GitHub CLI)
gh workflow run ci-cd.yml -f environment=staging
```

### Manual Deployment

#### Development/Staging (Azure Arc)

```bash
# 1. SSH to target machine
ssh user@hart-server  # or HART-DESKTOP

# 2. Pull latest image
docker pull ghcr.io/ahartn/hartonomous:staging

# 3. Stop existing container
docker stop hartonomous-api || true
docker rm hartonomous-api || true

# 4. Start new container
docker run -d \
  --name hartonomous-api \
  --network hartonomous-network \
  -p 8000:8000 \
  -e USE_AZURE_CONFIG=true \
  -e KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/ \
  -e APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io \
  ghcr.io/ahartn/hartonomous:staging

# 5. Verify
curl http://localhost:8000/v1/health
```

#### Production (Azure Container Apps)

```bash
# Login to Azure
az login

# Update Container App
az containerapp update \
  --name hartonomous-api \
  --resource-group rg-hartonomous \
  --image ghcr.io/ahartn/hartonomous:main \
  --revision-suffix prod-$(date +%Y%m%d-%H%M%S)

# Verify deployment
az containerapp revision list \
  --name hartonomous-api \
  --resource-group rg-hartonomous \
  --output table
```

---

## ?? Testing

### Run Tests Locally

```bash
# All tests
pytest api/tests/ -v

# Unit tests only
pytest api/tests/ -m unit -v

# Integration tests (requires PostgreSQL)
pytest api/tests/ -m integration -v

# With coverage
pytest api/tests/ --cov=api --cov-report=html
```

### Docker Compose Test Environment

```bash
# Start test environment
docker-compose -f docker-compose.test.yml up -d

# Run tests against containers
pytest api/tests/ -m integration -v

# Cleanup
docker-compose -f docker-compose.test.yml down -v
```

---

## ?? Troubleshooting

### Database Connection Issues

**Symptom:** `psycopg.OperationalError: connection refused`

**Solutions:**
1. Verify PostgreSQL is running:
   ```bash
   # Linux/macOS
   pg_isready -h localhost -p 5432
   
   # Windows
   sc query postgresql-x64-15
   ```

2. Check firewall rules (Windows):
   ```powershell
   New-NetFirewallRule -DisplayName "PostgreSQL" -Direction Inbound -LocalPort 5432 -Protocol TCP -Action Allow
   ```

3. Verify connection settings in `.env`:
   ```ini
   PGHOST=localhost  # Not 127.0.0.1 if using Docker
   PGPORT=5432
   ```

### Schema Initialization Failures

**Symptom:** `ERROR:  relation "atom" does not exist`

**Solutions:**
1. Re-run initialization script:
   ```bash
   ./scripts/init-database.sh localhost
   ```

2. Manual schema load:
   ```bash
   psql -U hartonomous -d hartonomous -f schema/core/tables/001_atom.sql
   ```

3. Check PostgreSQL logs:
   ```bash
   # Linux
   sudo tail -f /var/log/postgresql/postgresql-15-main.log
   
   # macOS (Homebrew)
   tail -f /usr/local/var/log/postgresql@15.log
   
   # Windows
   # Check Event Viewer ? Windows Logs ? Application
   ```

### Docker Compose Issues

**Symptom:** `Error: Cannot connect to the Docker daemon`

**Solutions:**
1. Start Docker Desktop (Windows/macOS)
2. Linux: Start Docker daemon
   ```bash
   sudo systemctl start docker
   ```

**Symptom:** `container name already in use`

**Solutions:**
```bash
# Remove existing containers
docker-compose down

# Force remove
docker rm -f hartonomous-api hartonomous-postgres hartonomous-neo4j
```

### Neo4j Connection Issues

**Symptom:** `ServiceUnavailable: Unable to retrieve routing information`

**Solutions:**
1. Verify Neo4j is running:
   ```bash
   # Docker
   docker logs hartonomous-neo4j
   
   # Neo4j Desktop
   # Check status in Neo4j Desktop UI
   ```

2. Check authentication:
   ```bash
   # Test connection
   cypher-shell -a bolt://localhost:7687 -u neo4j -p your-password
   ```

3. Disable Neo4j temporarily:
   ```ini
   # In .env
   NEO4J_ENABLED=false
   ```

### API Health Check Failures

**Symptom:** `503 Service Unavailable` from `/v1/ready`

**Solutions:**
1. Check database connectivity:
   ```bash
   curl http://localhost:8000/v1/health  # Basic health
   curl http://localhost:8000/v1/ready   # With DB check
   ```

2. View API logs:
   ```bash
   # Docker
   docker logs -f hartonomous-api
   
   # Manual
   # Check console output where uvicorn is running
   ```

3. Verify schema:
   ```bash
   psql -U hartonomous -d hartonomous -c "\dt"  # List tables
   ```

---

## ?? Monitoring & Health Checks

### Health Endpoints

| Endpoint | Purpose | Response |
|----------|---------|----------|
| `/` | Root | API info, links to docs |
| `/v1/health` | Basic liveness | `{"status": "ok"}` |
| `/v1/ready` | Readiness + DB check | `{"status": "ready", "database": {...}}` |
| `/v1/stats` | Database statistics | Atom/relation counts, DB size |
| `/docs` | OpenAPI/Swagger UI | Interactive API documentation |
| `/redoc` | ReDoc UI | Alternative API documentation |

### Example Health Check

```bash
# Basic health
curl http://localhost:8000/v1/health

# Readiness (validates DB connection + schema)
curl http://localhost:8000/v1/ready

# Database statistics
curl http://localhost:8000/v1/stats
```

### Monitoring Stack (Production)

- **Application Insights** - Request tracing, exceptions, custom events
- **Prometheus** - Metrics scraping (`/metrics` endpoint)
- **Grafana** - Visualization dashboards
- **Neo4j Browser** - Graph visualization (http://localhost:7474)

---

## ?? Additional Resources

- [Architecture Documentation](docs/02-ARCHITECTURE.md)
- [API Reference](http://localhost:8000/docs)
- [Azure Configuration](docs/azure/AZURE-CONFIGURATION.md)
- [Troubleshooting Guide](docs/contributing/TROUBLESHOOTING.md)
- [Contributing Guidelines](docs/contributing/CONTRIBUTING.md)

---

## ?? Support

- **Issues:** https://github.com/AHartTN/Hartonomous/issues
- **Discussions:** https://github.com/AHartTN/Hartonomous/discussions
- **Email:** aharttn@gmail.com

---

**Copyright ⌐ 2025 Anthony Hart. All Rights Reserved.**
