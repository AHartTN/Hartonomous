---
name: mathematical-verification
description: Validate geometric integrity of S³ substrate. Verify unit-norm constraints, spatial index correctness, and relationship graph topology. Use for validating Engine output.
---

# Mathematical Verification: Geometric Integrity

This skill provides validation procedures for the geometric substrate and relationship graph.

## Core Invariants

### 1. S³ Unit-Norm Constraint
**All points must lie on 4D unit sphere surface.**

```sql
-- Verify all Atoms on S³ surface
SELECT 
    COUNT(*) as violations,
    MIN(norm) as min_norm,
    MAX(norm) as max_norm
FROM (
    SELECT 
        a.id,
        SQRT(
            POW(p.centroid[1], 2) + 
            POW(p.centroid[2], 2) + 
            POW(p.centroid[3], 2) + 
            POW(p.centroid[4], 2)
        ) as norm
    FROM hartonomous.atom a
    JOIN hartonomous.physicality p ON a.physicality_id = p.id
) norms
WHERE ABS(norm - 1.0) > 1e-9;
-- Should return: violations = 0
```

**Tolerance**: ±1e-9 (floating point precision limit)

### 2. Hilbert Index Consistency
**128-bit Hilbert indices must preserve locality.**

```sql
-- Verify Hilbert index is 128-bit (16 bytes)
SELECT COUNT(*) as violations
FROM hartonomous.physicality
WHERE octet_length(hilbert_index) != 16;
-- Should return: 0

-- Verify locality: Nearby points in S³ -> nearby Hilbert codes
-- (Statistical test, not absolute)
WITH samples AS (
    SELECT 
        p1.id as id1,
        p2.id as id2,
        hartonomous.s3_geodesic_distance(p1.centroid, p2.centroid) as s3_dist,
        -- Hamming distance as proxy for Hilbert distance
        bit_count(p1.hilbert_index::bit(128) # p2.hilbert_index::bit(128)) as hilbert_dist
    FROM hartonomous.physicality p1
    CROSS JOIN LATERAL (
        SELECT * FROM hartonomous.physicality p2
        WHERE p2.id != p1.id
        ORDER BY random()
        LIMIT 10
    ) p2
    LIMIT 1000
)
SELECT 
    corr(s3_dist, hilbert_dist) as correlation
FROM samples;
-- Should be positive correlation (>0.3)
```

### 3. Content-Addressing Integrity
**BLAKE3 hashes must be unique and collision-free.**

```sql
-- Check for hash collisions in Compositions
SELECT hash, COUNT(*) as count
FROM hartonomous.composition
GROUP BY hash
HAVING COUNT(*) > 1;
-- Should return: 0 rows

-- Verify hash is 16 bytes
SELECT COUNT(*) as violations
FROM hartonomous.composition
WHERE octet_length(hash) != 16;
-- Should return: 0
```

### 4. Sequence Ordering Integrity
**Composition/Relation sequences must be contiguous and ordered.**

```sql
-- Check Composition sequences
WITH seq_check AS (
    SELECT 
        composition_id,
        array_agg(ordinal ORDER BY ordinal) as ordinals,
        COUNT(*) as length
    FROM hartonomous.composition_sequence
    GROUP BY composition_id
)
SELECT composition_id
FROM seq_check
WHERE ordinals != generate_series(0, length-1)::int[];
-- Should return: 0 rows (all sequences are [0,1,2,...,n-1])
```

### 5. ELO Rating Sanity
**ELO scores should be reasonable and evolving.**

```sql
-- Check ELO distribution
SELECT 
    MIN(elo_score) as min_elo,
    AVG(elo_score) as avg_elo,
    STDDEV(elo_score) as stddev_elo,
    MAX(elo_score) as max_elo,
    COUNT(*) as total_relations
FROM hartonomous.relation_rating;
-- Expect: avg around 1000-1200, stddev 200-500, no negative values

-- Check for stagnant relations (never updated)
SELECT COUNT(*) as never_updated
FROM hartonomous.relation_rating
WHERE last_updated IS NULL OR 
      last_updated < (NOW() - INTERVAL '30 days');
-- High count indicates ELO dynamics not working
```

### 6. Relationship Graph Connectivity
**Graph should be reasonably connected (no isolated components).**

```sql
-- Check for isolated compositions (no relations)
SELECT COUNT(*) as isolated_compositions
FROM hartonomous.composition c
WHERE NOT EXISTS (
    SELECT 1 FROM hartonomous.relation r
    WHERE r.composition_a_id = c.id OR r.composition_b_id = c.id
);
-- Some isolation OK (new ingests), but shouldn't be majority

-- Check component count estimate (simplified)
WITH graph_sample AS (
    SELECT 
        composition_a_id as node,
        composition_b_id as neighbor
    FROM hartonomous.relation
    LIMIT 10000
),
component_estimate AS (
    -- BFS would be expensive, this is a heuristic
    SELECT COUNT(DISTINCT node) as nodes,
           COUNT(*) as edges
    FROM graph_sample
)
SELECT 
    nodes,
    edges,
    edges::float / nodes as avg_degree
FROM component_estimate;
-- avg_degree should be >= 2 for reasonable connectivity
```

## Verification Tools

### C++ Unit Tests
```bash
# Run all unit tests
./scripts/test/run-unit-tests.sh

# Specific geometry tests
./build/linux-release-max-perf/Engine/tests/unit/test_geometry
./build/linux-release-max-perf/Engine/tests/unit/test_hilbert
```

### Integration Tests
```bash
# Database-dependent tests
./scripts/test/run-integration-tests.sh

# Tests include:
# - Atom seeding and norm verification
# - Composition creation and hash uniqueness
# - Relation ingestion and ELO initialization
# - Spatial index performance
```

## Debugging Workflow

### Norm Violations
1. Check Super Fibonacci distribution in seed_unicode
2. Verify Hopf fibration implementation
3. Verify normalization in Engine/src/geometry/s3_projection.cpp

### Hilbert Index Issues
1. Verify 128-bit encoding (no truncation)
2. Check Hilbert curve implementation (Engine/external/hilbert_hpp)
3. Verify 32-bit per dimension (4 × 32 = 128)

### Hash Collisions
1. Verify BLAKE3 usage (should be impossible with 128-bit)
2. Check for logic errors in content sequencing
3. Verify byte order consistency

### ELO Stagnation
1. Check that queries update ELO ratings
2. Verify relation_rating.last_updated is being set
3. Check ELO competition logic in Engine/src/reasoning/

## Performance Benchmarks

```sql
-- Spatial index performance (should be O(log N))
EXPLAIN ANALYZE
SELECT * FROM hartonomous.physicality
ORDER BY centroid <=> :random_point
LIMIT 10;
-- Look for "Index Scan using physicality_centroid_gist_idx"
-- Cost should be ~100-1000 even with millions of rows

-- Relationship lookup performance
EXPLAIN ANALYZE
SELECT * FROM hartonomous.relation r
JOIN hartonomous.relation_rating rr ON r.rating_id = rr.id
WHERE r.composition_a_id = :composition_id
ORDER BY rr.elo_score DESC
LIMIT 20;
-- Should use index on composition_a_id, cost < 10
```