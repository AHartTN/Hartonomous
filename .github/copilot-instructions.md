# Hartonomous AI Agent Instructions

## Project Overview

Hartonomous is a **database-centric AI architecture** where **storage IS intelligence**. Unlike traditional AI systems, there are no neural networks, no LLMs, and no vector embeddings. Intelligence emerges from geometric organization in PostgreSQL + PostGIS.

**Core Paradigm:** Inference = spatial traversal | Learning = geometric refinement | Memory = persistent 4D geometry (XYZM)

## Architecture: The Four Components

### 1. Universal Geometric Substrate (PostgreSQL + PostGIS)
- **Single table:** `atom` stores ALL data as 4D points/trajectories
- **Coordinates:** X,Y = semantic position (via LMDS), Z = hierarchy level (0=raw → 3=abstraction), M = salience/frequency
- **Indexing:** GiST R-Tree enables O(log N) k-NN queries; Hilbert curves optimize disk I/O
- **Identity:** `atom_id` is BLAKE3 hash of content (Structured Deterministic Identity - SDI) - NO random UUIDs

### 2. Shader Pipeline (Rust/C++ - External Preprocessor)
- Transforms raw data → structured atoms BEFORE database insertion
- Pipeline: Quantization → SDI generation → Hilbert indexing → RLE encoding → CPE building → COPY protocol
- Lives OUTSIDE database to isolate CPU-intensive work and enable horizontal scaling
- See [SHADER_IMPLEMENTATION_SPECIFICATION.md](SHADER_IMPLEMENTATION_SPECIFICATION.md)

### 3. Cortex Physics Engine (C++ PostgreSQL Extension)
- Background worker that continuously refines atom positions using LMDS (Landmark Multidimensional Scaling)
- Enforces "laws of physics" in semantic space: proximity = similarity
- Uses MaxMin landmark selection, Modified Gram-Schmidt orthonormalization, stress monitoring
- Links against Eigen library for linear algebra
- See [CORTEX_IMPLEMENTATION_SPECIFICATION.md](CORTEX_IMPLEMENTATION_SPECIFICATION.md)

### 4. Database Connector (Python Client)
- NOT an AI orchestrator - pure database client that translates operations into spatial SQL
- Operations: `query()` (k-NN), `search()` (radius), `neighborhood()`, `pattern()` (trajectory matching), `abstract()`/`refine()` (Z-level traversal)
- Uses `psycopg2` with connection pooling, prepared statements for performance
- See [AGENT_INTEGRATION_SPECIFICATION.md](AGENT_INTEGRATION_SPECIFICATION.md)

## Binary Ontology: Constants vs Compositions

**Constants (atom_class = 0):**
- Finite, indivisible atoms (e.g., token "the", number 42.00, RGB(255,0,0))
- Geometry: `POINT ZM`, `atomic_value` populated (max 64 bytes)
- ~150K total in vocabulary (40K numeric, 100K tokens, 256 colors, etc.)

**Compositions (atom_class = 1):**
- Infinite combinatorial space - relationships/trajectories
- Geometry: `LINESTRING ZM` (sequence through semantic space)
- `atomic_value` is NULL (meaning defined by constituent atoms)
- Examples: sentences, documents, neural network layers

## Development Workflow

### Prerequisites
- PostgreSQL 16+ with PostGIS 3.4+
- Rust 1.70+ OR C++17 (for Shader)
- C++ with PostgreSQL extension headers + Eigen (for Cortex)
- Python 3.11+ with psycopg2 (for Connector)

### Implementation Sequence (See [HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md](HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md))
1. **Week 1-2:** Database schema setup (create `atom`, GiST indexes)
2. **Week 3-4:** Shader pipeline (SDI, quantization, Hilbert, COPY protocol)
3. **Week 5-6:** Cortex background worker (LMDS, Gram-Schmidt, stress monitoring)
4. **Week 7-8:** Python connector (connection pooling, spatial queries)
5. **Week 9-10:** Production hardening (monitoring, backups, load testing)

### Testing Commands
```bash
# Python connector tests
python -m unittest tests.test_connector

# Shader integration tests (Rust)
cargo test --package shader -- --test-threads=1

# Cortex validation (SQL)
psql -d hartonomous -c "SELECT cortex_cycle_once();"
```

### Key SQL Patterns

**k-NN query (inference operation):**
```sql
SELECT atom_hash, ST_X(geom), ST_Y(geom), ST_Z(geom)
FROM atom
WHERE atom_hash != target_hash
ORDER BY geom <-> (SELECT geom FROM atom WHERE atom_hash = target_hash)
LIMIT k;
```

**Hierarchy traversal (abstraction/refinement):**
```sql
-- Moving UP hierarchy (abstraction)
SELECT * FROM atom
WHERE ST_Z(geom) > current_z
ORDER BY ST_3DDistance(geom, target_geom)
LIMIT k;
```

## Critical Conventions

1. **NO random UUIDs:** All identity is deterministic via `BLAKE3(Modality + SemanticClass + Normalization + Value)`
2. **Semantic similarity = Euclidean distance:** Use `<->` operator for k-NN, `ST_DWithin()` for radius queries
3. **Database is pure storage/query:** CPU-intensive work (hashing, quantization) happens in external Shader
4. **Cortex runs asynchronously:** Don't block queries waiting for recalibration
5. **COPY protocol for bulk loading:** Never INSERT individual atoms in loops
6. **SRID 4326 is Cartesian:** NOT geographic coordinates - local semantic plane
7. **Z dimension is discrete:** 0=raw data, 1=features, 2=concepts, 3=abstractions
8. **M dimension is salience:** Higher M = more important/frequent concepts

## Documentation Structure

- **[README.md](README.md):** Project overview, quick start
- **[HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md](HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md):** Complete implementation roadmap (1662 lines)
- Component specs: `SHADER_*.md`, `CORTEX_*.md`, `AGENT_*.md`
- Research foundations: `Atomic Spatial AI Architecture Blueprint.md`, etc. (in root)

## Common Pitfalls

❌ Don't treat this as a vector database - no embeddings, no cosine similarity
❌ Don't add external AI frameworks - intelligence IS the database geometry
❌ Don't use `ST_Distance()` for semantic queries - use `<->` for indexed k-NN
❌ Don't store large blobs in `atomic_value` - max 64 bytes, decompose larger data
❌ Don't run LMDS in query path - that's Cortex's job as background worker

✅ Think spatially: queries are geometric operations
✅ Leverage GiST index: it's optimized for <-> operator
✅ Use prepared statements for frequently-executed queries
✅ Monitor `pg_stat_statements` for query performance
✅ Validate SDI determinism: same content MUST generate same hash
