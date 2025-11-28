# First Ingestion Tutorial

**Deep dive: Ingest a document and inspect the atomization process.**

---

## Overview

In this tutorial, you'll:
1. Ingest a text document via API
2. Inspect created atoms in PostgreSQL
3. Examine spatial positioning
4. View composition hierarchy
5. Check Neo4j provenance graph

**Time:** ~10 minutes

---

## Prerequisites

- Hartonomous running (see [Quick Start](quick-start.md))
- `curl` or HTTP client
- PostgreSQL client (optional, for inspection)

---

## Step 1: Prepare Sample Document

Create a test document:

```bash
cat > sample.txt << 'EOF'
Machine learning is a subset of artificial intelligence.
It enables computers to learn from data without explicit programming.
Deep learning uses neural networks with multiple layers.
EOF
```

---

## Step 2: Ingest Document

**Via API:**

```bash
curl -X POST http://localhost/v1/ingest/text \
  -H "Content-Type: application/json" \
  -d @- << 'EOF'
{
  "content": "Machine learning is a subset of artificial intelligence. It enables computers to learn from data without explicit programming. Deep learning uses neural networks with multiple layers.",
  "metadata": {
    "source": "tutorial",
    "document_type": "educational",
    "author": "tutorial-user"
  }
}
EOF
```

**Response (example):**

```json
{
  "atom_id": 12345,
  "atoms_created": 183,
  "atoms_reused": 42,
  "compositions_created": 97,
  "relations_created": 23,
  "spatial_position": [0.523, 0.847, 1.201],
  "processing_time_ms": 127,
  "provenance_tracked": true,
  "stats": {
    "total_processed": 225,
    "atoms_created": 183,
    "atoms_reused": 42,
    "sparse_skipped": 0
  }
}
```

**What happened:**

1. **Text decomposed** into character atoms
2. **Content addressing**: Each character checked via `SHA-256(character)`
3. **Deduplication**: Common characters (`'e'`, `'t'`, `'a'`) reused
4. **Spatial positioning**: Each atom positioned via semantic neighbor averaging
5. **Composition hierarchy**: Document ? Sentences ? Words ? Characters
6. **Relations created**: Semantic connections between related atoms
7. **Neo4j sync**: Provenance graph updated asynchronously

---

## Step 3: Inspect Created Atoms

### Connect to PostgreSQL

```bash
docker-compose exec postgres psql -U hartonomous -d hartonomous
```

### Query Individual Atoms

**View character atoms:**

```sql
SELECT 
    atom_id,
    canonical_text,
    reference_count,
    ST_AsText(spatial_key) AS position,
    metadata
FROM atom
WHERE canonical_text ~ '^[a-z]$'  -- Single lowercase letters
ORDER BY reference_count DESC
LIMIT 10;
```

**Expected output:**

```
 atom_id | canonical_text | reference_count |        position         | metadata
---------+----------------+-----------------+-------------------------+----------
     101 | e              |              27 | POINT Z(0.12 0.34 0.56) | {...}
     116 | t              |              19 | POINT Z(0.13 0.36 0.57) | {...}
      97 | a              |              18 | POINT Z(0.11 0.33 0.55) | {...}
     105 | i              |              16 | POINT Z(0.14 0.35 0.58) | {...}
     114 | r              |              13 | POINT Z(0.15 0.37 0.59) | {...}
```

**Observations:**
- `'e'` has highest reference count (most common)
- Each character has unique `atom_id`
- `spatial_key` shows 3D position in semantic space
- `reference_count` = "atomic mass" (usage frequency)

### Query Word Atoms

```sql
SELECT 
    atom_id,
    canonical_text,
    reference_count,
    ST_AsText(spatial_key) AS position
FROM atom
WHERE canonical_text ~ '^[a-z]+$'  -- Multi-character words
  AND length(canonical_text) > 1
ORDER BY reference_count DESC
LIMIT 10;
```

**Expected output:**

```
 atom_id | canonical_text | reference_count |          position
---------+----------------+-----------------+---------------------------
   12350 | learning       |               3 | POINT Z(0.519 0.843 1.197)
   12349 | machine        |               1 | POINT Z(0.521 0.845 1.199)
   12355 | intelligence   |               1 | POINT Z(0.518 0.842 1.196)
   12352 | data           |               1 | POINT Z(0.515 0.840 1.194)
```

**Observations:**
- `'learning'` appears 3 times in document ? `reference_count=3`
- Words have **different spatial positions** than characters
- Positions cluster (similar Y/Z coordinates) ? semantic similarity

---

## Step 4: Examine Spatial Positioning

### How Are Positions Computed?

Each atom's position is the **weighted centroid** of its semantic neighbors.

**Query spatial neighbors of "learning":**

```sql
WITH target AS (
    SELECT atom_id, spatial_key
    FROM atom
    WHERE canonical_text = 'learning'
)
SELECT 
    a.canonical_text,
    ST_Distance(a.spatial_key, t.spatial_key) AS distance,
    a.reference_count
FROM atom a, target t
WHERE a.canonical_text ~ '^[a-z]+$'
  AND a.atom_id != t.atom_id
ORDER BY distance ASC
LIMIT 10;
```

**Expected output:**

```
 canonical_text | distance | reference_count
----------------+----------+-----------------
 machine        |    0.042 |               1
 deep           |    0.078 |               1
 intelligence   |    0.083 |               1
 data           |    0.124 |               1
 neural         |    0.156 |               1
 networks       |    0.189 |               1
```

**Observations:**
- `'machine'` is closest to `'learning'` (co-occur frequently)
- Distance = semantic dissimilarity
- Position emerged **without embedding model** (just from co-occurrence)

### Visualize Semantic Clusters

**Query atoms by spatial region:**

```sql
SELECT 
    canonical_text,
    ST_X(spatial_key) AS x,
    ST_Y(spatial_key) AS y,
    ST_Z(spatial_key) AS z,
    reference_count
FROM atom
WHERE canonical_text ~ '^[a-z]+$'
  AND length(canonical_text) > 3
ORDER BY ST_Y(spatial_key)  -- Sort by category axis
LIMIT 20;
```

**Export for visualization:**

```bash
docker-compose exec postgres psql -U hartonomous -d hartonomous -t -A -F"," \
  -c "SELECT canonical_text, ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key) 
      FROM atom 
      WHERE canonical_text ~ '^[a-z]+$' 
        AND length(canonical_text) > 3" \
  > atoms-3d.csv
```

**Plot with Python:**

```python
import pandas as pd
import matplotlib.pyplot as plt
from mpl_toolkits.mplot3d import Axes3D

# Load data
df = pd.read_csv('atoms-3d.csv', names=['word', 'x', 'y', 'z'])

# 3D scatter plot
fig = plt.figure(figsize=(12, 8))
ax = fig.add_subplot(111, projection='3d')
ax.scatter(df['x'], df['y'], df['z'])

# Label points
for _, row in df.iterrows():
    ax.text(row['x'], row['y'], row['z'], row['word'], size=8)

ax.set_xlabel('X (Modality)')
ax.set_ylabel('Y (Category)')
ax.set_zlabel('Z (Specificity)')
plt.title('Semantic Space - 3D Atom Positions')
plt.show()
```

---

## Step 5: View Composition Hierarchy

### Query Parent-Child Relationships

**Find what "learning" is composed of:**

```sql
WITH parent AS (
    SELECT atom_id FROM atom WHERE canonical_text = 'learning'
)
SELECT 
    ac.sequence_index,
    c.canonical_text AS component,
    c.atom_id
FROM atom_composition ac
JOIN atom c ON c.atom_id = ac.component_atom_id
JOIN parent p ON p.atom_id = ac.parent_atom_id
ORDER BY ac.sequence_index;
```

**Expected output:**

```
 sequence_index | component | atom_id
----------------+-----------+---------
              0 | l         |     108
              1 | e         |     101
              2 | a         |      97
              3 | r         |     114
              4 | n         |     110
              5 | i         |     105
              6 | n         |     110
              7 | g         |     103
```

**Observations:**
- Word `'learning'` = composition of 8 character atoms
- `sequence_index` preserves order
- Character `'n'` (atom_id=110) appears twice (index 4 and 6)

### Query Reverse Composition

**What uses character 'e'?**

```sql
WITH component AS (
    SELECT atom_id FROM atom WHERE canonical_text = 'e'
)
SELECT 
    p.canonical_text AS parent,
    COUNT(*) AS times_used,
    array_agg(ac.sequence_index ORDER BY ac.sequence_index) AS positions
FROM atom_composition ac
JOIN atom p ON p.atom_id = ac.parent_atom_id
JOIN component c ON c.atom_id = ac.component_atom_id
WHERE p.canonical_text ~ '^[a-z]+$'
GROUP BY p.canonical_text
ORDER BY times_used DESC
LIMIT 10;
```

**Expected output:**

```
   parent    | times_used |  positions
-------------+------------+-------------
 intelligence|          3 | {3,8,11}
 learning    |          1 | {1}
 enables     |          2 | {0,5}
 deep        |          1 | {1}
```

**Observations:**
- `'e'` appears in many words
- `positions` array shows where in parent
- This is **reference counting** in action

---

## Step 6: Check Relations

### Query Semantic Relations

**What is "learning" related to?**

```sql
WITH source AS (
    SELECT atom_id FROM atom WHERE canonical_text = 'learning'
)
SELECT 
    t.canonical_text AS target,
    rt.canonical_text AS relation_type,
    ar.weight,
    ar.confidence
FROM atom_relation ar
JOIN atom t ON t.atom_id = ar.target_atom_id
JOIN atom rt ON rt.atom_id = ar.relation_type_id
JOIN source s ON s.atom_id = ar.source_atom_id
ORDER BY ar.weight DESC
LIMIT 10;
```

**Expected output:**

```
   target    |  relation_type  | weight | confidence
-------------+-----------------+--------+------------
 machine     | semantic_pair   |   0.95 |       0.90
 deep        | semantic_assoc  |   0.78 |       0.75
 data        | semantic_assoc  |   0.65 |       0.70
 neural      | semantic_assoc  |   0.58 |       0.65
```

**Observations:**
- `'machine'` + `'learning'` have strongest relation (weight=0.95)
- Relation types are **themselves atoms** (`relation_type_id`)
- Weights strengthen with Hebbian learning (co-occurrence)

---

## Step 7: View Neo4j Provenance

### Open Neo4j Browser

```
http://localhost:7474
```

**Login:**
- Username: `neo4j`
- Password: `neo4jneo4j`

### Query Atom Provenance

**Find how "learning" was created:**

```cypher
MATCH path = (atom:Atom {canonical_text: 'learning'})-[:DERIVED_FROM*]->(origin:Atom)
RETURN path
LIMIT 25
```

**Visualize:**
- Central node: `'learning'` atom
- Connected nodes: Character atoms (`'l'`, `'e'`, `'a'`, etc.)
- Edges: `DERIVED_FROM` relationships
- Complete lineage from raw input to final atom

### Query Composition in Graph

```cypher
MATCH (parent:Atom {canonical_text: 'learning'})-[r:COMPOSED_OF]->(component:Atom)
RETURN parent, r, component
ORDER BY r.sequence_index
```

**Result:**
- Same composition as PostgreSQL
- But now as **graph structure** for traversal
- Full audit trail preserved

### Query Document Lineage

**Full document derivation:**

```cypher
MATCH path = (doc:Atom)-[:COMPOSED_OF*1..3]->(leaf:Atom)
WHERE doc.atom_id = 12345  // Your document atom_id
RETURN path
LIMIT 100
```

**Shows:**
- Document ? Sentences ? Words ? Characters
- Complete hierarchical breakdown
- Every atom traceable to origin

---

## Step 8: Performance Analysis

### Query Statistics

**How many atoms created vs. reused?**

```sql
SELECT 
    COUNT(*) FILTER (WHERE reference_count = 1) AS new_atoms,
    COUNT(*) FILTER (WHERE reference_count > 1) AS reused_atoms,
    COUNT(*) AS total_atoms,
    ROUND(AVG(reference_count), 2) AS avg_references
FROM atom
WHERE created_at > now() - interval '1 hour';  -- Recent atoms only
```

**Expected output:**

```
 new_atoms | reused_atoms | total_atoms | avg_references
-----------+--------------+-------------+----------------
       183 |           42 |         225 |           1.82
```

**Observations:**
- 81% new atoms (unique to this document)
- 19% reused (common characters like `'e'`, `'t'`)
- Average atom referenced 1.82 times

### Storage Efficiency

**Calculate deduplication savings:**

```sql
WITH stats AS (
    SELECT 
        SUM(length(atomic_value)) AS unique_bytes,
        SUM(length(atomic_value) * reference_count) AS total_bytes_without_dedup
    FROM atom
    WHERE created_at > now() - interval '1 hour'
)
SELECT 
    unique_bytes,
    total_bytes_without_dedup,
    total_bytes_without_dedup - unique_bytes AS bytes_saved,
    ROUND((total_bytes_without_dedup::NUMERIC - unique_bytes) / total_bytes_without_dedup * 100, 2) AS dedup_percentage
FROM stats;
```

**Expected output:**

```
 unique_bytes | total_bytes_without_dedup | bytes_saved | dedup_percentage
--------------+---------------------------+-------------+------------------
          225 |                       410 |         185 |            45.12
```

**Result:** ~45% storage reduction from deduplication alone!

---

## Step 9: Query by Semantic Similarity

**Find atoms similar to "learning":**

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
  AND ST_DWithin(a.spatial_key, t.spatial_key, 0.5)  -- Within radius 0.5
ORDER BY distance ASC
LIMIT 20;
```

**This is semantic search** — no embedding model needed!

---

## Key Takeaways

### 1. Content Addressing Works

- Same character anywhere = same atom
- Global deduplication via SHA-256
- Storage efficiency from reuse

### 2. Positions Emerge Organically

- No embedding model required
- Position = weighted centroid of neighbors
- Semantic clustering from co-occurrence

### 3. Composition Preserves Structure

- Hierarchical breakdown (document ? words ? chars)
- Sequence preserved via `sequence_index`
- Traversable in both directions

### 4. Relations Capture Semantics

- Hebbian learning ("fire together, wire together")
- Weights strengthen with use
- Relation types are atoms

### 5. Provenance Is Complete

- Neo4j graph tracks all derivations
- Every atom traceable to origin
- Full audit trail

### 6. Ingestion = Training

- No separate training phase
- Semantic space updated immediately
- Model "learns" from every document

---

## What's Next?

You now understand how atomization works. Continue with:

1. **[First Query Tutorial](first-query.md)** — Semantic search deep dive
2. **[Concepts: Spatial Semantics](../concepts/spatial-semantics.md)** — How positions are computed
3. **[API Reference](../api-reference/ingestion.md)** — All ingestion endpoints

---

## Cleanup

```sql
-- Delete tutorial atoms (if desired)
DELETE FROM atom
WHERE metadata->>'source' = 'tutorial';

-- Vacuum to reclaim space
VACUUM FULL atom;
```

---

**Next: [First Query Tutorial](first-query.md) ?**
