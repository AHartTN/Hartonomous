# Hartonomous Scripts

This directory contains automation scripts for building, installing, and testing Hartonomous.

## Quick Start

```bash
# 1. Verify your setup
./scripts/verify-setup.sh

# 2. Run full pipeline (recommended)
./scripts/00-full-pipeline.sh

# 3. Or run individual steps
./scripts/01-rebuild-database.sh
./scripts/02-build-all.sh
./scripts/03-install-extension.sh
./scripts/04-ingest-test-data.sh
./scripts/05-run-queries.sh
```

## Script Descriptions

### `verify-setup.sh` - Pre-flight Checks

**Purpose**: Verify that your environment is ready before running the pipeline

**Checks**:
- ✓ Git submodules initialized
- ✓ CMake version >= 3.25
- ✓ C++ compiler available (GCC 11+, Clang 14+, or MSVC 2022+)
- ✓ PostgreSQL installed and running
- ✓ PostGIS extension available
- ✓ PostgreSQL connection works
- ✓ Test data present
- ✓ Scripts executable
- ✓ Schema files present

**Usage**:
```bash
./scripts/verify-setup.sh
```

**Exit codes**:
- `0`: All checks passed, ready to proceed
- `1`: Errors found, fix before continuing

---

### `00-full-pipeline.sh` - Master Script

**Purpose**: Run the complete setup, build, and test pipeline from start to finish

**Steps**:
1. Rebuild database (drop + create hypercube)
2. Build C++ engine
3. Install PostgreSQL extension
4. Ingest test data (Moby Dick + minilm)
5. Run semantic queries

**Usage**:
```bash
# Default (release-native preset)
./scripts/00-full-pipeline.sh

# Specific preset
./scripts/00-full-pipeline.sh release-portable

# Skip individual steps
SKIP_DB=1 ./scripts/00-full-pipeline.sh           # Skip database rebuild
SKIP_BUILD=1 ./scripts/00-full-pipeline.sh        # Skip C++ build
SKIP_EXTENSION=1 ./scripts/00-full-pipeline.sh    # Skip extension install
SKIP_INGEST=1 ./scripts/00-full-pipeline.sh       # Skip data ingestion
SKIP_QUERIES=1 ./scripts/00-full-pipeline.sh      # Skip query tests

# Combine skips
SKIP_DB=1 SKIP_BUILD=1 ./scripts/00-full-pipeline.sh
```

**Expected time**: 15-30 minutes (first run)

**Exit codes**:
- `0`: Pipeline completed successfully
- `1`: Pipeline failed (error message will indicate which step)

---

### `01-rebuild-database.sh` - Database Setup

**Purpose**: Drop and recreate the hypercube database with fresh schema

**What it does**:
1. Drops existing hypercube database (if exists)
2. Creates new hypercube database
3. Enables PostGIS extension
4. Applies all schema files from `schema/`
5. Creates spatial and hash indexes
6. Verifies table count

**Usage**:
```bash
./scripts/01-rebuild-database.sh
```

**Environment variables**:
- `PGHOST`: PostgreSQL host (default: localhost)
- `PGPORT`: PostgreSQL port (default: 5432)
- `PGUSER`: PostgreSQL user (default: postgres)
- `PGDATABASE`: Database name (default: hypercube)
- `PGPASSWORD`: PostgreSQL password (optional)

**Example**:
```bash
# Connect to remote PostgreSQL
PGHOST=db.example.com PGPORT=5433 PGUSER=admin ./scripts/01-rebuild-database.sh

# Use different database name
PGDATABASE=hartonomous_dev ./scripts/01-rebuild-database.sh
```

**Expected time**: 1-2 minutes

**Warning**: This script **DROPS** the existing database. All data will be lost.

---

### `02-build-all.sh` - Build C++ Code

**Purpose**: Build the Hartonomous C++ engine

**What it does**:
1. Updates git submodules
2. Runs CMake configuration
3. Builds C++ engine with parallel jobs
4. Verifies libengine.a exists

**Usage**:
```bash
# Default (Release, release-native preset)
./scripts/02-build-all.sh

# Specific build type and preset
./scripts/02-build-all.sh Release release-portable
./scripts/02-build-all.sh Debug debug

# Just rebuild (skip CMake configuration)
cmake --build build/release-native -j $(nproc)
```

**Build presets** (defined in `CMakePresets.json`):
- `release-native`: Release build, native CPU optimizations (recommended)
- `release-portable`: Release build, portable binaries
- `debug`: Debug build with symbols

**Expected time**: 5-15 minutes (first build), 30 seconds (incremental)

**Output**: `build/<preset>/Engine/libengine.a`

---

### `03-install-extension.sh` - Install PostgreSQL Extension

**Purpose**: Install the Hartonomous PostgreSQL extension

**What it does**:
1. Finds PostgreSQL installation directories
2. Creates `hartonomous.control`
3. Creates `hartonomous--0.1.0.sql`
4. Installs to PostgreSQL extension directory
5. Tests installation with `CREATE EXTENSION`

**Usage**:
```bash
# Default preset
./scripts/03-install-extension.sh

# Specific preset
./scripts/03-install-extension.sh release-portable
```

**Requirements**:
- `pg_config` must be in PATH
- Sudo permissions (to write to PostgreSQL directories)

**Installation directories**:
- Extension files: `$(pg_config --sharedir)/extension/`
- Control file: `hartonomous.control`
- SQL file: `hartonomous--0.1.0.sql`

**Expected time**: 30 seconds

**Note**: You may be prompted for sudo password

---

### `04-ingest-test-data.sh` - Ingest Test Data

**Purpose**: Ingest test data (Moby Dick + HuggingFace models) into the database

**What it does**:
1. Builds C++ ingestion tool
2. Ingests Moby Dick text file
3. Ingests HuggingFace minilm model
4. Falls back to SQL INSERT if C++ build fails
5. Verifies ingestion with row counts

**Usage**:
```bash
# Default preset
./scripts/04-ingest-test-data.sh

# Specific preset
./scripts/04-ingest-test-data.sh release-portable
```

**Test data locations**:
- Moby Dick: `test-data/moby-dick.txt`
- minilm model: `test-data/minilm/`

**Fallback behavior**:
If C++ ingestion tool build fails, script inserts sample data via SQL:
- Compositions: Call, me, Ishmael, Captain, Ahab, ship, Pequod
- Relations: "Captain Ahab of the Pequod"

**Expected time**: 2-10 minutes (depends on data size)

**Output**:
```
Ingestion complete:
  Atoms: 5000 new, 0 existing
  Compositions: 500 new, 0 existing
  Relations: 100
  Compression: 87.5%
```

---

### `05-run-queries.sh` - Run Semantic Queries

**Purpose**: Demonstrate semantic search capabilities with example queries

**Queries**:

1. **Find composition by text**
   ```sql
   SELECT text, hash FROM compositions WHERE LOWER(text) = 'captain';
   ```

2. **Find related compositions** (co-occurrence)
   ```sql
   -- Find words that appear with "Captain" in relations
   -- Expected: Ahab, ship, Pequod, crew, etc.
   ```

3. **Answer question**: "What is the captain's name?"
   ```sql
   -- Uses proper noun detection and relevance ranking
   -- Expected: "Ahab" with highest confidence score
   ```

4. **Show relation context**
   ```sql
   -- Show full text of relations containing "Captain"
   -- Expected: "Captain Ahab of the Pequod", etc.
   ```

5. **Database statistics**
   ```sql
   -- Row counts and sizes for atoms, compositions, relations
   ```

**Usage**:
```bash
./scripts/05-run-queries.sh
```

**Expected time**: 30 seconds

**Success criteria**:
- Query 2 returns "Ahab" in top results
- Query 3 returns "Ahab" as highest-ranked answer
- No SQL errors

---

### `common.sh` - Shared Utilities

**Purpose**: Provide common functions for all scripts

**Functions**:

- `print_header(msg)`: Print colored header
- `print_step(msg)`: Print step indicator
- `print_success(msg)`: Print success message
- `print_error(msg)`: Print error message
- `print_warning(msg)`: Print warning message
- `print_info(msg)`: Print info message
- `print_complete(msg)`: Print completion banner
- `command_exists(cmd)`: Check if command is available
- `check_postgres()`: Verify PostgreSQL connection
- `get_repo_root()`: Get repository root directory

**Usage**:
```bash
source "$(dirname "$0")/common.sh"

print_step "Building engine..."
if cmake --build .; then
    print_success "Build complete"
else
    print_error "Build failed"
    exit 1
fi
```

**Colors**:
- Red: Errors
- Green: Success
- Yellow: Warnings
- Cyan: Steps
- Blue: Info
- Magenta: Headers

---

## Environment Variables

### PostgreSQL Connection

All scripts that interact with PostgreSQL use these variables:

- `PGHOST`: Database host (default: `localhost`)
- `PGPORT`: Database port (default: `5432`)
- `PGUSER`: Database user (default: `postgres`)
- `PGDATABASE`: Database name (default: `hypercube`)
- `PGPASSWORD`: Database password (optional, if not using peer auth)

**Example**:
```bash
export PGHOST=db.example.com
export PGPORT=5433
export PGUSER=admin
export PGPASSWORD=secretpass
export PGDATABASE=hartonomous_dev

./scripts/00-full-pipeline.sh
```

### Build Configuration

- `PRESET`: CMake preset (default: `release-native`)
- `BUILD_TYPE`: Release or Debug (default: `Release`)

**Example**:
```bash
PRESET=release-portable ./scripts/02-build-all.sh
```

### Pipeline Control

Skip steps in the full pipeline:

- `SKIP_DB=1`: Skip database rebuild
- `SKIP_BUILD=1`: Skip C++ build
- `SKIP_EXTENSION=1`: Skip extension install
- `SKIP_INGEST=1`: Skip data ingestion
- `SKIP_QUERIES=1`: Skip query tests

**Example**:
```bash
# Only rebuild database and run queries
SKIP_BUILD=1 SKIP_EXTENSION=1 SKIP_INGEST=1 ./scripts/00-full-pipeline.sh
```

---

## Troubleshooting

### Script not executable

```bash
chmod +x scripts/*.sh
```

### PostgreSQL connection failed

```bash
# Check PostgreSQL is running
systemctl status postgresql  # Linux
brew services list           # macOS

# Test connection
psql -h localhost -U postgres -d postgres -c "SELECT 1;"

# Check environment variables
echo $PGHOST $PGPORT $PGUSER $PGDATABASE
```

### Build failed

```bash
# Check CMake version
cmake --version  # Need 3.25+

# Check compiler
g++ --version    # Need GCC 11+ or Clang 14+

# Update submodules
git submodule update --init --recursive

# Clean build
rm -rf build/
./scripts/02-build-all.sh
```

### Extension installation failed

```bash
# Check pg_config
pg_config --version

# Check permissions
sudo ls -la $(pg_config --sharedir)/extension/

# Manual install
sudo cp PostgresExtension/hartonomous.control $(pg_config --sharedir)/extension/
sudo cp PostgresExtension/hartonomous--0.1.0.sql $(pg_config --sharedir)/extension/
```

### Ingestion failed

```bash
# Check database exists
psql -d hypercube -c "SELECT COUNT(*) FROM compositions;"

# Rebuild database
./scripts/01-rebuild-database.sh

# Check test data
ls -la test-data/moby-dick.txt
```

---

## Development Workflow

### Initial Setup

```bash
# 1. Verify environment
./scripts/verify-setup.sh

# 2. Run full pipeline
./scripts/00-full-pipeline.sh
```

### Iterative Development

```bash
# 1. Rebuild C++ code
./scripts/02-build-all.sh

# 2. Re-ingest data (if needed)
SKIP_DB=1 SKIP_BUILD=1 SKIP_EXTENSION=1 ./scripts/00-full-pipeline.sh

# 3. Test queries
./scripts/05-run-queries.sh
```

### Database Schema Changes

```bash
# 1. Edit schema files in schema/
vim schema/hartonomous_schema.sql

# 2. Rebuild database
./scripts/01-rebuild-database.sh

# 3. Re-ingest data
./scripts/04-ingest-test-data.sh

# 4. Test queries
./scripts/05-run-queries.sh
```

### Extension Changes

```bash
# 1. Edit extension SQL
vim PostgresExtension/hartonomous--0.1.0.sql

# 2. Reinstall extension
./scripts/03-install-extension.sh

# 3. Test in psql
psql -d hypercube -c "DROP EXTENSION hartonomous CASCADE;"
psql -d hypercube -c "CREATE EXTENSION hartonomous;"
```

---

## Continuous Integration

These scripts are designed to work in CI/CD pipelines:

```yaml
# Example GitHub Actions
steps:
  - name: Setup PostgreSQL
    uses: actions/setup-postgresql@v1
    with:
      version: 15

  - name: Verify setup
    run: ./scripts/verify-setup.sh

  - name: Run full pipeline
    run: ./scripts/00-full-pipeline.sh
    env:
      PGHOST: localhost
      PGUSER: postgres
      PGDATABASE: hypercube
```

---

## Performance Tips

### Parallel Builds

The build script automatically detects CPU count and uses parallel jobs:

```bash
# Automatic (recommended)
./scripts/02-build-all.sh

# Manual override
cmake --build build/release-native -j 16
```

### Database Tuning

For large datasets, tune PostgreSQL before ingestion:

```sql
-- postgresql.conf
shared_buffers = 4GB
effective_cache_size = 12GB
work_mem = 256MB
maintenance_work_mem = 1GB
max_parallel_workers_per_gather = 4
```

Then reload PostgreSQL:
```bash
sudo systemctl reload postgresql
```

### Incremental Ingestion

For large files, ingest in batches:

```bash
# Split large file
split -l 10000 large-file.txt chunk-

# Ingest each chunk
for chunk in chunk-*; do
    ./build/release-native/ingest_tool "$chunk"
done
```

---

## See Also

- **Main documentation**: `../QUICKSTART.md`
- **Session summary**: `../SESSION_COMPLETE.md`
- **Schema files**: `../schema/`
- **Test data**: `../test-data/`
- **Build output**: `../build/`
