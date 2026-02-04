---
name: semantic-sql-authoring
description: Author complex SQL queries for the Hartonomous Merkle DAG. Use when writing recursive CTEs for trajectories or PostGIS-based geometric searches on S³.
---

# Semantic SQL Authoring

This skill provides the specialized SQL patterns required to query the tiered trajectory substrate.

## Core Patterns

### 1. Trajectory Recomposition (Recursive CTE)
Reconstructs the hierarchical meaning from Relations down to Atoms.
```sql
WITH RECURSIVE cascade AS (
    -- Start with a Relation
    SELECT rs.CompositionId as child_id, rs.Ordinal, 1 as level
    FROM hartonomous.RelationSequence rs
    WHERE rs.RelationId = :target_relation_id
    UNION ALL
    -- Traverse down to Atoms if necessary (Level 1)
    SELECT cs.AtomId, cs.Ordinal, c.level + 1
    FROM hartonomous.CompositionSequence cs
    JOIN cascade c ON cs.CompositionId = c.child_id
)
SELECT * FROM cascade ORDER BY level, Ordinal;
```

### 2. 4D Geodesic KNN
Utilize the custom `<=>` operator for semantically similar lookups.
```sql
SELECT c.Id, v.Text, p.Centroid <=> :query_point as distance
FROM hartonomous.Composition c
JOIN hartonomous.Physicality p ON c.PhysicalityId = p.Id
JOIN hartonomous.v_composition_text v ON v.composition_id = c.Id
ORDER BY p.Centroid <=> :query_point
LIMIT 10;
```

### 3. uint128 Domain Operations
Handle 128-bit Hilbert indices stored as 16-byte `bytea`.
```sql
SELECT * FROM hartonomous.Composition
WHERE octet_length(hilbert_index) = 16
AND encode(hilbert_index, 'hex') LIKE 'ff%';
```

## Anti-Patterns
- **Do NOT** use `ST_Distance` (geographic); always use `<=>` or `hartonomous.geodesic_distance_s3`.
- **Do NOT** assume standard numeric comparisons for `hilbert_index`; it is a `uint128` (bytea).