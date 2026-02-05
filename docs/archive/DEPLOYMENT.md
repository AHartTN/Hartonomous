# Hartonomous Linux Deployment Guide

## Prerequisites

- Ubuntu 22.04+ or Debian-based Linux
- PostgreSQL 18
- Intel oneAPI toolkit (MKL)
- CMake 3.25+
- Ninja build system
- C++20 compiler (GCC 11+)

## Quick Start

```bash
# Complete deployment
./scripts/00-full-pipeline.sh linux-release-max-perf

# Or step-by-step:
./scripts/02-build-all.sh linux-release-max-perf
sudo ./scripts/03-install-extension.sh linux-release-max-perf
./scripts/01-rebuild-database.sh
```

## Build Configuration

### Presets
- `linux-release-max-perf`: Maximum performance (-march=native, -ffast-math)
- `linux-release-portable`: Portable build (AVX2 only)
- `linux-debug`: Debug build with sanitizers

### Build Artifacts
- `build/<preset>/Engine/libengine.a` - Core engine library
- `build/<preset>/libblake3_impl.a` - BLAKE3 hash library
- `build/<preset>/PostgresExtension/hartonomous.so` - PostgreSQL extension

## Database Setup

Database: `hypercube`
Extension: `hartonomous v0.1.0`

### Available Functions

```sql
-- Version info
SELECT hartonomous_version();

-- BLAKE3 hashing
SELECT blake3_hash('Hello, World!');
SELECT blake3_hash_codepoint(65);  -- 'A'

-- Unicode projection to S³
SELECT codepoint_to_s3(65);
SELECT codepoint_to_hilbert(65);

-- Centroid computation
SELECT compute_centroid(ARRAY[
    ROW(1.0, 0.0, 0.0, 0.0)::s3_point,
    ROW(0.0, 1.0, 0.0, 0.0)::s3_point
]);

-- Text ingestion (returns stats)
SELECT ingest_text('Sample text');

-- Semantic search
SELECT * FROM semantic_search('query text');
```

## Verification

```bash
# Test extension
psql -d hypercube -c "SELECT hartonomous_version();"

# Check all functions
psql -d hypercube -c "\df hartonomous*"

# Run full test suite
./scripts/05-run-queries.sh
```

## Production Deployment

1. **Build for production**
   ```bash
   cmake --preset linux-release-max-perf
   cmake --build build/linux-release-max-perf -j$(nproc)
   ```

2. **Install extension**
   ```bash
   sudo cp build/linux-release-max-perf/PostgresExtension/hartonomous.so \
       /usr/lib/postgresql/18/lib/
   sudo cp PostgresExtension/hartonomous.control \
       /usr/share/postgresql/18/extension/
   sudo cp PostgresExtension/hartonomous--0.1.0.sql \
       /usr/share/postgresql/18/extension/
   ```

3. **Create database and load extension**
   ```bash
   sudo -u postgres createdb hypercube
   sudo -u postgres psql -d hypercube -c "CREATE EXTENSION postgis;"
   sudo -u postgres psql -d hypercube -f PostgresExtension/schema/*.sql
   sudo -u postgres psql -d hypercube -c "CREATE EXTENSION hartonomous;"
   ```

## Architecture

- **Engine**: C++20 core with MKL-accelerated linear algebra
- **PostgreSQL Extension**: C/C++ bridge exposing functions to SQL
- **Dependencies**:
  - Eigen (MKL backend)
  - BLAKE3 (SIMD-optimized)
  - HNSWlib (approximate nearest neighbor)
  - nlohmann/json
  - PostGIS (spatial operations)

## Performance Configuration

Environment variables for MKL:
```bash
export MKL_NUM_THREADS=1         # Sequential mode (default)
export MKL_DYNAMIC=FALSE
export OMP_NUM_THREADS=1
```

## Troubleshooting

### Extension not loading
```bash
# Check if library exists
ls -l /usr/lib/postgresql/18/lib/hartonomous.so

# Check PostgreSQL logs
sudo tail -f /var/log/postgresql/postgresql-18-main.log
```

### Build failures
```bash
# Clean rebuild
rm -rf build
cmake --preset linux-release-max-perf
cmake --build build/linux-release-max-perf -j$(nproc)
```

### Database connection issues
```bash
# Verify PostgreSQL is running
sudo systemctl status postgresql

# Check peer authentication
sudo -u postgres psql -c "SELECT version();"
```
