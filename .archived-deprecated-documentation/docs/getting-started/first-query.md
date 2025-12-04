# First Query Tutorial

**Learn semantic search with spatial queries.**

---

## Overview

In this tutorial, you'll:
1. Query the semantic space
2. Understand K-nearest neighbors
3. Traverse relations
4. Combine spatial + graph queries
5. Understand distance metrics

**Time:** ~10 minutes

---

## Prerequisites

- Hartonomous running
- At least one document ingested ([First Ingestion](first-ingestion.md))
- PostgreSQL client (optional)

---

## Step 1: Basic Semantic Query

### Via API

**Query for atoms near "learning":**

```bash
curl "http://localhost/v1/query/semantic?text=learning&limit=10"
```

**Response:**

```json
{
  "query": "learning",
  "query_position": [0.519, 0.843, 1.197],
  "results": [
    {
      "atom_id": 12350,
      "canonical_text": "learning",
      "distance": 0.00,
      "reference_count": 3,
      "spatial_position": [0.519, 0.843, 1.197]
    },
    {
      "atom_id": 12349,
      "canonical_text": "machine",
      "distance": 0.042,
      "reference_count": 1,
      "spatial_position": [0.521, 0.845, 1.199]
    },
    {
      "atom_id": 12357,
      "canonical_text": "deep",
      "distance": 0.078,
      "reference_count": 1,
      "spatial_position": [0.515, 0.840, 1.194]
    },
    {
      "atom_id": 12355,
      "canonical_text": "intelligence",
      "distance": 0.083,
      "reference_count": 1,
      "spatial_position": [0.518, 0.842, 1.196]
    },
    {
      "atom_id": 12352,
      "canonical_text": "data",
      "distance": 0.124,
      "reference_count": 1,
      "spatial_position": [0.515, 0.840, 1.194]
    }
  ],
  "execution_time_ms": 4
}
```

**What happened:**

1. Query text `"learning"` atomized
2. Query atom looked up in database
3. Spatial position retrieved: `[0.519, 0.843, 1.197]`
4. K-nearest neighbors query executed (K=10)
5. Results ordered by distance (ascending)
6. Execution time: 4ms (PostGIS GiST index)

**Key observations:**
- Exact match (`"learning"`) has `distance=0.00`
- Related words cluster nearby (small distances)
- No embedding model used — positions from ingestion
- Fast query (4ms) via spatial index

---

## Step 2: Direct SQL Query

### Connect to PostgreSQL

```bash
docker-compose exec postgres psql -U hartonomous -d hartonomous
```

### K-Nearest Neighbors Query

```sql
-- Find 10 nearest atoms to "learning"
WITH target AS (
    SELECT atom_id, spatial_key
    FROM atom
    WHERE canonical_text = 'learning'
)
SELECT 
    a.atom_id,
    a.canonical_text,
    ST_Distance(a.spatial_key, t.spatial_key) AS distance,
    a.reference_count,
    ST_AsText(a.spatial_key) AS position
FROM atom a, target t
WHERE a.canonical_text ~ '^[a-z]+$'  -- Text atoms only
  AND a.atom_id != t.atom_id         -- Exclude self
ORDER BY ST_Distance(a.spatial_key, t.spatial_key) ASC
LIMIT 10;
```

**Result:**

```
 atom_id | canonical_text | distance | reference_count |          position
---------+----------------+----------+-----------------+---------------------------
   12349 | machine        |    0.042 |               1 | POINT Z(0.521 0.845 1.199)
   12357 | deep           |    0.078 |               1 | POINT Z(0.515 0.840 1.194)
   12355 | intelligence   |    0.083 |               1 | POINT Z(0.518 0.842 1.196)
   12352 | data           |    0.124 |               1 | POINT Z(0.515 0.840 1.194)
   12360 | neural         |    0.156 |               1 | POINT Z(0.512 0.838 1.192)
   12362 | networks       |    0.189 |               1 | POINT Z(0.510 0.836 1.190)
```

---

## Step 3: Radius Search

**Find all atoms within distance 0.15 of "learning":**

```sql
WITH target AS (
    SELECT spatial_key FROM atom WHERE canonical_text = 'learning'
)
SELECT 
    a.canonical_text,
    ST_Distance(a.spatial_key, t.spatial_key) AS distance,
    a.reference_count
FROM atom a, target t
WHERE a.canonical_text ~ '^[a-z]+$'
  AND ST_DWithin(a.spatial_key, t.spatial_key, 0.15)  -- Radius 0.15
ORDER BY distance ASC;
```

**Uses PostGIS `ST_DWithin` — optimized spatial index scan.**

**Result:**

```
 canonical_text | distance | reference_count
----------------+----------+-----------------
 learning       |    0.000 |               3
 machine        |    0.042 |               1
 deep           |    0.078 |               1
 intelligence   |    0.083 |               1
 data           |    0.124 |               1
```

**Performance:**
- GiST index prunes search space
- O(log N) instead of O(N) scan
- Fast even with millions of atoms

---

## Step 4: Bounding Box Search

**Find atoms in a 3D box region:**

```sql
SELECT 
    canonical_text,
    ST_X(spatial_key) AS x,
    ST_Y(spatial_key) AS y,
    ST_Z(spatial_key) AS z,
    reference_count
FROM atom
WHERE spatial_key && ST_MakeEnvelope(
    0.5, 0.8, 1.1,  -- Min X, Y, Z
    0.6, 0.9, 1.3   -- Max X, Y, Z
)
  AND canonical_text ~ '^[a-z]+$'
ORDER BY canonical_text;
```

**Use case:** Find all atoms in "AI/ML" semantic region.

---

## Step 5: Combine Spatial + Relation Queries

**Find atoms near "learning" that are related to "machine":**

```sql
WITH learning_neighbors AS (
    SELECT 
        a.atom_id,
        a.canonical_text,
        ST_Distance(
            a.spatial_key,
            (SELECT spatial_key FROM atom WHERE canonical_text = 'learning')
        ) AS dist_to_learning
    FROM atom a
    WHERE a.canonical_text ~ '^[a-z]+$'
      AND ST_DWithin(
          a.spatial_key,
          (SELECT spatial_key FROM atom WHERE canonical_text = 'learning'),
          0.2
      )
),
machine_relations AS (
    SELECT target_atom_id, weight
    FROM atom_relation
    WHERE source_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'machine')
)
SELECT 
    ln.canonical_text,
    ln.dist_to_learning,
    mr.weight AS relation_weight
FROM learning_neighbors ln
JOIN machine_relations mr ON mr.target_atom_id = ln.atom_id
ORDER BY ln.dist_to_learning ASC;
```

**Result:**

```
 canonical_text | dist_to_learning | relation_weight
----------------+------------------+-----------------
 learning       |            0.000 |            0.95
 data           |            0.124 |            0.68
 intelligence   |            0.083 |            0.72
```

**This combines:**
- Spatial proximity (near "learning")
- Semantic relations (related to "machine")
- **= Multi-faceted semantic search**

---

## Step 6: Traverse Relation Graph

**Find all atoms reachable from "machine" within 2 hops:**

```sql
WITH RECURSIVE relation_path AS (
    -- Base case: Start at "machine"
    SELECT 
        atom_id,
        canonical_text,
        0 AS depth
    FROM atom
    WHERE canonical_text = 'machine'
    
    UNION ALL
    
    -- Recursive case: Follow relations
    SELECT 
        a.atom_id,
        a.canonical_text,
        rp.depth + 1
    FROM relation_path rp
    JOIN atom_relation ar ON ar.source_atom_id = rp.atom_id
    JOIN atom a ON a.atom_id = ar.target_atom_id
    WHERE rp.depth < 2  -- Max 2 hops
      AND ar.weight > 0.5  -- Strong relations only
)
SELECT DISTINCT
    canonical_text,
    depth
FROM relation_path
ORDER BY depth, canonical_text;
```

**Result:**

```
 canonical_text | depth
----------------+-------
 machine        |     0
 learning       |     1
 data           |     1
 intelligence   |     1
 deep           |     2
 neural         |     2
```

**Use case:** "Expand query context" by following semantic connections.

---

## Step 7: Weighted Semantic Search

**Rank results by combined spatial + relation score:**

```sql
WITH target AS (
    SELECT atom_id, spatial_key FROM atom WHERE canonical_text = 'learning'
)
SELECT 
    a.canonical_text,
    ST_Distance(a.spatial_key, t.spatial_key) AS spatial_distance,
    COALESCE(ar.weight, 0.0) AS relation_weight,
    -- Combined score: closer in space + stronger relation = higher score
    (1.0 / (1.0 + ST_Distance(a.spatial_key, t.spatial_key))) * 0.5 +
    COALESCE(ar.weight, 0.0) * 0.5 AS combined_score
FROM atom a, target t
LEFT JOIN atom_relation ar ON ar.source_atom_id = t.atom_id AND ar.target_atom_id = a.atom_id
WHERE a.canonical_text ~ '^[a-z]+$'
  AND a.atom_id != t.atom_id
ORDER BY combined_score DESC
LIMIT 10;
```

**Result:**

```
 canonical_text | spatial_distance | relation_weight | combined_score
----------------+------------------+-----------------+----------------
 machine        |            0.042 |            0.95 |          0.968
 intelligence   |            0.083 |            0.72 |          0.817
 data           |            0.124 |            0.68 |          0.740
 deep           |            0.078 |            0.00 |          0.465
```

**"machine"** wins because:
- Close in space (`distance=0.042`)
- Strong relation (`weight=0.95`)

---

## Step 8: Multi-Modal Query

**Find atoms of ANY modality near "learning":**

```sql
WITH target AS (
    SELECT spatial_key FROM atom WHERE canonical_text = 'learning'
)
SELECT 
    a.canonical_text,
    a.metadata->>'modality' AS modality,
    ST_Distance(a.spatial_key, t.spatial_key) AS distance
FROM atom a, target t
WHERE ST_DWithin(a.spatial_key, t.spatial_key, 0.3)
ORDER BY distance ASC
LIMIT 20;
```

**If you had images/audio ingested:**

```
 canonical_text |  modality  | distance
----------------+------------+----------
 learning       | text       |    0.000
 machine        | text       |    0.042
 [image:ml.png] | image      |    0.087
 [audio:lec.mp3]| audio      |    0.145
```

**Cross-modal search works natively** — all modalities in same space!

---

## Step 9: Query Neo4j Provenance

### Via Cypher

**Find atoms derived from query result:**

```cypher
MATCH (result:Atom {canonical_text: 'machine'})<-[:DERIVED_FROM*]-(ancestor:Atom)
RETURN ancestor.canonical_text AS origin, count(*) AS descendant_count
ORDER BY descendant_count DESC
LIMIT 10;
```

**Shows:**
- What inputs produced "machine" atom
- How many atoms derived from it
- Complete derivation graph

### Combined SQL + Cypher

**SQL identifies atoms, Cypher traces provenance:**

1. **SQL**: Find atoms near query
2. **Extract** `atom_id` list
3. **Cypher**: Trace provenance for those IDs
4. **Result**: Semantic search + full lineage

---

## Step 10: Performance Analysis

### Query Execution Plan

```sql
EXPLAIN ANALYZE
WITH target AS (
    SELECT spatial_key FROM atom WHERE canonical_text = 'learning'
)
SELECT 
    a.canonical_text,
    ST_Distance(a.spatial_key, t.spatial_key) AS distance
FROM atom a, target t
WHERE ST_DWithin(a.spatial_key, t.spatial_key, 0.15)
ORDER BY distance ASC
LIMIT 10;
```

**Expected plan:**

```
Sort  (cost=... rows=10) (actual time=3.124..3.125 rows=5 loops=1)
  ->  Nested Loop  (cost=... rows=50) (actual time=0.156..3.089 rows=5 loops=1)
        ->  Index Scan using idx_atom_spatial on atom a  (cost=... rows=50)
              Index Cond: (spatial_key && ...)
              Filter: ST_DWithin(spatial_key, ...)
```

**Key observations:**
- **Index Scan** on `idx_atom_spatial` (GiST index)
- **NOT** Sequential Scan (O(log N) not O(N))
- **Actual time**: ~3ms for query
- **Rows scanned**: ~50 (not all atoms)

### Compare: With vs. Without Index

**Disable index:**
```sql
SET enable_indexscan = off;
SET enable_bitmapscan = off;

-- Re-run query
EXPLAIN ANALYZE ...;
```

**Result:**
- Sequential Scan (O(N))
- 1000x slower
- **Spatial index is critical for performance**

---

## Understanding Distance Metrics

### Euclidean Distance (Default)

```sql
SELECT ST_Distance(
    'POINT Z(0.5 0.8 1.2)',
    'POINT Z(0.6 0.9 1.3)'
) AS euclidean_distance;

-- Result: 0.173 (?((0.6-0.5)² + (0.9-0.8)² + (1.3-1.2)²))
```

### Distance = Semantic Dissimilarity

| Distance | Meaning |
|----------|---------|
| 0.00 - 0.05 | **Highly similar** (synonyms, variants) |
| 0.05 - 0.15 | **Similar** (related concepts) |
| 0.15 - 0.30 | **Somewhat related** (same domain) |
| 0.30 - 0.50 | **Weakly related** (different domains) |
| > 0.50 | **Unrelated** (no semantic connection) |

### Adjust Search Radius

```sql
-- Tight radius (only close matches)
WHERE ST_DWithin(spatial_key, $target, 0.05)

-- Medium radius (related concepts)
WHERE ST_DWithin(spatial_key, $target, 0.15)

-- Wide radius (broad search)
WHERE ST_DWithin(spatial_key, $target, 0.50)
```

---

## Key Takeaways

### 1. Spatial Queries = Semantic Search

- No embedding model needed
- Position = meaning
- Distance = dissimilarity

### 2. PostGIS Indexes Are Fast

- GiST index: O(log N)
- Sub-10ms queries
- Scales to millions of atoms

### 3. Relations Add Context

- Hebbian weights capture co-occurrence
- Combine spatial + graph for rich queries
- Multi-faceted ranking

### 4. Multi-Modal Works Natively

- All modalities in same space
- Text query returns images
- Cross-modal search "just works"

### 5. Provenance Is Queryable

- Neo4j tracks derivations
- SQL finds atoms ? Cypher traces lineage
- Complete audit trail

---

## Query Patterns Cheat Sheet

### K-Nearest Neighbors
```sql
ORDER BY ST_Distance(spatial_key, $target) ASC LIMIT K
```

### Radius Search
```sql
WHERE ST_DWithin(spatial_key, $target, $radius)
```

### Bounding Box
```sql
WHERE spatial_key && ST_MakeEnvelope($minX, $minY, $minZ, $maxX, $maxY, $maxZ)
```

### Combined Spatial + Relations
```sql
WITH neighbors AS (
    SELECT ... FROM atom WHERE ST_DWithin(...)
)
SELECT n.* FROM neighbors n
JOIN atom_relation ar ON ar.target_atom_id = n.atom_id
WHERE ar.weight > 0.5
```

### Relation Traversal
```sql
WITH RECURSIVE path AS (
    SELECT ... UNION ALL SELECT ...
)
SELECT * FROM path WHERE depth <= $max_hops
```

---

## What's Next?

You now understand semantic queries. Continue with:

1. **[Concepts: Spatial Semantics](../concepts/spatial-semantics.md)** — Deep dive into positioning
2. **[Concepts: Relations](../concepts/relations.md)** — Hebbian learning explained
3. **[API Reference: Query](../api-reference/query.md)** — All query endpoints

---

## Practice Queries

Try these on your own:

1. Find atoms within 0.1 distance of "data"
2. Query for atoms with `reference_count > 5` (common atoms)
3. Combine spatial search with metadata filters
4. Traverse 3-hop relation graph from "neural"
5. Find atoms in specific modality (text only, images only)

---

**Next: [Concepts Documentation](../concepts/README.md) ?**
