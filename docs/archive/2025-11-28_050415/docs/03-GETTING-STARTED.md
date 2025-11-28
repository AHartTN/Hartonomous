# GETTING STARTED

Docker → Running system in 5 minutes.

---

## Prerequisites

- Docker (20.10+)
- 4GB RAM
- 10GB disk

---

## Quick Start

```bash
# 1. Pull image
docker pull hartonomous/postgres:latest

# 2. Run
docker run -d \
  --name hartonomous \
  -p 5432:5432 \
  -e POSTGRES_PASSWORD=yourpassword \
  hartonomous/postgres:latest

# 3. Connect
psql -h localhost -U postgres -d hartonomous

# 4. Verify
hartonomous=# SELECT COUNT(*) FROM atom;
 count
-------
     0
```

---

## First Atomization

```sql
-- Atomize text
SELECT atomize_text('Hello World');

-- Verify atoms created
SELECT atom_id, canonical_text, reference_count
FROM atom
ORDER BY atom_id;

-- Expected: Atoms for 'H', 'e', 'l', 'o', ' ', 'W', 'r', 'd'
```

---

## Spatial Query

```sql
-- Insert test atom with spatial position
INSERT INTO atom (canonical_text, spatial_key, metadata)
VALUES ('cat', ST_MakePoint(0.5, 0.8, 1.2), '{"modality":"concept"}');

-- Find nearest neighbors
SELECT
    canonical_text,
    ST_Distance(spatial_key, ST_MakePoint(0.5, 0.8, 1.2)) AS distance
FROM atom
WHERE spatial_key IS NOT NULL
ORDER BY spatial_key <-> ST_MakePoint(0.5, 0.8, 1.2)
LIMIT 10;
```

---

## Build from Source

```bash
# Clone
git clone https://github.com/YourUsername/Hartonomous.git
cd Hartonomous

# Build Docker image
docker build -t hartonomous/postgres:latest -f docker/Dockerfile .

# Run
docker-compose up -d
```

---

## Docker Compose (recommended)

```yaml
# docker-compose.yml
version: '3.8'

services:
  postgres:
    image: postgis/postgis:15-3.3
    environment:
      POSTGRES_DB: hartonomous
      POSTGRES_USER: postgres
      POSTGRES_PASSWORD: yourpassword
    ports:
      - "5432:5432"
    volumes:
      - postgres_data:/var/lib/postgresql/data
      - ./schema:/docker-entrypoint-initdb.d

volumes:
  postgres_data:
```

```bash
docker-compose up -d
```

---

## Schema Initialization

Place in `schema/001-init.sql`:

```sql
CREATE EXTENSION postgis;
CREATE EXTENSION plpython3u;

-- Create tables (from 02-ARCHITECTURE.md)
\i schema/tables/atom.sql
\i schema/tables/atom_composition.sql
\i schema/tables/atom_relation.sql

-- Create indexes
\i schema/indexes/spatial.sql

-- Create functions
\i schema/functions/atomize.sql
```

---

## Verify Installation

```sql
-- Check extensions
SELECT * FROM pg_extension WHERE extname IN ('postgis', 'plpython3u');

-- Check tables
\dt

-- Check PostGIS version
SELECT PostGIS_Full_Version();

-- Test GPU (optional)
CREATE OR REPLACE FUNCTION test_gpu() RETURNS TEXT AS $$
    import torch
    return "GPU available" if torch.cuda.is_available() else "CPU only"
$$ LANGUAGE plpython3u;

SELECT test_gpu();
```

---

## Configuration

Edit `postgresql.conf`:

```ini
# Memory
shared_buffers = 4GB
effective_cache_size = 12GB
work_mem = 64MB

# Parallelism
max_parallel_workers_per_gather = 4
max_parallel_workers = 8

# WAL
wal_level = replica
max_wal_senders = 3
```

---

## Next Steps

- Read [04-MULTI-MODEL.md](04-MULTI-MODEL.md) to ingest models
- Read [08-INGESTION.md](08-INGESTION.md) for atomization patterns
- Read [10-API-REFERENCE.md](10-API-REFERENCE.md) for function reference

---

## Troubleshooting

**Connection refused**:
```bash
docker logs hartonomous
# Check if PostgreSQL started successfully
```

**PostGIS not found**:
```sql
CREATE EXTENSION postgis;
```

**PL/Python not available**:
```bash
# Install in container
docker exec -it hartonomous bash
apt-get update && apt-get install postgresql-plpython3-15
```

**Out of memory**:
```bash
# Increase Docker RAM allocation
# Docker Desktop → Settings → Resources → Memory → 8GB
```
