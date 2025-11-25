# Hartonomous Quick Setup Guide

This guide will get you from zero to running Hartonomous in 5 minutes.

---

## Prerequisites

Before starting, ensure you have:

- **Docker** (20.10 or higher)
- **Docker Compose** (v2.0 or higher)
- **4GB RAM** minimum (8GB recommended)
- **10GB disk space** minimum

### Check Prerequisites

```bash
# Check Docker version
docker --version
# Should show: Docker version 20.10.x or higher

# Check Docker Compose version
docker-compose --version
# Should show: Docker Compose version v2.x.x or higher

# Check available memory
docker info | grep Memory
# Should show at least 4GB
```

---

## Installation Steps

### 1. Clone the Repository

```bash
git clone https://github.com/AHartTN/Hartonomous.git
cd Hartonomous
```

### 2. Start Hartonomous

```bash
cd docker
docker-compose up -d
```

**What this does:**
- Downloads PostgreSQL + PostGIS image
- Builds Hartonomous container
- Initializes database with schema
- Starts PgBouncer for connection pooling

### 3. Wait for Initialization

```bash
# Watch logs for "database system is ready"
docker-compose logs -f hartonomous-postgres

# Press Ctrl+C when you see:
# "database system is ready to accept connections"
```

This usually takes 30-60 seconds.

### 4. Verify Installation

```bash
# Go back to project root
cd ..

# Run verification script
bash verify-setup.sh
```

**Expected output:**
```
? All tests passed!
```

---

## Quick Test

### Connect to Database

```bash
psql -h localhost -U postgres -d hartonomous
```

**Default password:** `changeme` (change in production!)

### Run Examples

```sql
-- Load quick start examples
\i /docker-entrypoint-initdb.d/999_examples.sql

-- Or run individual commands:

-- Atomize text
SELECT atomize_text('Hello Hartonomous');

-- View atoms
SELECT * FROM atom LIMIT 10;

-- Run OODA cycle
SELECT * FROM run_ooda_cycle();
```

---

## Troubleshooting

### Docker Container Not Starting

**Problem:** `docker-compose up -d` fails

**Solutions:**
```bash
# Check logs
docker-compose logs hartonomous-postgres

# Common issues:
# - Port 5432 already in use
docker ps | grep 5432  # Check what's using the port
sudo systemctl stop postgresql  # Stop local PostgreSQL if running

# - Out of memory
# Increase Docker memory allocation in Docker Desktop settings
```

### Cannot Connect to Database

**Problem:** `psql: connection refused`

**Solutions:**
```bash
# Check container is running
docker ps | grep hartonomous

# Check health status
docker inspect hartonomous | grep Health -A 10

# Wait longer for initialization
docker-compose logs -f hartonomous-postgres
# Wait for "database system is ready"

# Check port mapping
docker ps | grep 5432
# Should show: 0.0.0.0:5432->5432/tcp
```

### Extension Not Loaded

**Problem:** `ERROR: extension "postgis" is not available`

**Solutions:**
```bash
# Restart container
docker-compose restart hartonomous-postgres

# Check extension status
psql -h localhost -U postgres -d hartonomous -c "SELECT * FROM pg_extension;"

# Manually install (if needed)
psql -h localhost -U postgres -d hartonomous -c "CREATE EXTENSION IF NOT EXISTS postgis;"
```

### Schema Not Initialized

**Problem:** `ERROR: relation "atom" does not exist`

**Solutions:**
```bash
# Check if initialization ran
docker-compose logs hartonomous-postgres | grep "Hartonomous initialization"

# Manually run initialization
docker exec -it hartonomous bash
cd /docker-entrypoint-initdb.d
bash 000_init.sh
exit

# Or rebuild from scratch
docker-compose down -v  # WARNING: Deletes all data!
docker-compose up -d
```

---

## Configuration

### Change Password

Edit `docker/docker-compose.yml`:

```yaml
environment:
  POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-YOUR_SECURE_PASSWORD}
```

Or set environment variable:

```bash
export POSTGRES_PASSWORD=your_secure_password
docker-compose up -d
```

### Adjust Memory

Edit `docker/docker-compose.yml`:

```yaml
deploy:
  resources:
    limits:
      cpus: '4'
      memory: 8G  # Change this
```

### Persistent Storage

Data is stored in Docker volume `postgres_data`.

**Backup:**
```bash
# Dump database
docker exec hartonomous pg_dump -U postgres hartonomous > backup.sql

# Or backup volume
docker run --rm \
  -v postgres_data:/data \
  -v $(pwd):/backup \
  ubuntu tar czf /backup/postgres_data.tar.gz /data
```

**Restore:**
```bash
# Restore from dump
cat backup.sql | docker exec -i hartonomous psql -U postgres hartonomous
```

---

## Next Steps

### Learn the System

1. **Read the Vision** - `docs/01-VISION.md`
2. **Understand Architecture** - `docs/02-ARCHITECTURE.md`
3. **Explore Examples** - Run `schema/999_examples.sql`

### Experiment

```sql
-- Create concept atoms
SELECT atomize_value(convert_to('AI', 'UTF8'), 'AI', '{"modality": "concept"}');
SELECT atomize_value(convert_to('Machine Learning', 'UTF8'), 'Machine Learning', '{"modality": "concept"}');

-- Compute spatial positions
UPDATE atom 
SET spatial_key = compute_spatial_position(atom_id)
WHERE metadata->>'modality' = 'concept';

-- Create relations
DO $$
DECLARE
    v_ai_id BIGINT;
    v_ml_id BIGINT;
BEGIN
    SELECT atom_id INTO v_ai_id FROM atom WHERE canonical_text = 'AI';
    SELECT atom_id INTO v_ml_id FROM atom WHERE canonical_text = 'Machine Learning';
    PERFORM create_relation(v_ai_id, v_ml_id, 'includes', 0.9);
END $$;
```

### Develop

1. **Fork the Repository** - `https://github.com/AHartTN/Hartonomous`
2. **Read Contributing Guide** - `CONTRIBUTING.md`
3. **Check Roadmap** - `CHECKLIST.md`

---

## Stopping & Cleanup

### Stop Services

```bash
cd docker
docker-compose stop
```

### Stop and Remove Containers

```bash
docker-compose down
```

### Complete Cleanup (?? Deletes all data)

```bash
docker-compose down -v  # Remove volumes
docker system prune -a  # Remove images
```

---

## Production Deployment

For production deployments, see:

- **Azure** - `docs/11-DEPLOYMENT.md#azure`
- **AWS** - `docs/11-DEPLOYMENT.md#aws`
- **GCP** - `docs/11-DEPLOYMENT.md#gcp`
- **Kubernetes** - `docs/11-DEPLOYMENT.md#kubernetes`

---

## Getting Help

- **Documentation** - `docs/00-START-HERE.md`
- **GitHub Issues** - [Open an issue](https://github.com/AHartTN/Hartonomous/issues)
- **Email** - aharttn@gmail.com
- **Discord** - Coming soon

---

## Common Commands Reference

```bash
# Start
docker-compose up -d

# Stop
docker-compose stop

# Restart
docker-compose restart

# View logs
docker-compose logs -f

# Connect to database
psql -h localhost -U postgres -d hartonomous

# Run SQL file
psql -h localhost -U postgres -d hartonomous -f yourfile.sql

# Backup
docker exec hartonomous pg_dump -U postgres hartonomous > backup.sql

# Check status
docker ps | grep hartonomous
docker-compose ps
```

---

**You're ready! Welcome to Hartonomous. ??**

It's atoms all the way down.
