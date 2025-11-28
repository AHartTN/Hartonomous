# Relations

**Semantic connections: How atoms relate, Hebbian learning, and the knowledge graph.**

---

## What Are Relations?

**Relations** define semantic connections between atoms via typed, weighted edges.

```sql
Source Atom --[Relation Type, Weight]--> Target Atom
```

**Examples:**
- `'machine'` --[semantic_pair, 0.95]--> `'learning'`
- `'cat'` --[semantic_similar, 0.70]--> `'dog'`
- `QueryAtom` --[produced_result, 1.0]--> `ResultAtom`
- `NeuronA` --[temporal_precedes, 0.80]--> `NeuronB`

**Key principle:** Relations capture **how atoms connect semantically**, forming a knowledge graph.

---

## Relation Table Schema

### Complete DDL

```sql
CREATE TABLE atom_relation (
    -- Identity
    relation_id BIGSERIAL PRIMARY KEY,
    
    -- Source and target atoms
    source_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    target_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    
    -- Relation type is itself an atom!
    relation_type_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE RESTRICT,
    
    -- Synaptic weights (Hebbian learning)
    weight REAL NOT NULL DEFAULT 0.5 CHECK (weight >= 0.0 AND weight <= 1.0),
    confidence REAL NOT NULL DEFAULT 0.5 CHECK (confidence >= 0.0 AND confidence <= 1.0),
    importance REAL NOT NULL DEFAULT 0.5 CHECK (importance >= 0.0 AND importance <= 1.0),
    
    -- Geometric path through semantic space
    spatial_expression GEOMETRY(LINESTRINGZ, 0),
    
    -- Flexible metadata
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
    -- Tracking
    last_accessed TIMESTAMPTZ NOT NULL DEFAULT now(),
    
    -- Temporal versioning
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz,
    
    -- Uniqueness: one relation of each type between same atoms
    UNIQUE (source_atom_id, target_atom_id, relation_type_id)
);

-- Critical indexes
CREATE INDEX idx_relation_source ON atom_relation(source_atom_id);
CREATE INDEX idx_relation_target ON atom_relation(target_atom_id);
CREATE INDEX idx_relation_weight ON atom_relation(weight DESC);
CREATE INDEX idx_relation_type ON atom_relation(relation_type_id);
```

### Field Descriptions

| Field | Type | Purpose |
|-------|------|---------|
| `relation_id` | BIGSERIAL | Unique identifier |
| `source_atom_id` | BIGINT | Origin atom of relation |
| `target_atom_id` | BIGINT | Destination atom of relation |
| `relation_type_id` | BIGINT | Type of relation (itself an atom!) |
| `weight` | REAL | Synaptic efficacy (0.0-1.0) |
| `confidence` | REAL | Confidence in relation (0.0-1.0) |
| `importance` | REAL | Importance/relevance (0.0-1.0) |
| `spatial_expression` | GEOMETRY(LINESTRINGZ) | Geometric path (source ? target) |
| `last_accessed` | TIMESTAMPTZ | Last traversal time (for decay) |

---

## Relation Types

### Relation Types Are Atoms

**Key insight:** Relation types are stored in the `atom` table.

```sql
-- Create relation type atoms
INSERT INTO atom (content_hash, canonical_text, metadata)
VALUES
    (sha256('semantic_pair'), 'semantic_pair', '{"type":"relation_type"}'),
    (sha256('semantic_similar'), 'semantic_similar', '{"type":"relation_type"}'),
    (sha256('causes'), 'causes', '{"type":"relation_type"}'),
    (sha256('produced_result'), 'produced_result', '{"type":"relation_type"}');
```

**Benefits:**
- Relation types are first-class entities
- Can have their own relations (meta-relations)
- Can be positioned in semantic space
- Can be queried like any atom

### Common Relation Types

| Type | Meaning | Example |
|------|---------|---------|
| **semantic_pair** | Strong co-occurrence | `'machine'` ? `'learning'` |
| **semantic_similar** | Semantic similarity | `'cat'` ? `'dog'` |
| **semantic_opposite** | Antonym | `'hot'` ? `'cold'` |
| **causes** | Causal relationship | `'rain'` ? `'wet'` |
| **produced_result** | Provenance | `QueryAtom` ? `ResultAtom` |
| **temporal_precedes** | Temporal order | `EventA` ? `EventB` |
| **is_a** | Taxonomy | `'dog'` ? `'animal'` |
| **has_a** | Composition | `'car'` ? `'wheel'` |
| **part_of** | Membership | `'wheel'` ? `'car'` |

---

## Hebbian Learning

### "Neurons That Fire Together, Wire Together"

**Principle:** Relations strengthen with repeated co-occurrence.

```sql
-- Initial relation
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight)
VALUES ($machine_id, $learning_id, $semantic_pair_id, 0.5);
-- weight = 0.5 (default)

-- After co-occurrence #1
UPDATE atom_relation
SET weight = weight * 1.1, last_accessed = now()
WHERE source_atom_id = $machine_id AND target_atom_id = $learning_id;
-- weight = 0.55

-- After co-occurrence #2
UPDATE atom_relation
SET weight = weight * 1.1, last_accessed = now()
WHERE source_atom_id = $machine_id AND target_atom_id = $learning_id;
-- weight = 0.605

-- After 10 co-occurrences
-- weight ? 0.95 (strong connection)
```

**Formula:** `new_weight = min(1.0, old_weight × (1.0 + learning_rate))`

### Synaptic Decay

**Unused relations weaken over time:**

```sql
-- Decay relations not accessed in 30 days
UPDATE atom_relation
SET weight = weight * 0.9
WHERE last_accessed < now() - interval '30 days'
  AND weight > 0.1;

-- Delete very weak relations
DELETE FROM atom_relation
WHERE weight < 0.05;
```

**This implements synaptic pruning** (forget irrelevant connections).

---

## Weights, Confidence, Importance

### Weight (Synaptic Efficacy)

**Meaning:** Strength of connection (how often atoms co-occur).

```sql
-- Strong relation
weight = 0.95  -- "machine" + "learning" co-occur frequently

-- Weak relation
weight = 0.15  -- Rarely co-occur
```

**Usage:** Ranking, filtering, traversal decisions.

### Confidence (Certainty)

**Meaning:** How certain we are about this relation.

```sql
-- High confidence (observed 1000+ times)
confidence = 0.98

-- Low confidence (observed 2 times, might be noise)
confidence = 0.20
```

**Usage:** Filter unreliable relations.

### Importance (Relevance)

**Meaning:** How important this relation is (domain-specific).

```sql
-- Important for query context
importance = 0.90

-- Tangential relation
importance = 0.30
```

**Usage:** Prioritize relevant relations.

### Combined Scoring

```sql
-- Rank relations by combined score
SELECT 
    t.canonical_text AS target,
    ar.weight,
    ar.confidence,
    ar.importance,
    (ar.weight * 0.4 + ar.confidence * 0.3 + ar.importance * 0.3) AS combined_score
FROM atom_relation ar
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ar.source_atom_id = $source_id
ORDER BY combined_score DESC;
```

---

## Spatial Expressions

### Geometric Paths

`spatial_expression` = LINESTRING from source to target in 3D space.

```sql
-- Create relation with geometric path
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight, spatial_expression)
VALUES (
    $machine_id,
    $learning_id,
    $semantic_pair_id,
    0.95,
    ST_MakeLine(
        (SELECT spatial_key FROM atom WHERE atom_id = $machine_id),
        (SELECT spatial_key FROM atom WHERE atom_id = $learning_id)
    )
);
```

**Visualize as:**
```
'machine' (0.521, 0.845, 1.199)
    |
    | LINESTRING (geometric path)
    ?
'learning' (0.519, 0.843, 1.197)
```

### Query by Path Length

```sql
-- Find short paths (close atoms)
SELECT 
    s.canonical_text AS source,
    t.canonical_text AS target,
    ST_Length(ar.spatial_expression) AS path_length
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ST_Length(ar.spatial_expression) < 0.1
ORDER BY path_length ASC;

-- Result: Atoms very close in space (strong semantic similarity)
```

---

## Querying Relations

### Outgoing Relations (Source ? Targets)

```sql
-- What is "machine" related to?
SELECT 
    t.canonical_text AS target,
    rt.canonical_text AS relation_type,
    ar.weight,
    ar.confidence
FROM atom_relation ar
JOIN atom t ON t.atom_id = ar.target_atom_id
JOIN atom rt ON rt.atom_id = ar.relation_type_id
WHERE ar.source_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'machine')
ORDER BY ar.weight DESC;

-- Result:
-- learning  | semantic_pair     | 0.95 | 0.98
-- automation| semantic_related  | 0.72 | 0.85
-- computer  | semantic_similar  | 0.68 | 0.80
```

### Incoming Relations (Sources ? Target)

```sql
-- What relates to "learning"?
SELECT 
    s.canonical_text AS source,
    rt.canonical_text AS relation_type,
    ar.weight
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom rt ON rt.atom_id = ar.relation_type_id
WHERE ar.target_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'learning')
ORDER BY ar.weight DESC;

-- Result:
-- machine   | semantic_pair    | 0.95
-- deep      | semantic_related | 0.78
-- supervised| semantic_related | 0.65
```

### Bidirectional Relations

```sql
-- Find all atoms connected to "machine" (either direction)
SELECT DISTINCT
    CASE 
        WHEN ar.source_atom_id = $machine_id THEN t.canonical_text
        ELSE s.canonical_text
    END AS related_atom,
    ar.weight
FROM atom_relation ar
JOIN atom s ON s.atom_id = ar.source_atom_id
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ar.source_atom_id = $machine_id OR ar.target_atom_id = $machine_id
ORDER BY ar.weight DESC;
```

---

## Graph Traversal

### Single Hop

```sql
SELECT t.canonical_text
FROM atom_relation ar
JOIN atom t ON t.atom_id = ar.target_atom_id
WHERE ar.source_atom_id = $start_id
  AND ar.weight > 0.5;
```

### Multi-Hop (Recursive)

```sql
-- Find all atoms reachable within 3 hops
WITH RECURSIVE path AS (
    -- Base: Start atom
    SELECT 
        atom_id,
        canonical_text,
        0 AS depth,
        ARRAY[atom_id] AS path_ids
    FROM atom
    WHERE canonical_text = 'machine'
    
    UNION ALL
    
    -- Recursive: Follow relations
    SELECT 
        a.atom_id,
        a.canonical_text,
        p.depth + 1,
        p.path_ids || a.atom_id
    FROM path p
    JOIN atom_relation ar ON ar.source_atom_id = p.atom_id
    JOIN atom a ON a.atom_id = ar.target_atom_id
    WHERE p.depth < 3  -- Max 3 hops
      AND ar.weight > 0.5  -- Strong relations only
      AND a.atom_id != ALL(p.path_ids)  -- Avoid cycles
)
SELECT DISTINCT canonical_text, depth
FROM path
ORDER BY depth, canonical_text;

-- Result:
-- machine (depth=0)
-- learning, automation, computer (depth=1)
-- deep, neural, algorithm (depth=2)
-- network, layer, training (depth=3)
```

### Shortest Path

```sql
-- Find shortest path from "machine" to "neural"
WITH RECURSIVE path AS (
    SELECT 
        atom_id,
        canonical_text,
        0 AS depth,
        ARRAY[atom_id] AS path_ids,
        ARRAY[canonical_text] AS path_text
    FROM atom
    WHERE canonical_text = 'machine'
    
    UNION ALL
    
    SELECT 
        a.atom_id,
        a.canonical_text,
        p.depth + 1,
        p.path_ids || a.atom_id,
        p.path_text || a.canonical_text
    FROM path p
    JOIN atom_relation ar ON ar.source_atom_id = p.atom_id
    JOIN atom a ON a.atom_id = ar.target_atom_id
    WHERE p.depth < 10
      AND a.atom_id != ALL(p.path_ids)
      AND a.canonical_text != 'neural'  -- Stop when reached
)
SELECT path_text, depth
FROM path
WHERE canonical_text = 'neural'
ORDER BY depth ASC
LIMIT 1;

-- Result: ['machine', 'learning', 'deep', 'neural'] (3 hops)
```

---

## Combining Relations + Spatial Queries

### Semantic Expansion

**Query:** Find atoms near "learning" that are strongly related to "machine"

```sql
WITH machine_relations AS (
    SELECT target_atom_id, weight
    FROM atom_relation
    WHERE source_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'machine')
      AND weight > 0.6
),
learning_spatial AS (
    SELECT atom_id, canonical_text
    FROM atom
    WHERE ST_DWithin(
        spatial_key,
        (SELECT spatial_key FROM atom WHERE canonical_text = 'learning'),
        0.15
    )
)
SELECT 
    ls.canonical_text,
    mr.weight AS relation_weight,
    ST_Distance(
        ls.spatial_key,
        (SELECT spatial_key FROM atom WHERE canonical_text = 'learning')
    ) AS spatial_distance
FROM learning_spatial ls
JOIN machine_relations mr ON mr.target_atom_id = ls.atom_id
ORDER BY (mr.weight * 0.7 + (1.0 / (1.0 + spatial_distance)) * 0.3) DESC;

-- Combines: Spatial proximity + Relation strength
```

---

## Relation Metadata

### Provenance Tracking

```json
{
  "created_by": "user_query",
  "source_document": "doc_12345",
  "extraction_method": "co-occurrence",
  "co_occurrence_count": 127,
  "first_seen": "2025-11-01T00:00:00Z",
  "last_reinforced": "2025-11-28T12:00:00Z"
}
```

### Temporal Information

```json
{
  "valid_from_date": "2020-01-01",
  "valid_to_date": "2023-12-31",
  "temporal_context": "historical"
}
```

### Domain-Specific

```json
{
  "domain": "machine_learning",
  "confidence_source": "expert_labeled",
  "contradiction_count": 0
}
```

---

## Common Patterns

### Upsert Relation

```sql
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight)
VALUES ($source, $target, $type, 0.5)
ON CONFLICT (source_atom_id, target_atom_id, relation_type_id) DO UPDATE SET
    weight = LEAST(1.0, atom_relation.weight * 1.1),  -- Strengthen
    last_accessed = now(),
    metadata = atom_relation.metadata || EXCLUDED.metadata;
```

### Batch Create Relations

```sql
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight)
SELECT 
    $source_id,
    neighbor.atom_id,
    $semantic_neighbor_type_id,
    1.0 / (1.0 + ST_Distance($source_position, neighbor.spatial_key))  -- Inverse distance
FROM atom neighbor
WHERE ST_DWithin(neighbor.spatial_key, $source_position, 0.2)
  AND neighbor.atom_id != $source_id
ORDER BY ST_Distance(neighbor.spatial_key, $source_position) ASC
LIMIT 100;
```

### Prune Weak Relations

```sql
DELETE FROM atom_relation
WHERE weight < 0.1
  AND last_accessed < now() - interval '90 days';
```

### Find Mutual Relations

```sql
-- Atoms that relate to each other (bidirectional)
SELECT 
    a.canonical_text AS atom_a,
    b.canonical_text AS atom_b,
    r1.weight AS a_to_b_weight,
    r2.weight AS b_to_a_weight
FROM atom_relation r1
JOIN atom_relation r2 ON 
    r2.source_atom_id = r1.target_atom_id 
    AND r2.target_atom_id = r1.source_atom_id
JOIN atom a ON a.atom_id = r1.source_atom_id
JOIN atom b ON b.atom_id = r1.target_atom_id
WHERE r1.relation_type_id = r2.relation_type_id
ORDER BY (r1.weight + r2.weight) DESC;
```

---

## Performance Characteristics

### Index Usage

**Outgoing relations:**
```sql
-- Uses idx_relation_source
SELECT * FROM atom_relation WHERE source_atom_id = $id;
-- O(log N)
```

**Incoming relations:**
```sql
-- Uses idx_relation_target
SELECT * FROM atom_relation WHERE target_atom_id = $id;
-- O(log N)
```

**Strong relations:**
```sql
-- Uses idx_relation_weight
SELECT * FROM atom_relation WHERE weight > 0.8 ORDER BY weight DESC;
-- O(log N)
```

### Recursive Query Limits

```sql
-- Set max recursion depth
SET max_recursion_depth = 50;

-- Limit results
LIMIT 1000;

-- Break on target found
WHERE canonical_text != $target
```

---

## Key Takeaways

### 1. Relations = Semantic Graph

Atoms connected via typed, weighted edges forming knowledge graph.

### 2. Hebbian Learning

Weights strengthen with co-occurrence ("fire together, wire together").

### 3. Relation Types Are Atoms

First-class entities, can have their own relations and positions.

### 4. Three Weights

- `weight`: Synaptic efficacy (connection strength)
- `confidence`: Certainty in relation
- `importance`: Relevance/priority

### 5. Spatial Expressions

Geometric paths through semantic space (LINESTRING).

### 6. Graph Traversal

Multi-hop queries via recursive CTEs (shortest path, reachability).

### 7. Combined Queries

Spatial proximity + Relation strength = Rich semantic search.

---

## Next Steps

Now that you understand relations, continue with:

1. **[Spatial Semantics](spatial-semantics.md)** — How positions are computed
2. **[Compression](compression.md)** — Multi-layer encoding
3. **[Provenance](provenance.md)** — Neo4j tracking

---

**Next: [Spatial Semantics ?](spatial-semantics.md)**
