---
name: semantic-sql-authoring
description: Author SQL queries for Hartonomous relationship graph (Atoms/Compositions/Relations). Focus on ELO-weighted path queries and spatial indexing for navigation.
---

# Semantic SQL Authoring: Querying the Intelligence Graph

This skill provides SQL patterns for querying the Hartonomous substrate where intelligence = ELO-weighted relationship graph.

## Core Schema

```
hartonomous.atom           -- ~1.114M Unicode codepoints with S³ positions (IMMUTABLE)
hartonomous.composition    -- N-grams of Atoms (sequences)
hartonomous.relation       -- Co-occurrence patterns (WHERE INTELLIGENCE EMERGES)
hartonomous.relation_rating      -- ELO scores (semantic proximity, NOT geometric)
hartonomous.relation_evidence    -- Provenance (which ingest created this relation)
hartonomous.physicality    -- S³ geometric coordinates + Hilbert indices
```

## Essential Query Patterns

### 1. Find High-ELO Relations from Query
**Intelligence = High-ELO paths from query compositions.**
```sql
-- Given a query string, find strongest relationships
WITH query_comps AS (
    SELECT c.id AS composition_id
    FROM hartonomous.composition c
    WHERE c.hash = hartonomous.blake3(:query_text) -- Find composition by content
)
SELECT 
    r.id AS relation_id,
    r.composition_a_id,
    r.composition_b_id,
    rr.elo_score,
    re.source_description
FROM hartonomous.relation r
JOIN hartonomous.relation_rating rr ON r.rating_id = rr.id
JOIN hartonomous.relation_evidence re ON r.id = re.relation_id
WHERE r.composition_a_id IN (SELECT composition_id FROM query_comps)
ORDER BY rr.elo_score DESC
LIMIT 20;
```

### 2. Decompose Composition to Atoms
Reconstruct original Unicode sequence.
```sql
SELECT 
    a.codepoint,
    a.character,
    cs.ordinal
FROM hartonomous.composition_sequence cs
JOIN hartonomous.atom a ON cs.atom_id = a.id
WHERE cs.composition_id = :target_composition_id
ORDER BY cs.ordinal;
```

### 3. Spatial Proximity Search (O(log N) via PostGIS + Hilbert)
**Remember**: Geometric proximity ≠ semantic relatedness. Use spatial index for efficient graph access, not semantic similarity.
```sql
-- Find compositions near a specific S³ point (for graph navigation efficiency)
SELECT 
    c.id,
    c.hash,
    p.centroid,
    ST_Distance(p.centroid::geometry, ST_GeomFromText(:query_point_wkt)::geometry) AS geom_dist
FROM hartonomous.composition c
JOIN hartonomous.physicality p ON c.physicality_id = p.id
WHERE ST_DWithin(p.centroid::geometry, ST_GeomFromText(:query_point_wkt)::geometry, :radius)
ORDER BY p.centroid <=> ST_GeomFromText(:query_point_wkt)::geometry
LIMIT 100;
```

### 4. Multi-Hop Relationship Path
Explore relationship graph (reasoning).
```sql
WITH RECURSIVE path AS (
    -- Start from query composition
    SELECT 
        r.composition_a_id AS from_id,
        r.composition_b_id AS to_id,
        rr.elo_score,
        1 AS depth,
        ARRAY[r.id] AS relation_path
    FROM hartonomous.relation r
    JOIN hartonomous.relation_rating rr ON r.rating_id = rr.id
    WHERE r.composition_a_id = :start_composition_id
      AND rr.elo_score > :min_elo_threshold
    
    UNION ALL
    
    -- Follow high-ELO relations
    SELECT 
        r.composition_a_id,
        r.composition_b_id,
        rr.elo_score,
        p.depth + 1,
        p.relation_path || r.id
    FROM path p
    JOIN hartonomous.relation r ON p.to_id = r.composition_a_id
    JOIN hartonomous.relation_rating rr ON r.rating_id = rr.id
    WHERE p.depth < :max_depth
      AND rr.elo_score > :min_elo_threshold
      AND NOT (r.id = ANY(p.relation_path)) -- Prevent cycles
)
SELECT * FROM path
ORDER BY depth, elo_score DESC;
```

### 5. Reconstruct Content (Dense Storage Mode)
**For content requiring bit-perfect reconstruction (documents, images, user data).**
```sql
-- Reconstruct entire document from Relations → Compositions → Atoms → Unicode
WITH RECURSIVE content_relations AS (
    -- Get all relations for this content
    SELECT r.id, r.composition_a_id, r.composition_b_id
    FROM hartonomous.relation r
    JOIN hartonomous.relation_evidence re ON r.id = re.relation_id
    WHERE re.content_id = :content_id
),
content_compositions AS (
    -- Get all compositions referenced
    SELECT DISTINCT composition_id
    FROM (
        SELECT composition_a_id AS composition_id FROM content_relations
        UNION
        SELECT composition_b_id AS composition_id FROM content_relations
    ) AS all_comps
),
composition_atoms AS (
    -- Decompose each composition to atoms
    SELECT 
        cs.composition_id,
        a.codepoint,
        cs.ordinal,
        cs.occurrence_offset
    FROM hartonomous.composition_sequence cs
    JOIN hartonomous.atom a ON cs.atom_id = a.id
    WHERE cs.composition_id IN (SELECT composition_id FROM content_compositions)
    ORDER BY cs.composition_id, cs.ordinal
)
-- Reconstruct Unicode string
SELECT string_agg(chr(codepoint), '' ORDER BY ordinal) AS reconstructed_content
FROM composition_atoms;

-- Validation: hash output and compare to content.hash
```

### 6. Inspect Relation Evidence (Auditability)
**Why is this relation strong? Where did we observe it?**
```sql
SELECT 
    c.source_type,
    c.source_identifier,
    re.weight,
    re.position,
    re.context,
    re.timestamp,
    rr.elo_score
FROM hartonomous.relation_evidence re
JOIN hartonomous.content c ON re.content_id = c.content_id
JOIN hartonomous.relation_rating rr ON re.relation_id = rr.relation_id
WHERE re.relation_id = :target_relation_id
ORDER BY re.timestamp DESC;

-- Example output:
-- source_type | source_identifier | weight | position        | context           | elo_score
-- model       | bert-base         | 0.82   | layer_7_head_3  | attention matrix  | 2035
-- model       | gpt-3             | 0.91   | layer_42_head_8 | attention matrix  | 2035
-- text        | moby_dick.txt     | 0.95   | paragraph_142   | co-occurrence     | 2035
```

### 7. Surgical Deletion (GDPR Compliance)
**Remove knowledge from specific sources without retraining.**
```sql
-- Delete all evidence from a specific source
DELETE FROM hartonomous.relation_evidence
WHERE content_id = (
    SELECT content_id FROM hartonomous.content
    WHERE source_identifier = :source_to_remove
);

-- Recalculate ELO from remaining evidence
UPDATE hartonomous.relation_rating rr
SET elo_score = hartonomous.calculate_elo_from_evidence(rr.relation_id),
    last_updated = NOW()
WHERE rr.relation_id IN (
    SELECT DISTINCT relation_id 
    FROM hartonomous.relation_evidence
);

-- Prune orphaned relations (no evidence remaining)
DELETE FROM hartonomous.relation r
WHERE NOT EXISTS (
    SELECT 1 FROM hartonomous.relation_evidence re
    WHERE re.relation_id = r.id
);

-- Intelligence automatically adapts: paths re-route, ELO scores recalculate
```

## Critical Concepts

- **Semantic Proximity = ELO Score**, NOT geometric distance
- **Spatial Index = Efficiency Tool**, not semantic measure
- **Intelligence = Graph Navigation**, not coordinate search
- **Dense Storage = Bit-Perfect Reconstruction** (documents, images, user data)
- **Sparse Storage = Relationship Extraction Only** (AI models)
- **Evidence Table = Source of Truth** for ELO calculation + surgical deletion
- Use PostGIS for O(log N) access, then navigate via ELO weights