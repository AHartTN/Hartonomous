---
name: semantic-sql-authoring
description: Author SQL queries for Hartonomous relationship graph. Focus on ELO-weighted path queries and spatial indexing for navigation.
---

# Semantic SQL Authoring

## Schema
```
hartonomous.atom              -- ~1.114M Unicode codepoints on S³ (IMMUTABLE)
hartonomous.composition       -- N-grams of atoms (BLAKE3 content-addressed)
hartonomous.relation          -- Co-occurrence patterns (WHERE INTELLIGENCE EMERGES)
hartonomous.relation_rating   -- ELO scores (semantic proximity, NOT geometric)
hartonomous.relation_evidence -- Provenance (which ingest, for surgical deletion)
hartonomous.physicality       -- S³ coordinates + Hilbert indices
```

## Key Concepts
- **Semantic proximity = ELO score**, NOT geometric distance
- **Spatial index = efficiency tool**, not semantic measure
- **Intelligence = graph navigation**, not coordinate search
- **Evidence table = source of truth** for ELO + GDPR surgical deletion

## Essential Queries

### High-ELO relations from query
```sql
SELECT r.id, rr.elo_score, re.source_description
FROM hartonomous.relation r
JOIN hartonomous.relation_rating rr ON r.rating_id = rr.id
JOIN hartonomous.relation_evidence re ON r.id = re.relation_id
WHERE r.composition_a_id = :composition_id
ORDER BY rr.elo_score DESC LIMIT 20;
```

### Decompose composition to atoms
```sql
SELECT a.codepoint, cs.ordinal
FROM hartonomous.composition_sequence cs
JOIN hartonomous.atom a ON cs.atom_id = a.id
WHERE cs.composition_id = :target ORDER BY cs.ordinal;
```

### Spatial proximity (O(log N) via GiST)
```sql
SELECT c.id, p.centroid <=> ST_MakePoint(:x,:y,:z,:w) AS dist
FROM hartonomous.composition c
JOIN hartonomous.physicality p ON c.physicality_id = p.id
ORDER BY dist LIMIT 100;
```

### Multi-hop path (graph reasoning)
```sql
WITH RECURSIVE path AS (
    SELECT r.composition_b_id AS to_id, rr.elo_score, 1 AS depth, ARRAY[r.id] AS visited
    FROM hartonomous.relation r JOIN hartonomous.relation_rating rr ON r.rating_id = rr.id
    WHERE r.composition_a_id = :start AND rr.elo_score > :min_elo
    UNION ALL
    SELECT r.composition_b_id, rr.elo_score, p.depth + 1, p.visited || r.id
    FROM path p JOIN hartonomous.relation r ON p.to_id = r.composition_a_id
    JOIN hartonomous.relation_rating rr ON r.rating_id = rr.id
    WHERE p.depth < :max_depth AND rr.elo_score > :min_elo AND NOT (r.id = ANY(p.visited))
)
SELECT * FROM path ORDER BY depth, elo_score DESC;
```

### Surgical deletion (GDPR)
```sql
DELETE FROM hartonomous.relation_evidence WHERE content_id = :source_content_id;
-- Then recalculate ELO and prune orphaned relations
```

**Rule**: Heavy graph traversal and ELO computation should happen in C++ Engine via `hartonomous.so`, not in PL/pgSQL.