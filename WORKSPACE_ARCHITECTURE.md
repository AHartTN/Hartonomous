# Hartonomous Workspace Architecture - Complete Analysis

**Repository:** `AHartTN/Hartonomous` (branch: `develop`)  
**Analysis Date:** 2025-01-26  
**Purpose:** Complete understanding for deployment automation

---

## ??? SYSTEM ARCHITECTURE

### Core Concept
Hartonomous is a **self-organizing intelligence substrate** that treats data like a periodic table of elements:
- **Atomization**: Every piece of data ?64 bytes becomes a content-addressable "atom"
- **Spatial Semantics**: Atoms positioned in 3D semantic space (proximity = similarity)
- **Hierarchical Composition**: Complex structures built from atomic components
- **Relational Graph**: Weighted, typed relationships (is-a, has-a, causes)
- **Full Provenance**: Every change tracked via logical replication ? graph database

### Key Differentiators
1. **Explainable**: Full traceability from raw data to conclusions
2. **Efficient**: Database indexing (B-Trees, R-Trees) instead of GPU matrix ops
3. **Scalable**: Content-addressed deduplication + PostgreSQL foundation
4. **Transparent**: No "black box" - every inference is auditable

---

## ?? TECH STACK

### Backend Stack
```
Language:        Python 3.14
Framework:       FastAPI 0.109+
ASGI Server:     Uvicorn (with uvloop on Linux)
Database:        PostgreSQL 15 + PostGIS 3.4
Graph DB:        Neo4j 5.15+ (RECOMMENDED for provenance)
                 Apache AGE (EXPERIMENTAL - NOT production ready)
Connection:      psycopg3 (async, connection pooling)
Migrations:      Alembic 1.13+
```

### Database Extensions
```
? PostGIS           - Spatial types (GEOMETRY(POINTZ)), R-Tree/GiST indexing
? PL/Python3u       - In-database Python functions (NumPy/SciPy)
? pg_trgm           - Trigram similarity for text search
? btree_gin         - Multi-column GIN indexes
? pgcrypto          - SHA-256 hashing for content addressing
? Apache AGE        - Graph query extension (EXPERIMENTAL)
```

### Python Dependencies (Key Highlights)
```python
# Core API
fastapi>=0.109.0
uvicorn[standard]>=0.27.0
psycopg[binary,pool]>=3.1.0      # PostgreSQL async driver
pydantic>=2.5.0                   # Validation
pydantic-settings>=2.1.0          # Config management

# Database
alembic>=1.13.0                   # Migrations
sqlalchemy>=2.0.0                 # ORM (minimal use)
neo4j>=5.15.0                     # Provenance graph

# Scientific Computing (in-database)
numpy>=1.26.0
scipy>=1.12.0
scikit-learn>=1.4.0
pillow>=10.2.0                    # Image processing

# Azure Integration (Production)
azure-identity>=1.15.0
azure-keyvault-secrets>=4.8.0
azure-appconfiguration>=1.5.0
msal>=1.25.0                      # Entra ID auth
pyjwt[crypto]>=2.8.0
cryptography>=42.0.4

# Observability
prometheus-client>=0.19.0
python-json-logger>=2.0.0

# Testing
pytest>=7.4.0
pytest-asyncio>=0.21.0
pytest-cov>=4.1.0
httpx>=0.26.0                     # Test client
```

---

## ?? PROJECT STRUCTURE

```
Hartonomous/
??? .github/workflows/            # GitHub Actions (EMPTY - needs implementation)
?   ??? deploy.yml                # Deployment workflow (EMPTY)
?
??? api/                          # FastAPI application
?   ??? main.py                   # Application entry point
?   ??? config.py                 # Pydantic settings (env + Azure)
?   ??? dependencies.py           # DI for DB connections, auth
?   ?
?   ??? core/                     # Core utilities
?   ?   ??? database.py           # DB connection utilities
?   ?   ??? azure_config.py       # Azure Key Vault/App Config
?   ?
?   ??? models/                   # Pydantic models (request/response)
?   ?   ??? ingest.py             # Text/Image/Audio ingest models
?   ?   ??? query.py              # Query request/response models
?   ?   ??? training.py           # Training models
?   ?   ??? export.py             # Export models
?   ?
?   ??? routes/                   # API endpoints
?   ?   ??? health.py             # /health, /ready, /stats
?   ?   ??? ingest.py             # /ingest/text, /image, /audio
?   ?   ??? query.py              # /query/semantic, /spatial
?   ?   ??? train.py              # /train/* (model training)
?   ?   ??? export.py             # /export/* (data export)
?   ?
?   ??? services/                 # Business logic
?   ?   ??? atomization.py        # Text/image/audio ? atoms
?   ?   ??? query.py              # Semantic search
?   ?   ??? training.py           # Model training
?   ?   ??? export.py             # Data export
?   ?
?   ??? workers/                  # Background workers
?   ?   ??? neo4j_sync.py         # ? RECOMMENDED: Neo4j provenance sync
?   ?   ??? age_sync.py           # ? EXPERIMENTAL: Apache AGE sync
?   ?
?   ??? tests/                    # Test suite
?   ?   ??? test_sanity.py        # ? Basic sanity tests (PASSING)
?   ?   ??? test_config.py        # Config loading tests
?   ?   ??? integration/          # Integration tests
?   ?       ??? test_api.py       # API endpoint tests
?   ?       ??? test_database.py  # Database tests
?   ?
?   ??? requirements.txt          # Python dependencies
?   ??? .venv/                    # Virtual environment
?
??? schema/                       # PostgreSQL schema (SQL)
?   ??? extensions/               # Extension installation
?   ?   ??? 001_postgis.sql
?   ?   ??? 002_plpython.sql
?   ?   ??? 003_pg_trgm.sql
?   ?   ??? 004_btree_gin.sql
?   ?   ??? 005_pgcrypto.sql
?   ?   ??? 006_age.sql           # ? Apache AGE (experimental)
?   ?
?   ??? core/                     # Core schema (organized structure)
?   ?   ??? tables/               # Table definitions
?   ?   ?   ??? 001_atom.sql
?   ?   ?   ??? 002_atom_composition.sql
?   ?   ?   ??? 003_atom_relation.sql
?   ?   ?   ??? 004_history_tables.sql
?   ?   ?   ??? 005_ooda_tables.sql
?   ?   ?
?   ?   ??? indexes/              # Index definitions
?   ?   ?   ??? core/             # Core indexes
?   ?   ?   ??? spatial/          # Spatial indexes (GiST)
?   ?   ?   ??? relations/        # Relation indexes
?   ?   ?   ??? composition/      # Composition indexes
?   ?   ?
?   ?   ??? functions/            # PL/pgSQL + PL/Python functions
?   ?   ?   ??? atomization/      # atomize_text, atomize_image, etc.
?   ?   ?   ??? spatial/          # Spatial positioning, Hilbert curves
?   ?   ?   ??? relations/        # create_relation, traverse_relations
?   ?   ?   ??? composition/      # Composition management
?   ?   ?   ??? ooda/             # OODA loop (Observe-Orient-Decide-Act)
?   ?   ?   ??? provenance/       # Lineage tracking
?   ?   ?
?   ?   ??? triggers/             # Database triggers
?   ?   ?   ??? 001_temporal_versioning.sql
?   ?   ?   ??? 002_reference_counting.sql
?   ?   ?   ??? 003_provenance_notify.sql
?   ?   ?
?   ?   ??? types/                # Custom types
?   ?       ??? 001_modality_type.sql
?   ?       ??? 002_relation_type.sql
?   ?
?   ??? tables/                   # Legacy schema (flat structure)
?   ??? functions/                # Legacy functions
?   ??? indexes/                  # Legacy indexes
?   ??? triggers/                 # Legacy triggers
?   ??? views/                    # Analytical views
?       ??? v_atom_statistics.sql
?       ??? v_spatial_density.sql
?       ??? v_truth_convergence.sql
?
??? alembic/                      # Database migrations
?   ??? env.py                    # Alembic environment
?   ??? versions/                 # Migration scripts
?       ??? 030ddd58e667_baseline_schema.py
?
??? docker/                       # Docker configuration
?   ??? Dockerfile                # PostgreSQL + PostGIS + PL/Python + FastAPI
?   ??? docker-compose.yml        # Development environment
?
??? docker-compose.yml            # Main docker-compose (root)
?
??? scripts/                      # PowerShell deployment scripts
?   ??? DeploymentHelpers.psm1   # (EMPTY)
?   ??? Initialize-AzureConfig.ps1 # (EMPTY)
?   ??? Quick-Setup-Dev.ps1      # (EMPTY)
?
??? deployment/                   # Deployment files
?   ??? config.yml                # (EMPTY)
?   ??? deploy.ps1                # (EMPTY)
?   ??? deploy.sh                 # (EMPTY)
?
??? docs/                         # Documentation
?   ??? 02-ARCHITECTURE.md
?   ??? azure/
?   ?   ??? AZURE-CONFIGURATION.md  # ? Complete Azure setup
?   ??? architecture/
?   ??? api-reference/
?   ??? business/
?   ??? contributing/
?
??? .env                          # Environment configuration (localhost)
??? pytest.ini                    # Pytest configuration
??? .gitignore                    # Git ignore rules
??? .pylintrc                     # Python linting
??? .yamllint                     # YAML linting
??? .bandit                       # Security scanning
??? README.md                     # Project overview
```

---

## ?? CONFIGURATION MANAGEMENT

### Local Development (.env)
```ini
# Database (Local PostgreSQL)
PGHOST=localhost
PGPORT=5432
PGUSER=hartonomous
PGPASSWORD=Revolutionary-AI-2025!Geometry
PGDATABASE=hartonomous
PGSSLMODE=prefer

# API Server
API_HOST=0.0.0.0
API_PORT=8000
API_RELOAD=true
LOG_LEVEL=DEBUG

# Neo4j (Provenance)
NEO4J_ENABLED=true
NEO4J_URI=bolt://localhost:7687
NEO4J_USER=neo4j
NEO4J_PASSWORD=neo4jneo4j

# Authentication (Disabled for local)
AUTH_ENABLED=false

# Workers
AGE_WORKER_ENABLED=false          # ? Experimental, not production-ready
```

### Production (Azure)
```ini
# Azure Configuration (loads from Key Vault + App Config)
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io

# Authentication
AUTH_ENABLED=true
ENTRA_TENANT_ID=6c9c44c4-f04b-4b5f-bea0-f1069179799c
ENTRA_CLIENT_ID=2e54ba10-1d6e-4a8e-a100-6f84a586e7bf
# ENTRA_CLIENT_SECRET loaded from Key Vault

# Neo4j (Production on hart-server)
NEO4J_ENABLED=true
# NEO4J_PASSWORD loaded from Key Vault
```

---

## ?? AZURE INFRASTRUCTURE

### Resource Group
```
Subscription ID: ed614e1a-7d8b-4608-90c8-66e86c37080b
Resource Group:  rg-hartonomous
Location:        eastus
```

### Key Vault (kv-hartonomous)
**URL:** `https://kv-hartonomous.vault.azure.net/`

**Secrets (14 total):**
- `PostgreSQL-Hartonomous-Password`
- `PostgreSQL-HART-DESKTOP-hartonomous-Password`
- `PostgreSQL-hart-server-postgres-Password`
- `PostgreSQL-hart-server-ai-architect-Password`
- `AzureAd-ClientSecret`
- `EntraExternalId-ClientSecret`
- `Neo4jPassword`
- `ApplicationInsights-ConnectionString`
- `HuggingFace-ApiToken`
- `Stripe-PublishableKey`
- `Stripe-SecretKey`
- `Stripe-WebhookSecret`
- `HART-DESKTOP-Management-Secret`
- `HART-SERVER-Management-Secret`

### App Configuration (appconfig-hartonomous)
**Endpoint:** `https://appconfig-hartonomous.azconfig.io`

**Categories (41 keys):**
- Azure AD settings (tenant, client, scopes)
- Connection strings (PostgreSQL, Neo4j, SQL Server)
- API settings (host, port, CORS, auth)
- Worker settings (AGE, Neo4j)
- Feature flags

### Azure Arc (Hybrid Management)
**HART-DESKTOP (Windows)**
- Managed Identity: `505c61a6-bcd6-4f22-aee5-5c6c0094ae0d`
- Roles: Key Vault Secrets User, App Configuration Data Reader
- Extensions: WindowsAgent.SqlServer

**hart-server (Linux)**
- Managed Identity: `50c98169-43ea-4ee7-9daa-d752ed328994`
- Roles: Key Vault Secrets User, App Configuration Data Reader
- Extensions: AADSSHLogin, LinuxAgent.SqlServer

### Entra ID (Authentication)
**Main Tenant:** `aharttngmail710.onmicrosoft.com`
- Tenant ID: `6c9c44c4-f04b-4b5f-bea0-f1069179799c`
- Apps: Hartonomous API, Admin UI, GitHub Actions (dev/staging/prod)

**CIAM Tenant (External):** `hartonomous.onmicrosoft.com`
- Tenant ID: `1816887e-0796-4065-899f-a7478b3918d7`
- Authority: `https://hartonomous.ciamlogin.com/`
- ? App registration needed for customer portal

---

## ?? CONTAINERIZATION

### Dockerfile Strategy
**Single Dockerfile:** `docker/Dockerfile`
- Base: `postgis/postgis:15-3.4`
- Installs: PostgreSQL 15, PostGIS, PL/Python3, Python packages
- Multi-stage: Database + API in same container (for development)

**Production Consideration:**
- Should split into separate containers:
  - `hartonomous-postgres` (PostgreSQL + PostGIS + extensions)
  - `hartonomous-api` (FastAPI application)
  - `hartonomous-neo4j` (Provenance graph)

### Docker Compose
**Services:**
1. **postgres**
   - Image: `postgres:15-alpine`
   - Ports: `5432:5432`
   - Volumes: `./schema:/docker-entrypoint-initdb.d` (auto-init)
   - Health check: `pg_isready`

2. **api**
   - Build: `docker/Dockerfile`
   - Ports: `8000:8000`
   - Depends on: `postgres` (with health check)
   - Command: `uvicorn api.main:app --reload`

**Networks:**
- `hartonomous-network` (bridge)

**Volumes:**
- `postgres_data` (persistent database)

---

## ??? DATABASE SCHEMA

### Core Tables

#### **atom** (The Periodic Table of Intelligence)
```sql
atom_id           BIGSERIAL PRIMARY KEY
content_hash      BYTEA UNIQUE NOT NULL        -- SHA-256 (deduplication)
atomic_value      BYTEA CHECK (length <= 64)   -- The actual value
canonical_text    TEXT                         -- Cached text representation
spatial_key       GEOMETRY(POINTZ, 0)          -- 3D semantic position
reference_count   BIGINT DEFAULT 1             -- Atomic mass (usage count)
metadata          JSONB DEFAULT '{}'           -- Modality, tenant, model, etc.
created_at        TIMESTAMPTZ DEFAULT now()
valid_from        TIMESTAMPTZ DEFAULT now()
valid_to          TIMESTAMPTZ DEFAULT 'infinity'
```

**Indexes:**
- B-Tree: `content_hash` (unique), `reference_count`, `created_at`
- GiST: `spatial_key` (R-Tree for spatial queries)
- GIN: `metadata` (JSONB queries)

#### **atom_composition** (Hierarchical Structure)
```sql
composition_id    BIGSERIAL PRIMARY KEY
parent_atom_id    BIGINT ? atom(atom_id)       -- Molecule
component_atom_id BIGINT ? atom(atom_id)       -- Constituent atom
position          INTEGER                       -- Order in sequence
composition_type  TEXT                         -- 'text', 'image', 'audio', etc.
metadata          JSONB DEFAULT '{}'
created_at        TIMESTAMPTZ DEFAULT now()
```

**Indexes:**
- B-Tree: `parent_atom_id`, `component_atom_id`, `(parent, position)`
- Unique: `(parent_atom_id, component_atom_id, position)`

#### **atom_relation** (Knowledge Graph)
```sql
relation_id       BIGSERIAL PRIMARY KEY
source_atom_id    BIGINT ? atom(atom_id)       -- Subject
target_atom_id    BIGINT ? atom(atom_id)       -- Object
relation_type     relation_type_enum           -- 'is-a', 'has-a', 'causes', etc.
weight            FLOAT DEFAULT 1.0            -- Synaptic strength
confidence        FLOAT DEFAULT 1.0            -- Belief score
spatial_distance  FLOAT                        -- Distance in semantic space
metadata          JSONB DEFAULT '{}'
created_at        TIMESTAMPTZ DEFAULT now()
last_accessed     TIMESTAMPTZ DEFAULT now()
access_count      BIGINT DEFAULT 0
```

**Indexes:**
- B-Tree: `source_atom_id`, `target_atom_id`, `relation_type`, `weight`
- Composite: `(source, relation_type)`, `(target, relation_type)`

### Custom Types
```sql
CREATE TYPE modality_type AS ENUM (
    'text', 'image', 'audio', 'video', 'sensor', 'numeric', 'vector'
);

CREATE TYPE relation_type AS ENUM (
    'is-a', 'has-a', 'part-of', 'causes', 'enables',
    'requires', 'similar-to', 'opposite-of', 'derived-from'
);
```

### Key SQL Functions
```sql
-- Atomization
atomize_text(text, metadata)                  ? bigint[]
atomize_image_vectorized(pixels)              ? bigint
atomize_audio_sparse(samples)                 ? bigint

-- Spatial
compute_spatial_position(atom_id)             ? geometry
hilbert_index_3d(x, y, z, order)              ? bigint
find_similar_colors_hilbert(rgb, threshold)   ? table

-- Relations
create_relation(source, target, type, weight) ? bigint
traverse_relations(start_atom, max_depth)     ? table
reinforce_synapse(relation_id, delta)         ? void

-- Composition
compose_atoms(parent_id, child_ids[])         ? bigint
decompose_atom(atom_id)                       ? table

-- Provenance
get_atom_lineage(atom_id)                     ? table
trace_inference_reasoning(atom_id)            ? jsonb

-- OODA Loop
ooda_observe(input_data)                      ? observation_id
ooda_orient(observation_id)                   ? hypothesis[]
ooda_decide(hypotheses[])                     ? decision_id
ooda_act(decision_id)                         ? action_result
```

---

## ?? DATA FLOW

### 1. Ingestion (Write Path)
```
User Request (text/image/audio)
  ?
FastAPI Route (/v1/ingest/*)
  ?
AtomizationService
  ?
SQL Function (atomize_*)
  ?
?? Compute content_hash (SHA-256)
?? Check for existing atom (deduplication)
?? Compute spatial_key (3D position)
?? Insert into atom table
?? Create composition hierarchy
?? Trigger provenance notification
  ?
PostgreSQL Logical Replication (WAL)
  ?
Background Worker (Neo4jProvenanceWorker)
  ?
Neo4j Graph (provenance tracking)
```

### 2. Query (Read Path)
```
User Request (semantic search)
  ?
FastAPI Route (/v1/query/semantic)
  ?
QueryService
  ?
SQL Function (spatial search with GiST index)
  ?
?? Find atoms near spatial position
?? Traverse relations (BFS/DFS)
?? Aggregate compositions
?? Apply filters (metadata, confidence)
  ?
Return ranked results
```

---

## ?? TESTING STRATEGY

### Current Test Status
? **Sanity Tests** (`test_sanity.py`) - 12/12 PASSING
- Basic Python operations
- Async functionality
- Module imports
- Config loading
- API version detection

? **Integration Tests** - NOT YET IMPLEMENTED
- Database connection tests
- API endpoint tests
- Atomization tests
- Query tests
- Provenance tests

### Test Environments
1. **Unit Tests** (`-m unit`)
   - Fast, no external dependencies
   - Mock database, services
   - Run in CI on every commit

2. **Integration Tests** (`-m integration`)
   - Require PostgreSQL + Neo4j
   - Docker Compose environment
   - Run in CI before merge

3. **E2E Tests** (`-m e2e`)
   - Full stack deployment
   - Real data ingestion/query
   - Run in staging environment

### Test Coverage Goals
```
Target:          80%+ overall
Critical paths:  95%+
- Atomization
- Spatial positioning
- Relation creation
- Provenance tracking
```

---

## ?? DEPLOYMENT TARGETS

### 1. Localhost (Development)
**Purpose:** Local development, testing, debugging

**Stack:**
- PostgreSQL: localhost:5432
- Neo4j: localhost:7687
- FastAPI: localhost:8000
- Python venv: `api/.venv`

**Setup:**
```bash
# 1. Install PostgreSQL 15 + PostGIS
# 2. Install Neo4j Desktop
# 3. Create Python venv
cd api
python -m venv .venv
.venv\Scripts\activate
pip install -r requirements.txt

# 4. Initialize database
psql -U postgres -c "CREATE DATABASE hartonomous;"
psql -U hartonomous -d hartonomous -f schema/extensions/001_postgis.sql
# ... (run all schema/*.sql files in order)

# 5. Start API
uvicorn api.main:app --reload --port 8000
```

**Validation:**
```bash
# Health check
curl http://localhost:8000/v1/health

# Readiness check
curl http://localhost:8000/v1/ready

# Run tests
pytest api/tests/ -v
```

---

### 2. Development (HART-DESKTOP via Azure Arc)
**Purpose:** Continuous integration, feature testing

**Stack:**
- PostgreSQL: HART-DESKTOP:5432 (Azure Arc-enabled)
- Neo4j: localhost:7687
- FastAPI: HART-DESKTOP:8000
- Managed Identity: Azure Arc

**Configuration:**
```ini
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
AUTH_ENABLED=false           # Development mode
LOG_LEVEL=DEBUG
```

**Deployment Method:**
- GitHub Actions ? Azure Arc ? HART-DESKTOP
- Service Principal: `66a37c0f-5666-450b-b61f-c9e33b56115e`
- Federated credential: `repo:AHartTN/Hartonomous:environment:development`

**Validation:**
- Automated tests in CI
- Health checks
- Smoke tests

---

### 3. Staging (hart-server)
**Purpose:** Pre-production validation, performance testing

**Stack:**
- PostgreSQL: hart-server:5432 (Azure Arc-enabled)
- Neo4j: hart-server:7687 (production instance)
- FastAPI: hart-server:8000
- Managed Identity: Azure Arc
- OS: Linux (Ubuntu/Debian)

**Configuration:**
```ini
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
AUTH_ENABLED=true            # Entra ID enabled
LOG_LEVEL=INFO
NEO4J_ENABLED=true
NEO4J_URI=bolt://hart-server:7687
# NEO4J_PASSWORD from Key Vault
```

**Deployment Method:**
- GitHub Actions ? Azure Arc ? hart-server
- Service Principal: `f05370b1-d09f-4085-bd04-ac028c28b7f8`
- Federated credential: `repo:AHartTN/Hartonomous:environment:staging`

**Validation:**
- Full integration test suite
- Performance benchmarks
- Load testing
- Security scanning

---

### 4. Production (Azure Container Apps / AKS)
**Purpose:** Production workloads, customer-facing

**Stack (Option A: Container Apps)**
- PostgreSQL: Azure Database for PostgreSQL Flexible Server
- Neo4j: Azure VM / Neo4j Aura (managed)
- FastAPI: Azure Container Apps (serverless)
- Auth: Entra ID + CIAM (external users)
- Monitoring: Application Insights

**Stack (Option B: AKS)**
- PostgreSQL: Azure Database for PostgreSQL
- Neo4j: Neo4j Aura / StatefulSet
- FastAPI: AKS (Kubernetes)
- Ingress: NGINX / Application Gateway
- Auth: Entra ID + CIAM

**Configuration:**
```ini
USE_AZURE_CONFIG=true
KEY_VAULT_URL=https://kv-hartonomous.vault.azure.net/
APP_CONFIG_ENDPOINT=https://appconfig-hartonomous.azconfig.io
AUTH_ENABLED=true
ENTRA_TENANT_ID=6c9c44c4-f04b-4b5f-bea0-f1069179799c
CIAM_TENANT_ID=1816887e-0796-4065-899f-a7478b3918d7
LOG_LEVEL=INFO
NEO4J_ENABLED=true
# All secrets from Key Vault via Managed Identity
```

**Deployment Method:**
- GitHub Actions ? Azure Container Registry ? Container Apps/AKS
- Service Principal: `48a904b7-f070-407d-abab-1b71a3c049a9`
- Environment: `production`

**Validation:**
- Smoke tests
- Canary deployment
- Synthetic monitoring
- Real user monitoring (RUM)

---

## ?? AUTHENTICATION & AUTHORIZATION

### Entra ID (Internal Users)
**Tenant:** `aharttngmail710.onmicrosoft.com`
**Use Case:** Internal staff, GitHub Actions, Azure Arc

**Flow:**
```
User/Service ? Entra ID ? Access Token ? FastAPI
                              ?
                     Validate token (JWT)
                              ?
                     Check roles/scopes
                              ?
                     Allow/Deny request
```

**Roles:**
- `Hartonomous.Admin` - Full access
- `Hartonomous.Developer` - Read/Write API
- `Hartonomous.Viewer` - Read-only

### CIAM / External ID (External Users)
**Tenant:** `hartonomous.onmicrosoft.com`
**Use Case:** Customers, partners, public API

**Flow:**
```
Customer ? CIAM Login ? Access Token ? FastAPI
                              ?
                     Validate token (JWT)
                              ?
                     Check subscription tier
                              ?
                     Apply rate limits
                              ?
                     Allow/Deny request
```

**Tiers:**
- `Free` - 100 req/day, basic API
- `Pro` - 10k req/day, advanced features
- `Enterprise` - Unlimited, SLA, support

---

## ?? MONITORING & OBSERVABILITY

### Metrics (Prometheus)
```
# Application Metrics
hartonomous_requests_total{method, endpoint, status}
hartonomous_request_duration_seconds{method, endpoint}
hartonomous_atoms_created_total
hartonomous_atoms_total
hartonomous_relations_total
hartonomous_db_pool_active_connections
hartonomous_db_pool_idle_connections

# System Metrics
process_cpu_seconds_total
process_resident_memory_bytes
```

### Logs (JSON Structured)
```json
{
  "timestamp": "2025-01-26T20:45:00Z",
  "level": "INFO",
  "logger": "api.routes.ingest",
  "message": "Text ingestion complete",
  "atom_count": 1337,
  "processing_time_ms": 42.5,
  "user_id": "auth0|1234567890",
  "request_id": "req_abc123xyz"
}
```

### Traces (Application Insights)
- Request tracing (end-to-end)
- Dependency tracking (PostgreSQL, Neo4j)
- Exception tracking
- Custom events

### Alerts
```yaml
# Critical
- Database connection failures (> 5 in 5min)
- API error rate (> 5% in 15min)
- Response time P95 (> 1s)

# Warning
- Disk usage (> 80%)
- Memory usage (> 90%)
- Connection pool exhaustion

# Info
- Deployment started/completed
- Configuration changes
- Scheduled maintenance
```

---

## ??? CI/CD PIPELINE REQUIREMENTS

### GitHub Actions Workflow Structure

```yaml
name: Deploy Hartonomous

on:
  push:
    branches: [develop, staging, main]
  pull_request:
    branches: [develop, staging, main]
  workflow_dispatch:

env:
  # Registry
  REGISTRY: ghcr.io
  IMAGE_NAME: ${{ github.repository }}
  
  # Azure
  AZURE_SUBSCRIPTION_ID: ed614e1a-7d8b-4608-90c8-66e86c37080b
  AZURE_RESOURCE_GROUP: rg-hartonomous

jobs:
  # ???????????????????????????????????????????????????????????
  # VALIDATE
  # ???????????????????????????????????????????????????????????
  validate:
    name: Validate Code Quality
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: '3.14'
          cache: 'pip'
      
      - name: Install dependencies
        run: |
          pip install -r api/requirements.txt
          pip install pylint bandit yamllint
      
      - name: Lint Python
        run: pylint api/ --rcfile=.pylintrc
      
      - name: Security scan
        run: bandit -r api/ -c .bandit
      
      - name: Lint YAML
        run: yamllint .
      
      - name: Validate SQL syntax
        run: |
          # Use pg_query to validate SQL
          # TODO: Implement SQL validation

  # ???????????????????????????????????????????????????????????
  # TEST
  # ???????????????????????????????????????????????????????????
  test:
    name: Run Tests
    runs-on: ubuntu-latest
    needs: validate
    
    services:
      postgres:
        image: postgis/postgis:15-3.4
        env:
          POSTGRES_USER: hartonomous
          POSTGRES_PASSWORD: test-password
          POSTGRES_DB: hartonomous
        ports:
          - 5432:5432
        options: >-
          --health-cmd pg_isready
          --health-interval 10s
          --health-timeout 5s
          --health-retries 5
      
      neo4j:
        image: neo4j:5.15
        env:
          NEO4J_AUTH: neo4j/test-password
        ports:
          - 7687:7687
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Python
        uses: actions/setup-python@v5
        with:
          python-version: '3.14'
          cache: 'pip'
      
      - name: Install dependencies
        run: pip install -r api/requirements.txt
      
      - name: Initialize database
        run: |
          # Run schema/*.sql files in order
          ./scripts/init-db.sh
        env:
          DATABASE_URL: postgresql://hartonomous:test-password@localhost:5432/hartonomous
      
      - name: Run unit tests
        run: pytest api/tests/ -m unit -v --cov=api --cov-report=xml
      
      - name: Run integration tests
        run: pytest api/tests/ -m integration -v
        env:
          DATABASE_URL: postgresql://hartonomous:test-password@localhost:5432/hartonomous
          NEO4J_URI: bolt://localhost:7687
          NEO4J_PASSWORD: test-password
      
      - name: Upload coverage
        uses: codecov/codecov-action@v4
        with:
          file: ./coverage.xml

  # ???????????????????????????????????????????????????????????
  # BUILD
  # ???????????????????????????????????????????????????????????
  build:
    name: Build Docker Image
    runs-on: ubuntu-latest
    needs: test
    permissions:
      contents: read
      packages: write
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Set up Docker Buildx
        uses: docker/setup-buildx-action@v3
      
      - name: Log in to GitHub Container Registry
        uses: docker/login-action@v3
        with:
          registry: ${{ env.REGISTRY }}
          username: ${{ github.actor }}
          password: ${{ secrets.GITHUB_TOKEN }}
      
      - name: Extract metadata
        id: meta
        uses: docker/metadata-action@v5
        with:
          images: ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}
          tags: |
            type=ref,event=branch
            type=ref,event=pr
            type=semver,pattern={{version}}
            type=semver,pattern={{major}}.{{minor}}
            type=sha,prefix={{branch}}-
      
      - name: Build and push
        uses: docker/build-push-action@v5
        with:
          context: .
          file: ./docker/Dockerfile
          push: true
          tags: ${{ steps.meta.outputs.tags }}
          labels: ${{ steps.meta.outputs.labels }}
          cache-from: type=registry,ref=${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:buildcache
          cache-to: type=registry,ref=${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:buildcache,mode=max

  # ???????????????????????????????????????????????????????????
  # DEPLOY - DEVELOPMENT
  # ???????????????????????????????????????????????????????????
  deploy-dev:
    name: Deploy to Development (HART-DESKTOP)
    runs-on: ubuntu-latest
    needs: build
    if: github.ref == 'refs/heads/develop'
    environment:
      name: development
      url: http://hart-desktop:8000
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Azure Login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID_DEV }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      
      - name: Deploy via Azure Arc
        run: |
          # Use Azure CLI to deploy to Arc-enabled machine
          az connectedmachine extension create \
            --machine-name HART-DESKTOP \
            --resource-group ${{ env.AZURE_RESOURCE_GROUP }} \
            --name HartonomousAPI \
            --type CustomScriptExtension \
            --settings '{"commandToExecute": "./scripts/deploy.sh development"}'
      
      - name: Wait for deployment
        run: sleep 30
      
      - name: Health check
        run: |
          curl -f http://hart-desktop:8000/v1/health || exit 1
          curl -f http://hart-desktop:8000/v1/ready || exit 1
      
      - name: Run smoke tests
        run: pytest api/tests/ -m smoke -v
        env:
          API_BASE_URL: http://hart-desktop:8000

  # ???????????????????????????????????????????????????????????
  # DEPLOY - STAGING
  # ???????????????????????????????????????????????????????????
  deploy-staging:
    name: Deploy to Staging (hart-server)
    runs-on: ubuntu-latest
    needs: build
    if: github.ref == 'refs/heads/staging'
    environment:
      name: staging
      url: https://staging.hartonomous.com
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Azure Login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID_STAGING }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      
      - name: Deploy via Azure Arc
        run: |
          az connectedmachine extension create \
            --machine-name hart-server \
            --resource-group ${{ env.AZURE_RESOURCE_GROUP }} \
            --name HartonomousAPI \
            --type CustomScriptExtension \
            --settings '{"commandToExecute": "./scripts/deploy.sh staging"}'
      
      - name: Wait for deployment
        run: sleep 30
      
      - name: Health check
        run: |
          curl -f https://staging.hartonomous.com/v1/health || exit 1
          curl -f https://staging.hartonomous.com/v1/ready || exit 1
      
      - name: Run integration tests
        run: pytest api/tests/ -m integration -v
        env:
          API_BASE_URL: https://staging.hartonomous.com
      
      - name: Run performance tests
        run: |
          # Use k6 or locust for load testing
          # TODO: Implement performance tests

  # ???????????????????????????????????????????????????????????
  # DEPLOY - PRODUCTION
  # ???????????????????????????????????????????????????????????
  deploy-prod:
    name: Deploy to Production (Azure Container Apps)
    runs-on: ubuntu-latest
    needs: [build, deploy-staging]
    if: github.ref == 'refs/heads/main'
    environment:
      name: production
      url: https://api.hartonomous.com
    
    steps:
      - uses: actions/checkout@v4
      
      - name: Azure Login (OIDC)
        uses: azure/login@v2
        with:
          client-id: ${{ secrets.AZURE_CLIENT_ID_PROD }}
          tenant-id: ${{ secrets.AZURE_TENANT_ID }}
          subscription-id: ${{ secrets.AZURE_SUBSCRIPTION_ID }}
      
      - name: Deploy to Container Apps
        run: |
          az containerapp update \
            --name hartonomous-api \
            --resource-group ${{ env.AZURE_RESOURCE_GROUP }} \
            --image ${{ env.REGISTRY }}/${{ env.IMAGE_NAME }}:${{ github.sha }} \
            --revision-suffix ${{ github.sha }}
      
      - name: Wait for deployment
        run: sleep 60
      
      - name: Health check
        run: |
          curl -f https://api.hartonomous.com/v1/health || exit 1
          curl -f https://api.hartonomous.com/v1/ready || exit 1
      
      - name: Run smoke tests
        run: pytest api/tests/ -m smoke -v
        env:
          API_BASE_URL: https://api.hartonomous.com
      
      - name: Notify deployment
        uses: 8398a7/action-slack@v3
        with:
          status: ${{ job.status }}
          text: 'Hartonomous deployed to production'
          webhook_url: ${{ secrets.SLACK_WEBHOOK }}
```

---

## ?? DEPLOYMENT CHECKLIST

### Prerequisites
- [ ] PostgreSQL 15+ with PostGIS
- [ ] Neo4j 5.15+ (production provenance)
- [ ] Python 3.14+
- [ ] Docker 24+ (optional, for containerized deployment)
- [ ] Azure CLI (for Azure deployments)
- [ ] GitHub CLI (for repository setup)

### Infrastructure (Azure)
- [ ] Resource Group created (`rg-hartonomous`)
- [ ] Key Vault created (`kv-hartonomous`)
- [ ] App Configuration created (`appconfig-hartonomous`)
- [ ] Managed Identity configured for Arc machines
- [ ] RBAC roles assigned (Key Vault Secrets User, App Config Data Reader)
- [ ] Service Principals created for GitHub Actions (dev/staging/prod)
- [ ] Federated credentials configured (OIDC)

### Database Setup
- [ ] PostgreSQL installed and running
- [ ] Database created (`hartonomous`)
- [ ] User created with appropriate permissions
- [ ] PostGIS extension installed
- [ ] PL/Python3u extension installed
- [ ] Schema initialized (tables, indexes, functions, triggers)
- [ ] Test data loaded (optional)

### Application Setup
- [ ] Python virtual environment created
- [ ] Dependencies installed (`pip install -r requirements.txt`)
- [ ] Environment variables configured (`.env` or Azure)
- [ ] Database connection tested
- [ ] Neo4j connection tested (if enabled)
- [ ] Health endpoint accessible

### Testing
- [ ] Unit tests passing (`pytest -m unit`)
- [ ] Integration tests passing (`pytest -m integration`)
- [ ] Smoke tests passing (`pytest -m smoke`)
- [ ] Performance benchmarks recorded
- [ ] Security scan passed (bandit, safety)

### CI/CD Setup
- [ ] GitHub repository configured
- [ ] GitHub Actions workflow created
- [ ] Secrets configured (Azure credentials, API keys)
- [ ] Environments created (development, staging, production)
- [ ] Branch protection rules configured
- [ ] Code owners defined

### Monitoring & Alerting
- [ ] Application Insights configured
- [ ] Prometheus metrics enabled
- [ ] Log aggregation configured
- [ ] Alerts configured (critical, warning, info)
- [ ] Dashboard created (Grafana / Azure Portal)

### Security
- [ ] Secrets in Key Vault (not in code)
- [ ] Managed Identity used (no API keys in code)
- [ ] HTTPS enforced (production)
- [ ] Entra ID authentication enabled (production)
- [ ] Rate limiting configured
- [ ] CORS configured correctly
- [ ] SQL injection prevention verified
- [ ] Dependency vulnerabilities checked

### Documentation
- [ ] README updated
- [ ] API documentation generated (OpenAPI/Swagger)
- [ ] Deployment runbook created
- [ ] Troubleshooting guide created
- [ ] Architecture diagrams updated
- [ ] Changelog maintained

---

## ?? KNOWN ISSUES & WARNINGS

### 1. Apache AGE (EXPERIMENTAL)
**Status:** ?? NOT PRODUCTION READY

**Why:**
- AGE development team dismissed Oct 2024
- No active maintenance or bug fixes
- Compatibility issues with PostgreSQL 15+
- Neo4j recommended for production provenance

**Action:**
- Use `NEO4J_ENABLED=true` instead
- Set `AGE_WORKER_ENABLED=false`
- Remove AGE from production deployments

### 2. Empty Deployment Files
**Files:**
- `.github/workflows/deploy.yml` (EMPTY)
- `scripts/*.ps1` (EMPTY)
- `deployment/*.sh` (EMPTY)

**Impact:**
- No automated deployment currently
- Manual deployment required
- Risk of configuration drift

**Action:**
- Implement GitHub Actions workflow (see template above)
- Create deployment scripts
- Test on all environments

### 3. Missing alembic.ini
**Status:** ?? Alembic configuration missing

**Impact:**
- Cannot run migrations
- Manual schema management required

**Action:**
- Create `alembic.ini` with proper configuration
- Generate initial migration from schema
- Test migration on development environment

### 4. CIAM App Registration
**Status:** ?? Not yet created

**Impact:**
- External users cannot authenticate
- Customer portal not functional

**Action:**
- Create app registration in CIAM tenant
- Configure redirect URIs
- Store client secret in Key Vault
- Update App Configuration

### 5. Test Coverage
**Current:** ~20% (sanity tests only)
**Target:** 80%+

**Missing:**
- Integration tests (database, API)
- Atomization tests
- Query tests
- Provenance tests
- Performance tests

**Action:**
- Implement integration test suite
- Set up test database (Docker Compose)
- Add to CI pipeline

---

## ?? NEXT STEPS (PRIORITIZED)

### Phase 1: Foundation (Week 1)
1. **Create GitHub Actions Workflow**
   - Validate, test, build, deploy
   - Environment-specific configurations
   - Health checks and smoke tests

2. **Implement Deployment Scripts**
   - `scripts/deploy.sh` (Linux/Mac)
   - `scripts/deploy.ps1` (Windows/PowerShell)
   - Database initialization script

3. **Configure Alembic**
   - Create `alembic.ini`
   - Generate baseline migration
   - Test on development environment

4. **Complete Test Suite**
   - Integration tests (database, API)
   - Atomization tests
   - Query tests
   - Add to CI pipeline

### Phase 2: Infrastructure (Week 2)
5. **Azure Infrastructure as Code**
   - Bicep templates for all resources
   - Modular, reusable components
   - Environment-specific parameters

6. **Neo4j Production Setup**
   - Deploy Neo4j on hart-server or Aura
   - Configure provenance sync worker
   - Test end-to-end provenance tracking

7. **Monitoring & Alerting**
   - Application Insights integration
   - Prometheus metrics
   - Grafana dashboards
   - Alert rules

8. **CIAM Configuration**
   - Create app registration
   - Configure external user flows
   - Test authentication

### Phase 3: Production Readiness (Week 3)
9. **Performance Optimization**
   - Database tuning (indexes, vacuuming)
   - Connection pool optimization
   - Caching strategy
   - Load testing

10. **Security Hardening**
    - Penetration testing
    - Dependency updates
    - Secret rotation
    - Access reviews

11. **Documentation**
    - API reference (OpenAPI)
    - Deployment runbook
    - Troubleshooting guide
    - Architecture diagrams

12. **Disaster Recovery**
    - Backup strategy
    - Recovery procedures
    - Failover testing
    - RTO/RPO definition

---

## ?? SUCCESS CRITERIA

### Deployment Automation
- [ ] One-click deployment to localhost
- [ ] Automated CI/CD for all environments
- [ ] Zero-downtime production deployments
- [ ] Rollback capability within 5 minutes

### Testing & Quality
- [ ] 80%+ code coverage
- [ ] All integration tests passing
- [ ] Performance benchmarks met
- [ ] Security scan passes with no critical issues

### Observability
- [ ] Health checks passing in all environments
- [ ] Metrics collected and visualized
- [ ] Logs aggregated and searchable
- [ ] Alerts configured and tested

### Documentation
- [ ] README complete and accurate
- [ ] API documentation generated
- [ ] Deployment runbook tested
- [ ] Architecture diagrams current

---

**Analysis Complete!**  
This document provides a complete understanding of the Hartonomous workspace for implementing robust, idempotent, environment-agnostic deployment automation.

**Next Action:** Implement GitHub Actions workflow and deployment scripts based on this analysis.

---

**Copyright © 2025 Anthony Hart. All Rights Reserved.**
