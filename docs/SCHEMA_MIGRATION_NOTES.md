# Schema Migration Notes

**Date:** 2025-11-28  
**Status:** Documentation updated to reflect design intent  
**Action Required:** Schema migration from POINTZ to POINTZM

---

## Required Schema Changes

### 1. Atom Table: POINTZ ? POINTZM

**Current Schema:**
```sql
CREATE TABLE atom (
    ...
    spatial_key GEOMETRY(POINTZ, 0),  -- 3D: X, Y, Z
    ...
);
```

**Target Schema:**
```sql
CREATE TABLE atom (
    ...
    spatial_key GEOMETRY(POINTZM, 0),  -- 4D: X, Y, Z, M
    -- X, Y, Z: 3D semantic coordinates
    -- M: Hilbert curve index (N-dimensional encoding)
    ...
);
```

**Migration SQL:**
```sql
-- 1. Add temporary column
ALTER TABLE atom ADD COLUMN spatial_key_new GEOMETRY(POINTZM, 0);

-- 2. Migrate data (compute Hilbert index for existing points)
UPDATE atom
SET spatial_key_new = ST_MakePointM(
    ST_X(spatial_key),
    ST_Y(spatial_key),
    ST_Z(spatial_key),
    encode_hilbert_3d(ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key), 21)
)
WHERE spatial_key IS NOT NULL;

-- 3. Drop old column and rename
ALTER TABLE atom DROP COLUMN spatial_key;
ALTER TABLE atom RENAME COLUMN spatial_key_new TO spatial_key;

-- 4. Recreate indexes
DROP INDEX IF EXISTS idx_atom_spatial;
CREATE INDEX idx_atom_spatial ON atom USING GIST (spatial_key);  -- GiST on X,Y,Z
CREATE INDEX idx_atom_hilbert ON atom ((ST_M(spatial_key)));      -- B-tree on M (Hilbert)
```

### 2. Atom Composition: Optional POINTZM

**Current Schema:**
```sql
CREATE TABLE atom_composition (
    ...
    spatial_key GEOMETRY(POINTZ, 0),  -- Local coordinate frame
    ...
);
```

**Target Schema:**
```sql
CREATE TABLE atom_composition (
    ...
    spatial_key GEOMETRY(POINTZM, 0),  -- Local coordinate frame + Hilbert
    ...
);
```

**Decision:** May not need M coordinate in compositions (local coordinates don't need Hilbert indexing). Evaluate based on query patterns.

---

## Benefits of POINTZM

### 1. Exploit Spatial Types for Non-Spatial Data

PostGIS spatial types (`GEOMETRY`) store the Hilbert index as the M coordinate, enabling:
- **No redundant storage** (Hilbert index is part of spatial_key)
- **Native PostGIS functions** work on M coordinate
- **Consistent data model** (everything in spatial_key)

### 2. Dual Indexing Strategy

```sql
-- GiST index for exact spatial queries
SELECT * FROM atom 
WHERE ST_DWithin(spatial_key, $target, 0.15);
-- Uses R-tree on (X, Y, Z)

-- B-tree index for fast approximate queries
SELECT * FROM atom
WHERE ST_M(spatial_key) BETWEEN $hilbert_min AND $hilbert_max;
-- Uses B-tree on M (Hilbert index)

-- Combined query (best of both)
WITH candidates AS (
    SELECT * FROM atom
    WHERE ST_M(spatial_key) BETWEEN $hilbert_min AND $hilbert_max  -- Fast B-tree filter
)
SELECT * FROM candidates
WHERE ST_DWithin(spatial_key, $target, 0.15)  -- Exact GiST refinement
ORDER BY ST_Distance(spatial_key, $target) ASC
LIMIT 10;
```

### 3. N-Dimensional Extension Path

POINTZM with M=Hilbert provides a path to N-dimensional space:
- Current: Hilbert(X, Y, Z) ? 3D encoded as 1D
- Future: Hilbert(X, Y, Z, Time, Confidence, ...) ? ND encoded as 1D
- M coordinate stores the ND?1D encoding

---

## Implementation Priority

### Phase 1: Core Migration (High Priority)

- [ ] Migrate atom.spatial_key to POINTZM
- [ ] Update all ingestion code to compute M coordinate
- [ ] Create dual indexes (GiST on XYZ, B-tree on M)
- [ ] Update query code to use Hilbert approximation + spatial refinement

### Phase 2: Optimize Queries (Medium Priority)

- [ ] Benchmark GiST-only vs. Hilbert+GiST query patterns
- [ ] Add query planner hints for index selection
- [ ] Implement materialized views for hot queries

### Phase 3: N-Dimensional Extension (Future)

- [ ] Extend Hilbert algorithm to N-dimensions
- [ ] Add temporal dimension (valid_from/valid_to ? T axis)
- [ ] Add confidence dimension (metadata confidence ? C axis)
- [ ] Hilbert(X, Y, Z, T, C) ? 5D encoded in M coordinate

---

## Testing Plan

### 1. Data Integrity

```sql
-- Verify M coordinate matches computed Hilbert
SELECT atom_id, 
       ST_M(spatial_key) as stored_hilbert,
       encode_hilbert_3d(ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key), 21) as computed_hilbert
FROM atom
WHERE ABS(ST_M(spatial_key) - encode_hilbert_3d(ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key), 21)) > 0
LIMIT 10;
-- Should return 0 rows
```

### 2. Query Performance

```sql
-- Benchmark: GiST only (current)
EXPLAIN ANALYZE
SELECT * FROM atom
WHERE ST_DWithin(spatial_key, $target, 0.15)
ORDER BY ST_Distance(spatial_key, $target) ASC
LIMIT 10;

-- Benchmark: Hilbert + GiST (after migration)
EXPLAIN ANALYZE
WITH candidates AS (
    SELECT * FROM atom
    WHERE ST_M(spatial_key) BETWEEN $hilbert_min AND $hilbert_max
)
SELECT * FROM candidates
WHERE ST_DWithin(spatial_key, $target, 0.15)
ORDER BY ST_Distance(spatial_key, $target) ASC
LIMIT 10;

-- Compare execution times
```

### 3. Index Usage

```sql
-- Verify B-tree index on M is used
EXPLAIN (ANALYZE, BUFFERS)
SELECT * FROM atom
WHERE ST_M(spatial_key) BETWEEN 1000000 AND 1001000;

-- Should show: Index Scan using idx_atom_hilbert
```

---

## Rollback Plan

```sql
-- If migration causes issues, rollback:

-- 1. Recreate old POINTZ column
ALTER TABLE atom ADD COLUMN spatial_key_old GEOMETRY(POINTZ, 0);

-- 2. Copy X, Y, Z (drop M)
UPDATE atom
SET spatial_key_old = ST_MakePoint(
    ST_X(spatial_key),
    ST_Y(spatial_key),
    ST_Z(spatial_key)
);

-- 3. Swap columns
ALTER TABLE atom DROP COLUMN spatial_key;
ALTER TABLE atom RENAME COLUMN spatial_key_old TO spatial_key;

-- 4. Recreate GiST index only
CREATE INDEX idx_atom_spatial ON atom USING GIST (spatial_key);
DROP INDEX IF EXISTS idx_atom_hilbert;
```

---

## Documentation Status

? **Documentation updated** to reflect POINTZM design intent  
? **Current implementation (POINTZ)** clearly marked  
? **Migration notes** documented (this file)  
?? **Schema migration pending** (awaiting implementation)

---

**Next Steps:**
1. Implement `encode_hilbert_3d` as SQL function (PL/Python or native)
2. Execute migration SQL on development database
3. Benchmark query performance
4. Deploy to production after validation
