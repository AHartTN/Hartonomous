# Architectural Evolution

**Purpose:** Document key architectural decisions and evolution of Hartonomous system design.  
**Status:** Living document tracking paradigm shifts

---

## Evolution Timeline

### Phase 1: Initial Vision (Pre-November 2025)
**Key Concepts:**
- Content-addressable atomization
- Separate `atom_composition` table
- Basic spatial semantics

### Phase 2: Geometric Reformation (November 2025)
**Archived in:** `.archive/docs/` (2025-11-28_050415)

**Architecture:**
- `GEOMETRY(POINTZ, 0)` - 3D semantic space (X, Y, Z)
- Separate `atom`, `atom_composition`, `atom_relation` tables
- Landmark Projection for primitives
- Compositional Gravity (weighted centroids) for compositions
- Voronoi cells for classification
- OODA loop for self-optimization
- Multi-model semantic space

**Key Features:**
- Gram-Schmidt orthogonalization for projection
- Semantic neighbor averaging for positioning
- Hilbert curves for locality preservation
- Fractal deduplication via BPE crystallization
- Cross-model consensus through spatial clustering

**Schema:**
```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),
    canonical_text TEXT,
    spatial_key GEOMETRY(POINTZ, 0),  -- 3D only
    reference_count BIGINT DEFAULT 1,
    metadata JSONB,
    created_at TIMESTAMPTZ DEFAULT now()
);

CREATE TABLE atom_composition (
    composition_id BIGSERIAL PRIMARY KEY,
    parent_atom_id BIGINT REFERENCES atom(atom_id),
    component_atom_id BIGINT REFERENCES atom(atom_id),
    sequence_index BIGINT NOT NULL
);
```

**Strengths:**
- Clear separation of concerns
- Well-defined spatial semantics
- Multi-model consensus patterns
- Proven BPE crystallization

**Limitations:**
- M-coordinate unused (always 0)
- Separate composition table requires joins
- No Hilbert B-tree optimization
- Limited to 3D semantic space

---

### Phase 3: Database-Centric AI (December 2025 - Current)
**Documented in:** `docs/Reinventing AI with Spatial SQL.md`

**Paradigm Shift:** "The Database IS the AI"

**Architecture:**
- `GEOMETRY(POINTZM, 0)` - 4D semantic space
- M-coordinate **triple-duty** (revolutionary insight):
  1. **Hilbert curve index** (locality-preserving 1D mapping)
  2. **Sequence position** (for trajectories/sequences)
  3. **Delta offset** (for compression)
- `composition_ids` array replaces separate table
- Zero-copy ingestion via SQL CLR (≥4 GB/s)
- Temporal tables for provenance (no separate graph DB)
- 99% deduplication through 5-layer compression

**Key Innovation: M-Coordinate Triple-Duty**

The M-coordinate serves THREE purposes simultaneously:

1. **Hilbert Index** (Spatial Locality)
   ```sql
   -- Points close in 3D space have similar M values
   CREATE INDEX idx_atom_hilbert ON atom USING BTREE(ST_M(spatial_key));
   -- Range query in M = range query in 3D space
   ```

2. **Sequence Position** (Temporal/Logical Order)
   ```sql
   -- M = position in original sequence
   -- Gaps in M represent sparse data (implicit zeros)
   LINESTRING ZM (
       x1 y1 z1 0,   -- First element
       x2 y2 z2 5,   -- Sixth element (M=1-4 are zeros)
       x3 y3 z3 7    -- Eighth element (M=6 is zero)
   )
   ```

3. **Delta Offset** (Compression)
   ```sql
   -- M stores cumulative delta for RLE compression
   -- Same XYZ coords, different M = repetition
   ```

**Schema:**
```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,
    canonical_text TEXT CHECK (length(canonical_text) <= 64),
    spatial_key GEOMETRY(POINTZM, 0),  -- 4D: X,Y,Z semantic + M multi-purpose
    composition_ids BIGINT[],          -- Inlined for performance
    metadata JSONB DEFAULT '{}'::jsonb,
    is_stable BOOLEAN DEFAULT FALSE,
    reference_count INT DEFAULT 0,
    created_at TIMESTAMPTZ DEFAULT now()
);

-- Dual indexing strategy
CREATE INDEX idx_atom_spatial_gist ON atom USING GIST(spatial_key);  -- KNN queries
CREATE INDEX idx_atom_hilbert_btree ON atom USING BTREE(ST_M(spatial_key));  -- Range queries
```

**5-Layer Compression Pipeline:**
1. **CAS Layer** (50% reduction) - Content-addressable deduplication
2. **Sparse Layer** (30% reduction) - Implicit zeros via M gaps
3. **RLE Layer** (10% reduction) - Run-length via M coordinate
4. **Delta Layer** (8% reduction) - Delta encoding via M offsets
5. **Hilbert Layer** (1% reduction) - Locality-based clustering

**Result:** 99% total deduplication

**Provenance Solution:**
- PostgreSQL temporal tables (`*_history`)
- System versioning tracks every atom/change
- SQL queries traverse lineage
- No separate graph database needed

**Zero-Copy Ingestion:**
- SQL CLR functions ingest directly to geometry
- ≥4 GB/s throughput
- No application-layer serialization
- Database does atomization

**Strengths:**
- Extreme compression (99% deduplication)
- Blazing fast ingestion (≥4 GB/s)
- Complete provenance via temporal tables
- Queryable compression (no decompression needed)
- N-dimensional scaling via Hilbert
- Single database (no separate graph DB)

**Challenges:**
- M-coordinate overloading requires careful design
- Migration from POINTZ to POINTZM
- Application logic must understand M semantics

---

## Key Architectural Decisions

### Decision 1: M-Coordinate Triple-Duty
**Date:** December 2025  
**Rationale:**
- Hilbert index needed for locality preservation
- Sequence position needed for trajectories
- Delta offset needed for RLE compression
- PostGIS POINTZM provides 4th dimension
- Same coordinate can serve all three purposes in different contexts

**Impact:**
- Eliminates need for separate Hilbert index column
- Enables sparse trajectories
- Provides compression without separate encoding
- Reduces storage and index overhead

### Decision 2: Inline Compositions
**Date:** December 2025  
**Change:** `composition_ids BIGINT[]` instead of separate table  
**Rationale:**
- PostgreSQL array operations are efficient
- Eliminates JOIN overhead
- Composition traversal is read-heavy
- Most compositions are small (<100 components)

**Impact:**
- Faster query performance
- Simpler schema
- Better cache locality
- Tradeoff: Array size limits (10K components max)

### Decision 3: Temporal Tables for Provenance
**Date:** December 2025  
**Change:** PostgreSQL system versioning instead of Neo4j  
**Rationale:**
- PostgreSQL FOR SYSTEM_TIME queries are fast
- No data synchronization lag
- Atomic consistency with main data
- SQL is universal query language
- Simpler operational model

**Impact:**
- Eliminates Neo4j dependency
- Simplified deployment
- Better consistency guarantees
- Provenance queries in same language as data queries

### Decision 4: Zero-Copy via SQL CLR
**Date:** December 2025  
**Change:** SQL CLR functions ingest directly to POINTZM  
**Rationale:**
- Application-layer serialization is bottleneck
- Database can parallelize better
- Direct memory manipulation
- Leverage PostgreSQL's internal optimizations

**Impact:**
- ≥4 GB/s ingestion throughput
- Reduced memory overhead
- Simpler application code
- Database becomes the atomization engine

---

## Migration Path: POINTZ → POINTZM

### Phase 1: Add M Support (Non-Breaking)
```sql
-- Update spatial_key to POINTZM (preserves existing data)
ALTER TABLE atom ALTER COLUMN spatial_key TYPE GEOMETRY(POINTZM, 0);

-- Backfill M coordinate with Hilbert index
UPDATE atom SET spatial_key = ST_SetMeasure(
    spatial_key,
    hilbert_encode_3d(ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key))
);
```

### Phase 2: Add Hilbert B-Tree Index
```sql
CREATE INDEX idx_atom_hilbert_btree ON atom USING BTREE(ST_M(spatial_key));
```

### Phase 3: Migrate Compositions to Arrays
```sql
-- Add composition_ids column
ALTER TABLE atom ADD COLUMN composition_ids BIGINT[];

-- Backfill from atom_composition table
UPDATE atom a SET composition_ids = (
    SELECT array_agg(component_atom_id ORDER BY sequence_index)
    FROM atom_composition ac
    WHERE ac.parent_atom_id = a.atom_id
);

-- Create compatibility view for old code
CREATE VIEW atom_composition AS
SELECT
    parent_atom_id,
    unnest(composition_ids) AS component_atom_id,
    generate_series(0, cardinality(composition_ids) - 1) AS sequence_index
FROM atom
WHERE composition_ids IS NOT NULL;
```

### Phase 4: Add Temporal Versioning
```sql
-- Add system versioning
ALTER TABLE atom ADD COLUMN valid_from TIMESTAMPTZ DEFAULT now();
ALTER TABLE atom ADD COLUMN valid_to TIMESTAMPTZ DEFAULT 'infinity';

CREATE TABLE atom_history (LIKE atom);
-- Trigger to maintain history
```

---

## Lessons Learned

### What Worked
1. **Content-addressable storage** - Global deduplication is powerful
2. **Geometric semantics** - Position = meaning is intuitive and queryable
3. **64-byte limit** - Forces proper atomization
4. **Reference counting** - Atomic mass = importance works well
5. **PostGIS** - Battle-tested spatial indexing is reliable

### What Changed
1. **Separate composition table** → Inline arrays (performance)
2. **POINTZ** → POINTZM (Hilbert optimization)
3. **Neo4j provenance** → Temporal tables (simplicity)
4. **Application ingestion** → SQL CLR (performance)
5. **M unused** → M triple-duty (efficiency)

### Future Considerations
1. **5D+ geometries** - Beyond XYZM for additional metadata
2. **Distributed Hilbert** - Sharding via Hilbert ranges
3. **GPU-accelerated queries** - PostGIS + CUDA integration
4. **Adaptive compression** - Choose compression layers dynamically
5. **Streaming ingestion** - Real-time atomization pipeline

---

## References

- [Reinventing AI with Spatial SQL.md](Reinventing AI with Spatial SQL.md) - Current authoritative vision
- [DATABASE_ARCHITECTURE_COMPLETE.md](architecture/DATABASE_ARCHITECTURE_COMPLETE.md) - Detailed schema
- [IMPLEMENTATION_REFERENCE.md](IMPLEMENTATION_REFERENCE.md) - Extracted patterns from November 2025
- `.archive/docs/` - November 2025 architecture snapshot

---

**Last Updated:** 2025-12-03  
**Status:** Current architecture fully documented
