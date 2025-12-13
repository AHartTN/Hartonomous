# Hartonomous: Universal Geometric Data Architecture

**Storage IS Intelligence** - A database-centric AI architecture where the persistence layer becomes the cognitive engine.

---

## Overview

Hartonomous implements a radical inversion of traditional AI architectures: instead of treating the database as passive storage while intelligence resides in volatile model weights, Hartonomous embeds intelligence directly into the database geometry. Inference becomes spatial traversal. Learning becomes geometric refinement. Memory becomes geometry.

### Core Innovation

**All data exists as atoms in 4D semantic space (XYZM).**

- **X, Y:** Semantic coordinates (learned via LMDS)
- **Z:** Hierarchy level (raw data → features → concepts → abstractions)
- **M:** Salience/frequency (importance, dwell time, sequence weight)

**Semantic similarity manifests as Euclidean distance.** The database doesn't just store data—it understands relationships through spatial proximity.

---

## Architecture Components

```
┌─────────────────────────────────────────────────────┐
│         Application Interface Layer                 │
│      (Python - Database Client - The "Query")       │
│  - Spatial query composition (k-NN, radius, etc)    │
│  - Connection pooling & transaction management      │
│  - Result aggregation & formatting                  │
└────────────────┬────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────────────────┐
│           Cortex Physics Engine                     │
│    (PostgreSQL Extension - C++ - The "Laws")        │
│  - LMDS (Landmark Multidimensional Scaling)         │
│  - Modified Gram-Schmidt (Axis Orthonormalization)  │
│  - Stress Monitoring & Recalibration                │
└────────────────┬────────────────────────────────────┘
                 ↓
┌─────────────────────────────────────────────────────┐
│         Universal Geometric Substrate               │
│    (PostgreSQL + PostGIS - The "Brain")             │
│  - atom: Single table for all data       │
│  - GiST spatial index: O(log N) queries             │
│  - 4D XYZM coordinates: Semantic manifold           │
└────────────────┬────────────────────────────────────┘
                 ↑
┌─────────────────────────────────────────────────────┐
│            Shader Pipeline                          │
│    (Rust/C++ - External - The "Sensory Organ")     │
│  - Quantization (continuous → finite Constants)     │
│  - SDI Generation (deterministic hashing)           │
│  - RLE Encoding (time → M dimension)                │
│  - CPE Building (hierarchy construction)            │
│  - COPY Protocol (bulk loading)                     │
└─────────────────────────────────────────────────────┘
```

---

## Getting Started

### Prerequisites

- **Database:** PostgreSQL 16+ with PostGIS 3.4+
- **Shader:** Rust 1.70+ or C++17
- **Cortex:** C++ with PostgreSQL extension development headers, Eigen library
- **Connector:** Python 3.11+ with psycopg2
- **Hardware:** 16GB+ RAM, NVMe SSD recommended

### Quick Start

1. **Read the Master Plan:**
   ```bash
   cat HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md
   ```

2. **Set up database (Phase 1):**
   - Install PostgreSQL + PostGIS
   - Create schema from master plan
   - Configure for spatial workload

3. **Build Shader pipeline (Phase 2):**
   - Follow `SHADER_IMPLEMENTATION_SPECIFICATION.md`
   - Implement SDI generation and Hilbert indexing
   - Test with sample corpus

4. **Deploy Cortex (Phase 3):**
   - Follow `CORTEX_IMPLEMENTATION_SPECIFICATION.md`
   - Compile C++ extension
   - Start background worker

5. **Integrate connector (Phase 4):**
   - Follow `AGENT_INTEGRATION_SPECIFICATION.md`
   - Build Python database connector
   - Test spatial query operations

---

## Documentation Structure

### Primary Documents

| Document | Purpose | Audience |
|----------|---------|----------|
| **HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md** | Complete implementation roadmap | Architects, leads |
| **SHADER_IMPLEMENTATION_SPECIFICATION.md** | Rust/C++ preprocessing pipeline | Backend engineers |
| **CORTEX_IMPLEMENTATION_SPECIFICATION.md** | C++ physics engine | Database engineers |
| **AGENT_INTEGRATION_SPECIFICATION.md** | Python database connector | Backend engineers |
| **README.md** (this file) | Project overview | Everyone |

### Research Foundations

Located in parent directory:
- Atomic Spatial AI Architecture Blueprint.md
- Spatial AI Architecture Realignment Plan.md
- Hartonomous Converged Architecture (multiple versions)
- Reimagining Data Atomization and AI Architecture.md

---

## Core Concepts

### Universal Atomization

Everything decomposes into:

1. **Constants** (atom_class = 0)
   - Finite, indivisible units
   - Examples: token "the", number 42.00, RGB(255,0,0)
   - Quantized to limited set (~150K total)
   - Geometry: POINT ZM

2. **Compositions** (atom_class = 1)
   - Relationships between atoms
   - Examples: words, sentences, images, neural network layers
   - Infinite combinatorial space
   - Geometry: LINESTRING ZM (trajectories)

### Structured Deterministic Identity (SDI)

Identity is a mathematical function of content:

```
atom_hash = BLAKE3(Modality + SemanticClass + Normalization + Value)
```

**Properties:**
- Same content → same hash (100% deterministic)
- Different content → different hash (cryptographically secure)
- Automatic deduplication via PRIMARY KEY constraint
- Content-addressable storage (no central coordination)

### Spatial Indexing

**GiST (R-Tree) on 4D geometry:**
- Enables O(log N) k-NN queries
- Sub-millisecond latency at scale
- Outperforms dense vector approaches by 100-200×

**Hilbert Index for physical clustering:**
- Maps 4D → 1D space-filling curve
- Optimizes disk I/O for cold starts
- NOT used for semantic queries

---

## Implementation Phases

### Phase 1: Foundation (Week 1-2)
**Deliverable:** Working PostgreSQL database with spatial schema

- Deploy PostgreSQL 16+ with PostGIS 3.4+
- Create atom table (BYTEA PK, GEOMETRYZM)
- Create GiST and B-Tree indexes
- Configure for spatial workload
- Validate with test data

### Phase 2: Shader Pipeline (Week 3-4)
**Deliverable:** High-performance ingestion pipeline

- Implement SDI generation (BLAKE3 structured hash)
- Implement quantization (finite constant substrate)
- Implement RLE (time → M dimension)
- Implement CPE (hierarchy construction via pair encoding)
- Implement Hilbert index calculation
- Implement COPY protocol bulk loader
- Test with real corpus (text, images, etc.)

### Phase 3: Cortex Physics Engine (Week 5-6)
**Deliverable:** Continuous geometric refinement

- Set up C++ PostgreSQL extension environment
- Implement MaxMin landmark selection
- Implement co-occurrence distance metric
- Implement LMDS projection (Eigen library)
- Implement Modified Gram-Schmidt orthonormalization
- Implement stress monitoring and recalibration loop
- Register background worker
- Validate geometric convergence

### Phase 4: Database Connector (Week 7-8)
**Deliverable:** Python interface for spatial queries

- Set up Python psycopg2 environment
- Implement connection pooling (HartonomousPool)
- Build spatial query methods (k-NN, radius, trajectory)
- Implement high-level API (Hartonomous class)
- Create query composition methods (analogy, hierarchy traversal)
- Add monitoring and status reporting
- Test inference operations (spatial queries = intelligence)

### Phase 5: Production Hardening (Week 9-10)
**Deliverable:** Production-ready system

- Set up monitoring (pg_stat_statements, custom metrics)
- Implement backup strategy (pg_basebackup, WAL archiving)
- Performance tuning (query plans, index optimization)
- Load testing (throughput, concurrency)
- Documentation (ADRs, runbooks)
- Disaster recovery testing

---

## Key Design Decisions

### Why PostgreSQL + PostGIS?

1. **Native spatial indexing:** GiST R-Tree for O(log N) queries
2. **Extension framework:** C++ background workers (Cortex)
3. **Proven scalability:** Handles billions of rows
4. **Open source:** No licensing costs
5. **Rich ecosystem:** pgvector, pg_cron, etc.

### Why External Shader?

Database should be pure storage/query engine. CPU-intensive preprocessing (hashing, quantization, Hilbert calculation) happens externally for:
- Performance isolation
- Horizontal scalability (multiple Shader instances)
- Technology flexibility (Rust for safety, C++ for speed)

### Why Background Worker Cortex?

LMDS refinement is continuous, long-running process that shouldn't block queries:
- Runs in separate process
- Low priority (doesn't interfere with queries)
- Automatic restart on crash
- Direct SPI access (bypasses SQL parser)

### Why 4D (XYZM)?

- **XY:** Semantic manifold (learned via LMDS)
- **Z:** Hierarchy (enables multi-scale reasoning)
- **M:** Salience (importance, frequency, temporal context)

3D insufficient for rich semantic relationships. 4D provides enough dimensions for meaningful embeddings while remaining spatially indexable.

---

## Performance Characteristics

### Query Latency

| Operation | Target | Scaling |
|-----------|--------|---------|
| k-NN (k=10) | <10ms warm cache | O(log N) |
| Radius search | <10ms | O(log N) |
| Trajectory match | <50ms | O(log N) + Fréchet |

### Throughput

| Operation | Target |
|-----------|--------|
| Bulk ingestion | >10K atoms/sec |
| Concurrent k-NN | >1K QPS |
| Cortex recalibration | >100 atoms/sec |

### Storage

| Dataset | Size | Index Size |
|---------|------|------------|
| 1M atoms | ~500MB | ~100MB |
| 10M atoms | ~5GB | ~1GB |
| 100M atoms | ~50GB | ~10GB |

*With deduplication, actual storage can be 10-100× less for text corpora*

---

## Success Criteria

### Functional

- ✅ Deduplication: Identical content stored once
- ✅ Semantic convergence: Related atoms move closer over time
- ✅ Trajectory matching: Similar patterns retrieved correctly
- ✅ Reconstruction: Files decomposed and reassembled perfectly

### Performance

- ✅ Query latency: <10ms (warm cache)
- ✅ Index efficiency: Cache hit ratio >95%
- ✅ Scaling: O(log N) verified empirically
- ✅ Stability: Cortex runs >7 days without crash

### System Health

- ✅ Index bloat: <30%
- ✅ Query plans: All spatial queries use GiST index
- ✅ Backup/recovery: PITR validated
- ✅ Monitoring: Metrics dashboards operational

---

## Troubleshooting

### Slow Queries

**Symptom:** Queries taking >100ms

**Diagnosis:**
```sql
EXPLAIN ANALYZE
SELECT * FROM atom
ORDER BY geom <-> ST_MakePoint(0,0,0,0)::geometry(POINTZM,4326)
LIMIT 10;
```

Look for "Index Scan using idx_atoms_geom_gist". If you see "Seq Scan", index not being used.

**Solutions:**
1. `VACUUM ANALYZE atom;`
2. Check shared_buffers configuration
3. Verify GiST index exists and isn't corrupted

### High Memory Usage

**Diagnosis:**
```sql
SELECT
    sum(heap_blks_hit) / (sum(heap_blks_hit) + sum(heap_blks_read)) as cache_ratio
FROM pg_statio_user_tables
WHERE relname = 'atom';
```

**Solutions:**
1. Increase shared_buffers (target: 25% of RAM)
2. Check for table bloat: `SELECT * FROM pg_stat_user_tables;`
3. Run VACUUM FULL if bloat >30%

### Cortex Not Running

**Check status:**
```sql
SELECT * FROM pg_stat_activity WHERE query LIKE '%cortex%';
```

**Check logs:**
```bash
tail -f /var/log/postgresql/postgresql-16-main.log | grep -i cortex
```

**Restart:**
```sql
SELECT pg_reload_conf();  -- Reloads configuration, restarts background workers
```

---

## Contributing

This is a research project implementing novel architecture. Contributions welcome:

1. **Implementation:** Follow specifications exactly
2. **Testing:** Add tests for all new features
3. **Documentation:** Update specs if architecture changes
4. **Benchmarks:** Validate performance claims

---

## License

See individual research documents for licensing information.

---

## References

### Internal Documentation
- HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md
- SHADER_IMPLEMENTATION_SPECIFICATION.md
- CORTEX_IMPLEMENTATION_SPECIFICATION.md
- AGENT_INTEGRATION_SPECIFICATION.md

### Research Papers
- Landmark MDS: Silva & Tenenbaum (2004)
- Modified Gram-Schmidt: Björck (1967)
- Fréchet Distance: Alt & Godau (1995)
- BLAKE3: O'Connor, Aumasson, et al. (2020)

### External Resources
- PostgreSQL: https://www.postgresql.org/docs/
- PostGIS: https://postgis.net/documentation/
- psycopg2: https://www.psycopg.org/docs/
- Eigen: https://eigen.tuxfamily.org/

---

**Version:** 1.0
**Date:** 2025-12-13
**Status:** Implementation Specifications Complete

---

## Quick Reference

**Start here:** `HARTONOMOUS_IMPLEMENTATION_MASTER_PLAN.md`

**Phase 1:** Set up database
**Phase 2:** Build Shader → `SHADER_IMPLEMENTATION_SPECIFICATION.md`
**Phase 3:** Deploy Cortex → `CORTEX_IMPLEMENTATION_SPECIFICATION.md`
**Phase 4:** Build connector → `AGENT_INTEGRATION_SPECIFICATION.md`

**Validate:** Run benchmarks, compare to research docs

**Deploy:** Follow production hardening checklist

**Monitor:** pg_stat_statements + custom metrics

**Maintain:** VACUUM, REINDEX, backup/recovery

---

**Hartonomous: Where storage becomes intelligence.**
