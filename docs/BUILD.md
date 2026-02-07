# Build Guide

Complete instructions for building and deploying Hartonomous.

---

## Prerequisites

### System Requirements

- **OS**: Ubuntu 22.04+ or Debian-based Linux
- **RAM**: 16GB minimum, 32GB recommended
- **Storage**: 50GB for build artifacts + database
- **CPU**: x86_64 with AVX2 (AVX-512 for optimal performance)

### Required Software

#### 1. PostgreSQL 18
```bash
# Add PostgreSQL repository
sudo sh -c 'echo "deb http://apt.postgresql.org/pub/repos/apt $(lsb_release -cs)-pgdg main" > /etc/apt/sources.list.d/pgdg.list'
wget --quiet -O - https://www.postgresql.org/media/keys/ACCC4CF8.asc | sudo apt-key add -
sudo apt update

# Install PostgreSQL 18
sudo apt install postgresql-18 postgresql-18-postgis-3
```

#### 2. Intel oneAPI Base Toolkit (MKL)
```bash
# Download from Intel (or use package manager)
wget https://registrationcenter-download.intel.com/akdlm/IRC_NAS/[version]/l_BaseKit_p_[version]_offline.sh

# Install
sh ./l_BaseKit_p_[version]_offline.sh

# Source environment (add to ~/.bashrc for persistence)
source /opt/intel/oneapi/setvars.sh
```

#### 3. Build Tools
```bash
sudo apt install \
  build-essential \
  cmake \
  ninja-build \
  git \
  pkg-config \
  libpq-dev \
  postgresql-server-dev-18
```

#### 4. C++20 Compiler
```bash
# GCC 11+
sudo apt install g++-12
    - UCD (Unicode Character Database)
    - UCA (Unicode Collation Algorithm)
    - Semantic sequences, stroke counts, decompositions

  #### Step 5b: Ingest WordNet and OMW-data
  ```bash
  ./scripts/linux/06-ingest-wordnet-omw.sh
  ```


# Verify
g++-12 --version  # Should be 12.0+
```

---

## Clone Repository

```bash
git clone --recursive https://github.com/AHartTN/Hartonomous.git
cd Hartonomous

# If you forgot --recursive:
git submodule update --init --recursive
```

**Verify submodules:**
```bash
ls Engine/external/
# Should show: blake3/ eigen/ hilbert/ hnswlib/ json/ postgis/ spectra/ tree-sitter/
```

---

## Build Process

### Option 1: Full Automated Pipeline

**Recommended for first-time setup:**

```bash
./full-send.sh
```

This runs the complete pipeline:
1. Build C++ engine (82 targets)
2. Run unit tests (validate build)
3. Setup development environment (one-time symlinks)
4. Create seed database
5. Run UCDIngestor (Unicode metadata)
6. Create hartonomous database
7. Load extensions (PostGIS, s3, hartonomous)
8. Populate Atoms (~1.114M records)
9. Ingest sample data (if available)
10. Run integration + e2e tests

**Time:** 10-30 minutes depending on hardware.

**Logs:** Saved to `logs/` directory.

### Option 2: Step-by-Step Build

**For development or troubleshooting:**

#### Step 1: Build Engine
```bash
./scripts/build/build-all.sh
```

Builds:
- `libengine_core.so` - Pure geometry/math (no database)
- `libengine_io.so` - Database integration
- `libengine.so` - Unified for C# interop
- `s3.so` - PostgreSQL extension (S³ operations)
- `hartonomous.so` - PostgreSQL extension (full substrate)
- Ingestion tools: `seed_unicode`, `ingest_model`, `ingest_text`, `walk_test`

#### Step 2: Run Unit Tests
```bash
./scripts/test/run-unit-tests.sh
```

**Expected:** 22/22 tests passing in ~0.23 seconds.

These tests have **no external dependencies** - pure logic validation.

#### Step 3: Install Locally + Setup Symlinks (One-Time)
```bash
# Install to project/install/ directory
./scripts/build/install-local.sh

# Create symlinks from system dirs → project/install/ (requires sudo, ONE TIME)
sudo ./scripts/build/install-dev-symlinks.sh

# Update library cache (only ldconfig needs sudo going forward)
sudo ldconfig
```

**After this point:** `./rebuild.sh` handles everything without sudo (except final ldconfig).

#### Step 4: Create Seed Database
```bash
./scripts/database/setup-seed-db.sh
```

Creates temporary `ucd_seed` database for Unicode metadata.

#### Step 5: Run UCDIngestor
```bash
./UCDIngestor/setup_db.sh
```

Populates seed database with:
- UCD (Unicode Character Database)
- UCA (Unicode Collation Algorithm)
- Semantic sequences, stroke counts, decompositions

**Time:** 5-10 minutes.

#### Step 6: Create Hartonomous Database
```bash
./scripts/database/create-hartonomous-db.sh
```

Creates `hartonomous` database and loads schema:
- `00-foundation.sql` - Extensions, types
- `01-core-tables.sql` - Atom, Composition, Relation tables
- `02-functions.sql` - SQL functions

#### Step 7: Load Extensions
```bash
./scripts/database/load-extensions.sh
```

Loads into hartonomous database:
-  `postgis` - Spatial operations
- `s3` - S³ sphere functions
- `hartonomous` - Full substrate operations

#### Step 8: Populate Atoms (Lock Foundation)
```bash
./scripts/database/populate-atoms.sh
```

Runs `seed_unicode` tool:
- Reads UCD/UCA from seed database
- Applies Super Fibonacci distribution
- Uses Hopf fibration for S³ projection
- Creates ~1.114M Atom + Physicality records

**This is CRITICAL:** Atoms become immutable after this. All future intelligence builds on this foundation.

**Time:** 10-20 minutes.

**Verify:**
```bash
psql -U postgres -d hartonomous -c "SELECT COUNT(*) FROM hartonomous.atom;"
# Should return: ~1114000
```

#### Step 9: Ingest Data (Optional)

**Embeddings (if available):**
```bash
./scripts/ingest/ingest-embeddings.sh path/to/model/
```

**Text:**
```bash
./scripts/ingest/ingest-text.sh test-data/moby_dick.txt
```

#### Step 10: Run Integration Tests
```bash
./scripts/test/run-integration-tests.sh
```

**Expected:** 4/4 tests passing.

These tests **require hartonomous database** with  extensions loaded.

---

## Development Workflow

### Fast Iteration After Initial Setup

```bash
# Make changes to C++ code
vim Engine/src/ingestion/ingestion_pipeline.cpp

# Rebuild (automatically copies to install/, runs ldconfig)
./rebuild.sh

# Run unit tests
./scripts/test/run-unit-tests.sh

# Run integration tests
./scripts/test/run-integration-tests.sh
```

**No sudo needed** (except ldconfig at end of rebuild.sh).

### Build Targets

```bash
# Build only C++ engine
./scripts/build/build-engine.sh

# Build engine + PostgreSQL extensions
./scripts/build/build-all.sh

# Build specific target
cd build/linux-release-max-perf
ninja engine_core
```

---

## Configuration

### CMake Presets

Default preset: `linux-release-max-perf`

**Options:**
- `linux-release-max-perf`: `-march=native`, all optimizations
- `linux-release-portable`: `-march=x86-64-v3`, portable to most CPUs
- `linux-debug`: Debug symbols, no optimizations

**Change preset:**
```bash
cmake --preset linux-release-portable
cmake --build build/linux-release-portable
```

### Build Options

Set in `CMakeLists.txt` or via `-D` flags:

```bash
cmake -B build \
  -DHARTONOMOUS_ENABLE_NATIVE_ARCH=ON \    # -march=native
  -DHARTONOMOUS_MKL_THREADING=GNU \         # GNU OpenMP
  -DHARTONOMOUS_MKL_INTERFACE=LP64 \        # 32-bit int BLAS
  -DHARTONOMOUS_HNSW_SIMD=AUTO              # Auto-detect SIMD
```

---

## Database Configuration

### Connection Settings

**Environment variables** (used by tools):
```bash
export HARTONOMOUS_DB_HOST=localhost
export HARTONOMOUS_DB_PORT=5432
export HARTONOMOUS_DB_USER=postgres
export HARTONOMOUS_DB_NAME=hartonomous
```

### Performance Tuning

Edit `/etc/postgresql/18/main/postgresql.conf`:

```conf
# Memory
shared_buffers = 8GB                    # 25% of RAM
effective_cache_size = 24GB             # 75% of RAM
work_mem = 256MB                        # Per operation
maintenance_work_mem = 2GB              # For VACUUM, CREATE INDEX

# Parallelism
max_parallel_workers_per_gather = 4
max_parallel_workers = 8

# Disk
random_page_cost = 1.1                  # SSD
effective_io_concurrency = 200          # SSD

# Planner
default_statistics_target = 100
```

Restart PostgreSQL:
```bash
sudo systemctl restart postgresql
```

---

## Troubleshooting

### Build Failures

**MKL not found:**
```bash
# Source MKL environment
source /opt/intel/oneapi/setvars.sh

# Or set MKLROOT manually
export MKLROOT=/opt/intel/oneapi/mkl/latest
```

**PostgreSQL headers not found:**
```bash
# Install dev package
sudo apt install postgresql-server-dev-18

# Verify pg_config
pg_config --includedir-server
```

**Submodules missing:**
```bash
git submodule update --init --recursive
```

### Runtime Issues

**Library not found:**
```bash
# Check symlinks exist
ls -la /usr/local/lib/libengine*.so

# Run ldconfig
sudo ldconfig

# Verify dynamic linker sees them
ldconfig -p | grep engine
```

**Database connection failure:**
```bash
# Check PostgreSQL running
sudo systemctl status postgresql

# Check connection
psql -U postgres -d hartonomous -c "SELECT 1;"
```

**Extension not found:**
```bash
# Check extension installed
ls /usr/lib/postgresql/18/lib/*.so

# Try loading manually
psql -U postgres -d hartonomous -c "CREATE EXTENSION hartonomous;"
```

### Test Failures

**Unit tests fail:**
- Check build completed successfully
- Run individual test: `./build/linux-release-max-perf/Engine/tests/unit/test_hashing`

**Integration tests fail:**
- Check hartonomous database exists
- Check extensions loaded: `psql -U postgres -d hartonomous -c "\dx"`
- Check Atoms populated: `SELECT COUNT(*) FROM hartonomous.atom;`

---

## Performance Validation

### Benchmark Build Times

```bash
time ./scripts/build/build-all.sh
```

**Expected:** 2-5 minutes on modern hardware (with OBJECT libraries, ~40-50% faster than before).

### Benchmark Query Performance

```bash
# After ingestion
psql -U postgres -d hartonomous << EOF
\timing on
SELECT COUNT(*) FROM hartonomous.relation;
SELECT * FROM hartonomous.relation ORDER BY random() LIMIT 1000;
EOF
```

**Expected:** Milliseconds for complex queries thanks to spatial indexing.

---

## Next Steps

- Read [VISION.md](VISION.md) for paradigm overview
- Read [ARCHITECTURE.md](ARCHITECTURE.md) for technical details
- Read [MODELS.md](MODELS.md) for model integration
- Start ingesting data: `./scripts/ingest/`

---

## Clean Build

**Remove all build artifacts:**
```bash
rm -rf build/ install/
./scripts/build/build-all.sh
```

**Drop and recreate database:**
```bash
./scripts/database/create-hartonomous-db.sh --drop
./scripts/database/populate-atoms.sh
```

**Full reset:**
```bash
rm -rf build/ install/
dropdb hartonomous
dropdb ucd_seed
./full-send.sh
```
