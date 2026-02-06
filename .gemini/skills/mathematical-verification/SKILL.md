---
name: mathematical-verification
description: Validate geometric integrity of S³ substrate. Verify unit-norm constraints, spatial index correctness, and relationship graph topology.
---

# Mathematical Verification

## Core Invariants to Verify

### 1. S³ Unit-Norm (All points on 4D unit sphere)
```sql
SELECT COUNT(*) as violations FROM (
    SELECT SQRT(POW(ST_X(p.centroid),2) + POW(ST_Y(p.centroid),2) + 
                POW(ST_Z(p.centroid),2) + POW(ST_M(p.centroid),2)) as norm
    FROM hartonomous.physicality p
) norms WHERE ABS(norm - 1.0) > 1e-9;
-- Must return 0
```

### 2. Hilbert Index (128-bit, locality-preserving)
```sql
SELECT COUNT(*) FROM hartonomous.physicality WHERE octet_length(hilbert_index) != 16;
-- Must return 0
```

### 3. BLAKE3 Content-Addressing (unique, 16-byte hashes)
```sql
SELECT hash, COUNT(*) FROM hartonomous.composition GROUP BY hash HAVING COUNT(*) > 1;
-- Must return 0 rows
```

### 4. Composition Sequences (contiguous ordinals)
```sql
WITH seq_check AS (
    SELECT composition_id, array_agg(ordinal ORDER BY ordinal) as ordinals, COUNT(*) as length
    FROM hartonomous.composition_sequence GROUP BY composition_id
)
SELECT composition_id FROM seq_check
WHERE ordinals != (SELECT array_agg(i) FROM generate_series(0, length-1) i);
-- Must return 0 rows
```

### 5. ELO Distribution (reasonable range)
```sql
SELECT MIN(elo_score), AVG(elo_score), STDDEV(elo_score), MAX(elo_score)
FROM hartonomous.relation_rating;
-- Expect: avg ~1000-1200, no negatives
```

### 6. Graph Connectivity (no majority isolation)
```sql
SELECT COUNT(*) as isolated FROM hartonomous.composition c
WHERE NOT EXISTS (SELECT 1 FROM hartonomous.relation r
    WHERE r.composition_a_id = c.id OR r.composition_b_id = c.id);
-- Some isolation OK, shouldn't be majority
```

## C++ Unit Tests
```bash
cd build/linux-release-max-perf
LD_LIBRARY_PATH="$PWD/Engine:$LD_LIBRARY_PATH" ctest --output-on-failure -L unit
```

Test coverage: hashing determinism, S³ normalization, geodesic distance, Super Fibonacci distribution, Hopf roundtrip, Hilbert encoding, locality preservation, LWGeom serialization.

## Debugging
- **Norm violations**: Check `Engine/src/geometry/super_fibonacci.cpp` normalization
- **Hilbert issues**: Check `Engine/include/spatial/hilbert_curve_4d.hpp` (4×32=128 bits)
- **Hash collisions**: Impossible with 128-bit BLAKE3 — indicates logic error in content sequencing