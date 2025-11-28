# Installation Guide

**Complete installation and configuration for Hartonomous.**

---

## Prerequisites

### Required Software

- **Docker** 24.0 or later
- **Docker Compose** 2.20 or later
- **Git** 2.40 or later
- **8GB RAM** minimum (16GB recommended for production)
- **10GB disk space** for initial setup
- **20GB+** for production workloads

### Operating Systems

Tested on:
- **Linux**: Ubuntu 22.04+, Debian 12+, RHEL 9+
- **macOS**: macOS 13+ (Ventura or later)
- **Windows**: Windows 11 with WSL2

---

## Installation Steps

### 1. Install Docker

#### Linux (Ubuntu/Debian)
```bash
# Update package index
sudo apt-get update

# Install dependencies
sudo apt-get install -y ca-certificates curl gnupg

# Add Docker's official GPG key
sudo install -m 0755 -d /etc/apt/keyrings
curl -fsSL https://download.docker.com/linux/ubuntu/gpg | sudo gpg --dearmor -o /etc/apt/keyrings/docker.gpg
sudo chmod a+r /etc/apt/keyrings/docker.gpg

# Add repository
echo \
  "deb [arch=$(dpkg --print-architecture) signed-by=/etc/apt/keyrings/docker.gpg] https://download.docker.com/linux/ubuntu \
  $(. /etc/os-release && echo "$VERSION_CODENAME") stable" | \
  sudo tee /etc/apt/sources.list.d/docker.list > /dev/null

# Install Docker
sudo apt-get update
sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin

# Add user to docker group
sudo usermod -aG docker $USER
newgrp docker

# Verify installation
docker --version
docker compose version
```

#### macOS
```bash
# Install via Homebrew
brew install --cask docker

# Start Docker Desktop
open -a Docker

# Verify installation
docker --version
docker compose version
```

#### Windows (WSL2)
```powershell
# Install WSL2
wsl --install

# Download and install Docker Desktop for Windows
# https://www.docker.com/products/docker-desktop

# Enable WSL2 integration in Docker Desktop settings

# Verify installation (in WSL2 terminal)
docker --version
docker compose version
```

---

### 2. Clone Repository

```bash
# Clone repository
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous

# Verify structure
ls -la
# Expected: docker-compose.yml, schema/, src/, api/, docs/
```

---

### 3. Configure Environment

#### Create Environment File

```bash
# Copy example environment file
cp .env.example .env

# Edit configuration
nano .env  # or use your preferred editor
```

#### Environment Variables

**`.env` file contents:**

```bash
# ============================================================================
# PostgreSQL Configuration
# ============================================================================
PGUSER=hartonomous
PGPASSWORD=Revolutionary-AI-2025!Geometry
PGDATABASE=hartonomous
PGPORT=5432

# Connection string (auto-generated from above)
DATABASE_URL=postgresql://${PGUSER}:${PGPASSWORD}@postgres:5432/${PGDATABASE}

# Connection Pool
POOL_MIN_SIZE=5
POOL_MAX_SIZE=20
POOL_TIMEOUT=30
POOL_MAX_IDLE=600

# ============================================================================
# Neo4j Configuration
# ============================================================================
NEO4J_ENABLED=true
NEO4J_URI=bolt://neo4j:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=neo4jneo4j
NEO4J_DATABASE=neo4j

# Neo4j HTTP/UI ports
NEO4J_BOLT_PORT=7687
NEO4J_HTTP_PORT=7474

# ============================================================================
# API Configuration
# ============================================================================
API_HOST=0.0.0.0
API_PORT=8000
API_RELOAD=true
LOG_LEVEL=INFO

# API prefix
API_V1_PREFIX=/v1

# CORS origins (comma-separated)
CORS_ORIGINS=http://localhost,http://localhost:3000,http://localhost:8000

# ============================================================================
# Code Atomizer (C# Microservice)
# ============================================================================
CODE_ATOMIZER_URL=http://code-atomizer:8080

# ============================================================================
# Workers
# ============================================================================
# Neo4j provenance worker (RECOMMENDED)
NEO4J_WORKER_ENABLED=true
NEO4J_WORKER_BATCH_SIZE=100
NEO4J_WORKER_INTERVAL=5

# AGE worker (EXPERIMENTAL - not recommended)
AGE_WORKER_ENABLED=false

# ============================================================================
# Security
# ============================================================================
# Authentication (disabled for local dev)
AUTH_ENABLED=false
API_KEY_HEADER=X-API-Key

# Azure configuration (disabled for local)
USE_AZURE_CONFIG=false

# ============================================================================
# Monitoring
# ============================================================================
ENABLE_METRICS=false
METRICS_PORT=9090
```

#### Security Considerations

**?? IMPORTANT: Change default passwords for production!**

```bash
# Generate secure passwords
openssl rand -base64 32  # For PGPASSWORD
openssl rand -base64 32  # For NEO4J_PASSWORD
```

---

### 4. Initialize Database Schema

The database schema is automatically initialized on first startup via `docker-entrypoint-initdb.d/init-db.sh`.

**Verify schema files exist:**

```bash
ls schema/
# Expected:
# - 000_init.sh
# - core/tables/
# - core/functions/
# - extensions/
# - indexes/
```

**Manual initialization (if needed):**

```bash
# Start only PostgreSQL
docker-compose up -d postgres

# Wait for PostgreSQL to be ready
docker-compose exec postgres pg_isready -U hartonomous

# Run schema manually
docker-compose exec -T postgres psql -U hartonomous -d hartonomous < schema/000_init.sh

# Verify tables exist
docker-compose exec postgres psql -U hartonomous -d hartonomous -c "\dt"
# Expected: atom, atom_composition, atom_relation
```

---

### 5. Start Services

#### Start All Services

```bash
# Start all services in detached mode
docker-compose up -d

# View startup logs
docker-compose logs -f

# Wait for "? Hartonomous API ready" message
```

#### Start Services Individually

```bash
# Start PostgreSQL only
docker-compose up -d postgres

# Start Neo4j only
docker-compose up -d neo4j

# Start API (requires postgres and neo4j)
docker-compose up -d api

# Start Code Atomizer
docker-compose up -d code-atomizer

# Start Caddy (reverse proxy)
docker-compose up -d caddy
```

---

### 6. Verify Installation

#### Check Service Status

```bash
# View running containers
docker-compose ps

# Expected output:
# NAME                      STATUS    PORTS
# hartonomous-postgres      Up        0.0.0.0:5432->5432/tcp
# hartonomous-neo4j         Up        0.0.0.0:7474->7474/tcp, 0.0.0.0:7687->7687/tcp
# hartonomous-api           Up        0.0.0.0:8000->8000/tcp
# hartonomous-code-atomizer Up        0.0.0.0:8080->8080/tcp
# hartonomous-caddy         Up        0.0.0.0:80->80/tcp, 0.0.0.0:443->443/tcp
```

#### Health Checks

**API Health:**
```bash
curl http://localhost/v1/health
```

**Expected response:**
```json
{
  "status": "healthy",
  "version": "0.6.0",
  "database": "connected",
  "neo4j": "connected",
  "code_atomizer": "connected",
  "timestamp": "2025-11-28T12:00:00Z"
}
```

**PostgreSQL:**
```bash
docker-compose exec postgres pg_isready -U hartonomous
# Expected: postgres:5432 - accepting connections
```

**Neo4j:**
```bash
curl http://localhost:7474
# Expected: HTTP 200 with Neo4j browser HTML
```

**Code Atomizer:**
```bash
curl http://localhost:8080/health
# Expected: {"status":"healthy"}
```

#### Verify Database Schema

```bash
# Connect to PostgreSQL
docker-compose exec postgres psql -U hartonomous -d hartonomous

# List tables
\dt

# Expected tables:
# atom
# atom_composition
# atom_relation
# (plus history/audit tables)

# Verify PostGIS
SELECT PostGIS_version();

# Verify extensions
\dx

# Expected extensions:
# - postgis
# - pg_trgm
# - btree_gin
# - pgcrypto
# - plpython3u

# Exit
\q
```

#### Verify Neo4j

```bash
# Open Neo4j Browser
open http://localhost:7474  # macOS
xdg-open http://localhost:7474  # Linux
start http://localhost:7474  # Windows

# Login credentials (from .env):
# Username: neo4j
# Password: neo4jneo4j

# Run test query
MATCH (n) RETURN count(n);
# Expected: 0 (no nodes yet)
```

---

## Configuration Details

### PostgreSQL Configuration

**Memory Settings** (in `docker-compose.yml`):

```yaml
command: >
  postgres
  -c shared_buffers=256MB           # RAM for caching
  -c effective_cache_size=1GB       # OS cache estimate
  -c work_mem=64MB                  # Per-operation memory
  -c maintenance_work_mem=256MB     # Index/vacuum memory
  -c max_parallel_workers_per_gather=2
  -c random_page_cost=1.1           # SSD optimization
  -c wal_level=logical              # For logical replication
  -c max_wal_senders=10
  -c max_replication_slots=10
```

**For production**, increase based on available RAM:
- **16GB RAM**: `shared_buffers=4GB`, `effective_cache_size=12GB`
- **32GB RAM**: `shared_buffers=8GB`, `effective_cache_size=24GB`
- **64GB RAM**: `shared_buffers=16GB`, `effective_cache_size=48GB`

### Neo4j Configuration

**Memory Settings** (in `docker-compose.yml`):

```yaml
environment:
  NEO4J_server_memory_heap_initial__size: 512m
  NEO4J_server_memory_heap_max__size: 1G
  NEO4J_server_memory_pagecache_size: 512m
```

**For production**, increase based on graph size:
- **Small** (<1M nodes): 1G heap, 512MB pagecache
- **Medium** (<10M nodes): 4G heap, 2GB pagecache
- **Large** (>10M nodes): 16G heap, 8GB pagecache

---

## Port Configuration

### Default Ports

| Service | Port | Protocol | Purpose |
|---------|------|----------|---------|
| **Caddy** | 80 | HTTP | Reverse proxy |
| **Caddy** | 443 | HTTPS | Reverse proxy (TLS) |
| **API** | 8000 | HTTP | FastAPI (internal) |
| **PostgreSQL** | 5432 | TCP | Database |
| **Neo4j Bolt** | 7687 | Bolt | Graph queries |
| **Neo4j HTTP** | 7474 | HTTP | Browser UI |
| **Code Atomizer** | 8080 | HTTP | C# microservice |

### Change Ports (if conflicts)

**Edit `docker-compose.yml`:**

```yaml
services:
  postgres:
    ports:
      - "5433:5432"  # Change 5433 to your desired port

  neo4j:
    ports:
      - "7475:7474"  # HTTP
      - "7688:7687"  # Bolt
```

**Update `.env`:**
```bash
PGPORT=5433
NEO4J_BOLT_PORT=7688
NEO4J_HTTP_PORT=7475
```

---

## Volume Management

### Data Persistence

Docker volumes store persistent data:

```bash
# List volumes
docker volume ls | grep hartonomous

# Expected volumes:
# hartonomous_postgres_data   # PostgreSQL data
# hartonomous_neo4j_data      # Neo4j data
# hartonomous_neo4j_logs      # Neo4j logs
# hartonomous_neo4j_plugins   # Neo4j plugins
```

### Backup Volumes

```bash
# Backup PostgreSQL
docker-compose exec postgres pg_dump -U hartonomous -d hartonomous > backup-$(date +%Y%m%d).sql

# Backup Neo4j
docker-compose exec neo4j neo4j-admin database dump neo4j --to-path=/backups
docker cp hartonomous-neo4j:/backups/neo4j.dump ./neo4j-backup-$(date +%Y%m%d).dump
```

### Restore from Backup

```bash
# Restore PostgreSQL
docker-compose exec -T postgres psql -U hartonomous -d hartonomous < backup-20251128.sql

# Restore Neo4j
docker cp neo4j-backup-20251128.dump hartonomous-neo4j:/backups/
docker-compose exec neo4j neo4j-admin database load neo4j --from-path=/backups
```

### Reset All Data (?? DESTROYS EVERYTHING)

```bash
# Stop services
docker-compose down

# Remove volumes
docker-compose down -v

# Restart (fresh database)
docker-compose up -d
```

---

## Network Configuration

### Internal Network

Services communicate via Docker internal network `hartonomous-network`:

```yaml
networks:
  hartonomous-network:
    driver: bridge
```

### DNS Resolution

Services use container names as hostnames:
- `postgres` ? PostgreSQL
- `neo4j` ? Neo4j
- `api` ? FastAPI
- `code-atomizer` ? C# microservice

**Example connection string from API:**
```
postgresql://hartonomous:password@postgres:5432/hartonomous
```

---

## Troubleshooting

### Services Won't Start

**Check logs:**
```bash
docker-compose logs postgres
docker-compose logs neo4j
docker-compose logs api
```

**Common issues:**

1. **Port conflicts**
   ```bash
   # Check what's using port 5432
   lsof -i :5432  # macOS/Linux
   netstat -ano | findstr :5432  # Windows
   
   # Solution: Change port in docker-compose.yml
   ```

2. **Insufficient memory**
   ```bash
   # Check Docker memory allocation
   docker info | grep Memory
   
   # Solution: Increase Docker memory in settings (8GB minimum)
   ```

3. **Volume permission issues**
   ```bash
   # Fix permissions
   sudo chown -R $(id -u):$(id -g) ./data
   ```

### Database Connection Fails

```bash
# Test connection from host
docker-compose exec postgres psql -U hartonomous -d hartonomous -c "SELECT version();"

# Test connection from API
docker-compose exec api python -c "
import asyncio
import asyncpg
async def test():
    conn = await asyncpg.connect('postgresql://hartonomous:password@postgres:5432/hartonomous')
    print(await conn.fetchval('SELECT 1'))
    await conn.close()
asyncio.run(test())
"
```

### Neo4j Connection Fails

```bash
# Check Neo4j status
docker-compose exec neo4j cypher-shell -u neo4j -p neo4jneo4j "RETURN 1;"

# Check logs
docker-compose logs neo4j | grep ERROR
```

### API Health Check Fails

```bash
# Check API logs
docker-compose logs api | tail -50

# Restart API
docker-compose restart api

# Check from inside container
docker-compose exec api curl http://localhost:8000/v1/health
```

### Schema Not Initialized

```bash
# Manually initialize
docker-compose exec -T postgres psql -U hartonomous -d hartonomous < schema/000_init.sh

# Verify
docker-compose exec postgres psql -U hartonomous -d hartonomous -c "\dt"
```

---

## Performance Tuning

### PostgreSQL Optimization

**For production workloads**, tune PostgreSQL:

```sql
-- Connect to database
docker-compose exec postgres psql -U hartonomous -d hartonomous

-- Recommended settings (adjust based on hardware)
ALTER SYSTEM SET shared_buffers = '4GB';
ALTER SYSTEM SET effective_cache_size = '12GB';
ALTER SYSTEM SET work_mem = '128MB';
ALTER SYSTEM SET maintenance_work_mem = '1GB';
ALTER SYSTEM SET max_parallel_workers_per_gather = 4;
ALTER SYSTEM SET random_page_cost = 1.1;

-- Reload configuration
SELECT pg_reload_conf();
```

### Neo4j Optimization

**Edit `docker-compose.yml`:**

```yaml
neo4j:
  environment:
    # Increase heap for large graphs
    NEO4J_server_memory_heap_initial__size: 4G
    NEO4J_server_memory_heap_max__size: 8G
    NEO4J_server_memory_pagecache_size: 4G
    
    # Performance tuning
    NEO4J_dbms_memory_transaction_total_max: 1G
    NEO4J_db_checkpoint_interval_time: 15m
```

---

## Next Steps

? Installation complete!

Continue with:
1. **[Quick Start](quick-start.md)** — Ingest your first document
2. **[First Ingestion](first-ingestion.md)** — Deep dive tutorial
3. **[API Reference](../api-reference/ingestion.md)** — Full endpoint documentation

---

## Appendix: Manual Installation (No Docker)

### Install PostgreSQL 16 + PostGIS

```bash
# Ubuntu/Debian
sudo apt-get install -y postgresql-16 postgresql-16-postgis-3 postgresql-plpython3-16

# Create database
sudo -u postgres createuser -s hartonomous
sudo -u postgres createdb -O hartonomous hartonomous

# Enable extensions
sudo -u postgres psql -d hartonomous -c "CREATE EXTENSION postgis;"
sudo -u postgres psql -d hartonomous -c "CREATE EXTENSION pg_trgm;"
sudo -u postgres psql -d hartonomous -c "CREATE EXTENSION btree_gin;"
sudo -u postgres psql -d hartonomous -c "CREATE EXTENSION pgcrypto;"
sudo -u postgres psql -d hartonomous -c "CREATE EXTENSION plpython3u;"

# Initialize schema
psql -U hartonomous -d hartonomous < schema/000_init.sh
```

### Install Neo4j 5.15

```bash
# Ubuntu/Debian
wget -O - https://debian.neo4j.com/neotechnology.gpg.key | sudo apt-key add -
echo 'deb https://debian.neo4j.com stable latest' | sudo tee /etc/apt/sources.list.d/neo4j.list
sudo apt-get update
sudo apt-get install -y neo4j=1:5.15.0

# Start Neo4j
sudo systemctl start neo4j
sudo systemctl enable neo4j

# Set password
cypher-shell -u neo4j -p neo4j "ALTER USER neo4j SET PASSWORD 'neo4jneo4j';"
```

### Install Python API

```bash
# Install Python 3.14+
sudo apt-get install -y python3.14 python3.14-venv python3-pip

# Create virtual environment
python3.14 -m venv venv
source venv/bin/activate

# Install dependencies
pip install -r requirements.txt

# Set environment variables
export DATABASE_URL="postgresql://hartonomous:password@localhost:5432/hartonomous"
export NEO4J_URI="bolt://localhost:7687"
export NEO4J_USER="neo4j"
export NEO4J_PASSWORD="neo4jneo4j"

# Start API
uvicorn api.main:app --host 0.0.0.0 --port 8000
```

### Install Code Atomizer (.NET)

```bash
# Install .NET 8.0 SDK
wget https://dot.net/v1/dotnet-install.sh
chmod +x dotnet-install.sh
./dotnet-install.sh --channel 8.0

# Build code atomizer
cd src/Hartonomous.CodeAtomizer.Api
dotnet publish -c Release -o ./publish

# Run
cd publish
./Hartonomous.CodeAtomizer.Api
```

---

**Last Updated:** 2025-11-28  
**Version:** Hartonomous v0.6.0
