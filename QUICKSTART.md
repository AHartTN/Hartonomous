# Hartonomous - Quick Start Guide

## Overview

This guide will get you from zero to a working Hartonomous system with semantic search capabilities in ~30 minutes.

## What You'll Get

- **PostgreSQL database** with Hartonomous schema (hypercube)
- **C++ Engine** with ingestion and query capabilities
- **PostgreSQL Extension** for SQL-level functions
- **Test data**: Moby Dick text + HuggingFace minilm model
- **Working semantic search**: Query "What is the captain's name?" → "Ahab"

## Prerequisites

### Required

- **PostgreSQL 15+** with PostGIS extension
- **CMake 3.25+**
- **C++20 compiler** (GCC 11+, Clang 14+, or MSVC 2022+)
- **Git** with submodule support
- **BLAKE3** (included as submodule)

### Optional

- **Intel MKL** for accelerated linear algebra (or OpenBLAS)
- **HuggingFace model files** (minilm) in `test-data/minilm/`
- **Moby Dick text** in `test-data/moby-dick.txt`

## Installation

### 1. Clone Repository

```bash
git clone https://github.com/yourusername/Hartonomous.git
cd Hartonomous
git submodule update --init --recursive
```

### 2. Configure Environment

Set PostgreSQL connection parameters (or use defaults):

```bash
export PGHOST=localhost      # Default: localhost
export PGPORT=5432          # Default: 5432
export PGUSER=postgres      # Default: postgres
export PGDATABASE=hypercube # Default: hypercube
export PGPASSWORD=yourpass  # Optional
```

### 3. Run Full Pipeline

The master script does everything:

```bash
./scripts/00-full-pipeline.sh
```

This will:
1. ✓ Rebuild database (drop + create hypercube)
2. ✓ Build C++ engine
3. ✓ Install PostgreSQL extension
4. ✓ Ingest test data (Moby Dick + minilm)
5. ✓ Run semantic queries

**Expected time**: ~15-30 minutes (depending on hardware)

## Manual Step-by-Step

If you prefer to run each step manually:

```bash
# Step 1: Rebuild database
./scripts/01-rebuild-database.sh

# Step 2: Build C++ code
./scripts/02-build-all.sh Release release-native

# Step 3: Install PostgreSQL extension
./scripts/03-install-extension.sh release-native

# Step 4: Ingest test data
./scripts/04-ingest-test-data.sh release-native

# Step 5: Run queries
./scripts/05-run-queries.sh
```

## Testing Semantic Search

### Example 1: Find Related Compositions

```sql
psql -d hypercube -c "
WITH captain_relations AS (
    SELECT DISTINCT rc.relation_hash
    FROM relation_children rc
    JOIN compositions c ON rc.child_hash = c.hash
    WHERE LOWER(c.text) = 'captain'
)
SELECT c.text, COUNT(*) AS co_occurrence_count
FROM captain_relations cr
JOIN relation_children rc ON rc.relation_hash = cr.relation_hash
JOIN compositions c ON c.hash = rc.child_hash
WHERE LOWER(c.text) != 'captain'
GROUP BY c.text
ORDER BY co_occurrence_count DESC
LIMIT 10;
"
```

**Expected output**: Words related to "Captain" (Ahab, ship, Pequod, etc.)

### Example 2: Answer Question

Query: "What is the captain's name?"

```sql
psql -d hypercube -c "
WITH captain_related AS (
    SELECT DISTINCT rc.relation_hash
    FROM relation_children rc
    JOIN compositions c ON rc.child_hash = c.hash
    WHERE LOWER(c.text) = 'captain'
),
potential_answers AS (
    SELECT
        c.text,
        COUNT(DISTINCT cr.relation_hash) AS relevance,
        CASE WHEN c.text ~ '^[A-Z][a-z]+$' THEN 2 ELSE 1 END AS name_boost
    FROM captain_related cr
    JOIN relation_children rc ON rc.relation_hash = cr.relation_hash
    JOIN compositions c ON c.hash = rc.child_hash
    WHERE LOWER(c.text) != 'captain'
      AND LENGTH(c.text) > 2
    GROUP BY c.text
)
SELECT text AS answer, relevance * name_boost AS confidence_score
FROM potential_answers
ORDER BY confidence_score DESC
LIMIT 5;
"
```

**Expected output**: "Ahab" with highest confidence score

## Architecture Overview

### Data Model

```
Atoms (Unicode codepoints)
    ↓
Compositions (words, phrases)
    ↓
Relations (sentences, paragraphs, embeddings)
```

### Key Features

1. **Content-Addressable Storage**
   - BLAKE3 hashing ensures deduplication
   - Same content = same hash = stored once

2. **Hierarchical Merkle DAG**
   - Atoms → Compositions → Relations
   - Parent hash includes all children
   - Efficient compression and deduplication

3. **4D Spatial Indexing**
   - Every composition has a centroid (x, y, z, w) on S³ surface
   - PostGIS spatial indexes for proximity queries
   - Hilbert curve indexing for cache-friendly traversal

4. **Relationship-Based Querying**
   - Semantic search via co-occurrence counting
   - NOT proximity-based (not embedding similarity)
   - Proper noun detection for question answering

5. **Modular Ingestion**
   - Text: UTF-8 → UTF-32 → atoms → compositions
   - HuggingFace: config + tokenizer + vocab + tensors
   - Future: images, video, audio (same pipeline)

## Directory Structure

```
Hartonomous/
├── Engine/                  # C++ implementation
│   ├── include/
│   │   ├── hashing/        # BLAKE3 pipeline
│   │   ├── database/       # PostgreSQL connection
│   │   ├── ingestion/      # Text + safetensor ingestion
│   │   └── query/          # Semantic search
│   ├── src/                # Implementation files
│   ├── external/           # Git submodules
│   │   ├── blake3/
│   │   ├── eigen/
│   │   ├── hnswlib/
│   │   ├── spectra/
│   │   └── json/           # nlohmann/json
│   └── CMakeLists.txt
├── PostgresExtension/       # PostgreSQL extension
├── scripts/                 # Automation scripts
│   ├── 00-full-pipeline.sh # Master script (run this!)
│   ├── 01-rebuild-database.sh
│   ├── 02-build-all.sh
│   ├── 03-install-extension.sh
│   ├── 04-ingest-test-data.sh
│   ├── 05-run-queries.sh
│   └── common.sh
├── schema/                  # SQL schema files
├── test-data/              # Test data (Moby Dick, minilm)
└── build/                  # Build output (git-ignored)
```

## Build Presets

Available CMake presets (configure in CMakePresets.json):

- **release-native**: Release build, native CPU optimizations (default)
- **release-portable**: Release build, portable binaries
- **debug**: Debug build with symbols

Example:

```bash
./scripts/02-build-all.sh Release release-portable
```

## Environment Variables

### PostgreSQL Connection

- `PGHOST`: Database host (default: localhost)
- `PGPORT`: Database port (default: 5432)
- `PGUSER`: Database user (default: postgres)
- `PGDATABASE`: Database name (default: hypercube)
- `PGPASSWORD`: Database password (optional)

### Build Options

- `PRESET`: CMake preset (default: release-native)
- `BUILD_TYPE`: Release or Debug (default: Release)

### Pipeline Control

Skip individual steps in the full pipeline:

```bash
SKIP_DB=1 ./scripts/00-full-pipeline.sh        # Skip database rebuild
SKIP_BUILD=1 ./scripts/00-full-pipeline.sh     # Skip C++ build
SKIP_EXTENSION=1 ./scripts/00-full-pipeline.sh # Skip extension install
SKIP_INGEST=1 ./scripts/00-full-pipeline.sh    # Skip data ingestion
SKIP_QUERIES=1 ./scripts/00-full-pipeline.sh   # Skip query tests
```

## Troubleshooting

### PostgreSQL Connection Failed

```bash
# Check PostgreSQL is running
systemctl status postgresql  # Linux
brew services list           # macOS

# Test connection
psql -h localhost -U postgres -d postgres -c "SELECT 1;"

# Check environment variables
echo $PGHOST $PGPORT $PGUSER $PGDATABASE
```

### Build Failed

```bash
# Check CMake version
cmake --version  # Need 3.25+

# Check compiler
g++ --version    # Need GCC 11+ or Clang 14+

# Check submodules
git submodule update --init --recursive

# Clean build
rm -rf build/
./scripts/02-build-all.sh
```

### Extension Installation Failed

```bash
# Check pg_config
pg_config --version

# Check permissions
sudo ls -la $(pg_config --sharedir)/extension/

# Reinstall
sudo rm -f $(pg_config --sharedir)/extension/hartonomous*
./scripts/03-install-extension.sh
```

### Ingestion Failed

```bash
# Check database exists
psql -d hypercube -c "SELECT COUNT(*) FROM compositions;"

# Check test data
ls -la test-data/moby-dick.txt
ls -la test-data/minilm/

# Rebuild database
./scripts/01-rebuild-database.sh

# Re-run ingestion
./scripts/04-ingest-test-data.sh
```

## Performance Tips

### Compiler Optimizations

The default `release-native` preset enables:
- **SIMD**: AVX-512 (MSVC) or native (GCC/Clang)
- **LTO**: Link-time optimization
- **Fast math**: Non-IEEE floating-point
- **Loop unrolling**: Aggressive loop optimizations

### Database Tuning

For large datasets, tune PostgreSQL:

```sql
-- postgresql.conf
shared_buffers = 4GB
effective_cache_size = 12GB
work_mem = 256MB
maintenance_work_mem = 1GB
max_parallel_workers_per_gather = 4
```

### Ingestion Performance

- **Batch size**: Increase batch size in `text_ingester.cpp`
- **Parallelism**: Use multiple ingestion processes
- **Indexing**: Disable indexes during bulk ingestion, rebuild after

## Next Steps

1. **Ingest your own data**
   ```bash
   build/release-native/ingest_tool your-text-file.txt
   build/release-native/ingest_tool path/to/huggingface/model/
   ```

2. **Build custom queries**
   - See `scripts/05-run-queries.sh` for examples
   - Use relationship traversal for semantic search
   - Leverage spatial indexes for proximity queries

3. **Extend ingestion**
   - Add image ingestion (extract CLIP embeddings)
   - Add video ingestion (frame extraction + embeddings)
   - Add audio ingestion (Whisper + embeddings)

4. **Build applications**
   - C# app layer (future)
   - REST API for queries
   - Web UI for visualization

## Resources

- **Documentation**: See `docs/` directory
- **Schema**: See `schema/hartonomous_schema.sql`
- **Examples**: See `scripts/05-run-queries.sh`
- **Tests**: See `Engine/tests/`

## License

[Your License Here]

## Support

- Issues: https://github.com/yourusername/Hartonomous/issues
- Discussions: https://github.com/yourusername/Hartonomous/discussions
