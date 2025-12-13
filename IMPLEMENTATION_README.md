# Hartonomous - Geometric AI Implementation

Production-ready implementation of database-centric spatial AI architecture.

## Quick Start

### 1. Setup Database
```powershell
# Install PostgreSQL 16+ with PostGIS 3.4+
# Then run:
.\scripts\setup_database.ps1
```

### 2. Build Shader (Rust)
```powershell
.\scripts\build_shader.ps1
```

### 3. Build Cortex (C Extension)
```powershell
.\scripts\build_cortex.ps1
```

### 4. Install Python Connector
```powershell
cd connector
pip install -r requirements.txt
```

### 5. Run Tests
```powershell
python -m unittest discover tests
```

## Project Structure

```
Hartonomous/
├── database/
│   └── schema.sql          # PostgreSQL + PostGIS schema
├── shader/                  # Rust preprocessing pipeline
│   ├── src/
│   │   ├── sdi.rs          # BLAKE3 identity generation
│   │   ├── quantizer.rs    # Numeric/text quantization
│   │   ├── hilbert_indexer.rs  # 4D→1D indexing
│   │   └── copy_loader.rs  # Bulk loading
│   └── Cargo.toml
├── cortex/                  # C PostgreSQL extension
│   ├── cortex.c            # Background worker
│   ├── cortex--1.0.sql     # Extension SQL
│   └── Makefile
├── connector/               # Python client API
│   ├── pool.py             # Connection pooling
│   ├── connector.py        # Spatial queries
│   └── api.py              # High-level interface
├── scripts/                 # Build automation
└── tests/                   # Test suites
```

## Architecture

**Database (PostgreSQL + PostGIS):**
- `atom` table: 4D XYZM geometry
- GiST spatial index: O(log N) k-NN queries
- BLAKE3-based identity: deterministic, collision-resistant

**Shader (Rust):**
- Quantization: continuous → finite constants
- SDI generation: structured hashing
- Hilbert indexing: physical clustering
- COPY protocol: bulk loading

**Cortex (C Extension):**
- Background worker: continuous recalibration
- LMDS projection: semantic coordinate refinement
- Stress monitoring: identify atoms needing updates

**Connector (Python):**
- Connection pooling: efficient resource usage
- Spatial queries: k-NN, radius, hierarchy traversal
- High-level API: `query()`, `search()`, `abstract()`, `refine()`

## Key Principles

1. **NO random UUIDs** - All identity is deterministic (SDI)
2. **Semantic similarity = Euclidean distance** - Use `<->` operator
3. **Database IS intelligence** - No external AI frameworks
4. **COPY for bulk loading** - Never loop INSERT statements
5. **Cortex runs asynchronously** - Don't block queries

## Production Checklist

- [ ] PostgreSQL tuned for spatial workload
- [ ] GiST index VACUUM and ANALYZE scheduled
- [ ] Connection pooling configured
- [ ] Prepared statements for hot queries
- [ ] Cortex background worker monitoring
- [ ] Backup strategy (pg_basebackup + WAL)
- [ ] Metrics collection (pg_stat_statements)

## Documentation

See `.github/copilot-instructions.md` for AI agent guidance.
See `HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md` for complete specification.
