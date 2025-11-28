# Compositions

**Hierarchical structures: How atoms combine into molecules, compounds, and complex data.**

---

## What Are Compositions?

**Compositions** define hierarchical structure: what contains what, in what order.

```sql
Parent Atom ? [Component Atom 1, Component Atom 2, ..., Component Atom N]
```

**Examples:**
- Document ? Sentences ? Words ? Characters
- Vector ? Float values (sparse)
- Image ? Pixels ? RGB values
- Model ? Layers ? Weights ? Floats

**Key principle:** Complex structures are **recursive compositions** of simpler atoms.

---

## Composition Table Schema

### Complete DDL

```sql
CREATE TABLE atom_composition (
    -- Identity
    composition_id BIGSERIAL PRIMARY KEY,
    
    -- Parent-child relationship
    parent_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    component_atom_id BIGINT NOT NULL REFERENCES atom(atom_id) ON DELETE CASCADE,
    
    -- Order matters - sequence within parent
    sequence_index BIGINT NOT NULL,
    
    -- Local coordinate frame (position relative to parent)
    spatial_key GEOMETRY(POINTZ, 0),
    
    -- Flexible metadata
    metadata JSONB NOT NULL DEFAULT '{}'::jsonb,
    
    -- Temporal versioning
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'::timestamptz,
    
    -- Uniqueness constraint
    UNIQUE (parent_atom_id, component_atom_id, sequence_index)
);

-- Critical indexes
CREATE INDEX idx_composition_parent ON atom_composition(parent_atom_id, sequence_index);
CREATE INDEX idx_composition_component ON atom_composition(component_atom_id);
```

### Field Descriptions

| Field | Type | Purpose |
|-------|------|---------|
| `composition_id` | BIGSERIAL | Unique identifier |
| `parent_atom_id` | BIGINT | Container/parent atom |
| `component_atom_id` | BIGINT | Contained/child atom |
| `sequence_index` | BIGINT | Position within parent (order preserved) |
| `spatial_key` | GEOMETRY(POINTZ) | Local position relative to parent |
| `metadata` | JSONB | Composition-specific attributes |

---

## Text Composition

### Example: "Hello World"

**Hierarchy:**

```
Document (atom_id=1000)
  ?? Word: "Hello" (atom_id=1001)
  ?   ?? 'H' (atom_id=72)  [sequence_index=0]
  ?   ?? 'e' (atom_id=101) [sequence_index=1]
  ?   ?? 'l' (atom_id=108) [sequence_index=2]
  ?   ?? 'l' (atom_id=108) [sequence_index=3]
  ?   ?? 'o' (atom_id=111) [sequence_index=4]
  ?? Word: "World" (atom_id=1002)
      ?? 'W' (atom_id=87)  [sequence_index=0]
      ?? 'o' (atom_id=111) [sequence_index=1]
      ?? 'r' (atom_id=114) [sequence_index=2]
      ?? 'l' (atom_id=108) [sequence_index=3]
      ?? 'd' (atom_id=100) [sequence_index=4]
```

**SQL representation:**

```sql
-- Word "Hello" composed of characters
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (1001, 72, 0),   -- 'H'
    (1001, 101, 1),  -- 'e'
    (1001, 108, 2),  -- 'l'
    (1001, 108, 3),  -- 'l'
    (1001, 111, 4);  -- 'o'

-- Document composed of words
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (1000, 1001, 0),  -- "Hello"
    (1000, 1002, 1);  -- "World"
```

**Observations:**
- Character `'l'` (atom_id=108) appears **3 times** (indices 2, 3 in "Hello", index 3 in "World")
- Same atom referenced multiple times = **deduplication**
- `sequence_index` preserves order

---

## Sparse Representation

### Key Insight: Missing = Zero

**Only non-zero values stored.**

```sql
-- Dense vector: [0.0, 0.23, 0.0, 0.0, -0.14, 0.0, 0.87]
-- Traditional storage: 7 rows

-- Sparse storage (Hartonomous): 3 rows
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (vector_id, atomize_float(0.23), 1),   -- Index 1
    (vector_id, atomize_float(-0.14), 4),  -- Index 4
    (vector_id, atomize_float(0.87), 6);   -- Index 6

-- Indices 0, 2, 3, 5 are implicit zeros (no rows)
```

**Benefits:**
- **Storage reduction**: 3/7 = 43% of dense storage
- **Query speed**: Fewer rows to scan
- **Compression**: Works naturally with sparse data (embeddings, models)

### Query Sparse Data

```sql
-- Reconstruct dense vector
SELECT 
    sequence_index,
    COALESCE(a.canonical_text::numeric, 0.0) AS value
FROM generate_series(0, 6) AS sequence_index
LEFT JOIN atom_composition ac ON ac.sequence_index = sequence_index
    AND ac.parent_atom_id = $vector_id
LEFT JOIN atom a ON a.atom_id = ac.component_atom_id
ORDER BY sequence_index;

-- Result:
-- 0 ? 0.0
-- 1 ? 0.23
-- 2 ? 0.0
-- 3 ? 0.0
-- 4 ? -0.14
-- 5 ? 0.0
-- 6 ? 0.87
```

---

## Image Composition

### Example: 2×2 RGB Image

**Hierarchy:**

```
Image (atom_id=2000)
  ?? Pixel(0,0) RGB(255,87,51) (atom_id=2001) [sequence_index=0]
  ?? Pixel(0,1) RGB(100,200,50) (atom_id=2002) [sequence_index=1]
  ?? Pixel(1,0) RGB(50,100,200) (atom_id=2003) [sequence_index=2]
  ?? Pixel(1,1) RGB(200,50,100) (atom_id=2004) [sequence_index=3]
```

**SQL:**

```sql
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (2000, 2001, 0),  -- Top-left
    (2000, 2002, 1),  -- Top-right
    (2000, 2003, 2),  -- Bottom-left
    (2000, 2004, 3);  -- Bottom-right
```

**Row-major order:** `sequence_index = y * width + x`

**For 256×256 image:**
- 65,536 pixel atoms
- If many pixels have same RGB ? deduplication
- Example: Solid blue background ? 1 pixel atom referenced 40,000 times

---

## Model Weight Composition

### Example: Neural Network Layer

```
Layer5 (atom_id=5000)
  ?? Weight[0] = 0.12 (atom_id=5001) [sequence_index=0]
  ?? Weight[1] = 0.0  (implicit zero, no row)
  ?? Weight[2] = -0.34 (atom_id=5002) [sequence_index=2]
  ?? Weight[3] = 0.0  (implicit zero, no row)
  ?? Weight[4] = 0.87 (atom_id=5003) [sequence_index=4]
```

**Benefits for models:**
- **Quantized weights** deduplicate (e.g., 8-bit quantization ? 256 unique values)
- **Sparse models** (pruned) store efficiently
- **Model diffs** = composition changes (no full re-upload)

---

## Traversal Queries

### Top-Down: Parent ? Components

```sql
-- What is "Hello" composed of?
SELECT 
    ac.sequence_index,
    c.canonical_text AS component
FROM atom_composition ac
JOIN atom c ON c.atom_id = ac.component_atom_id
WHERE ac.parent_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'Hello')
ORDER BY ac.sequence_index;

-- Result:
-- 0 ? H
-- 1 ? e
-- 2 ? l
-- 3 ? l
-- 4 ? o
```

### Bottom-Up: Component ? Parents

```sql
-- What uses character 'l'?
SELECT 
    p.canonical_text AS parent,
    COUNT(*) AS times_used,
    array_agg(ac.sequence_index ORDER BY ac.sequence_index) AS positions
FROM atom_composition ac
JOIN atom p ON p.atom_id = ac.parent_atom_id
WHERE ac.component_atom_id = (SELECT atom_id FROM atom WHERE canonical_text = 'l')
GROUP BY p.canonical_text
ORDER BY times_used DESC;

-- Result:
-- "Hello" ? 2 times ? [2, 3]
-- "World" ? 1 time  ? [3]
-- "parallel" ? 3 times ? [4, 5, 6]
```

### Multi-Level Traversal

```sql
-- Recursive: Document ? Sentences ? Words ? Characters
WITH RECURSIVE hierarchy AS (
    -- Base: Start at document
    SELECT 
        atom_id,
        canonical_text,
        0 AS depth
    FROM atom
    WHERE atom_id = $document_id
    
    UNION ALL
    
    -- Recursive: Follow compositions
    SELECT 
        c.atom_id,
        c.canonical_text,
        h.depth + 1
    FROM hierarchy h
    JOIN atom_composition ac ON ac.parent_atom_id = h.atom_id
    JOIN atom c ON c.atom_id = ac.component_atom_id
    WHERE h.depth < 10  -- Max depth
)
SELECT * FROM hierarchy
ORDER BY depth, canonical_text;
```

---

## Local Coordinate Frames

### Spatial Key in Compositions

`spatial_key` in `atom_composition` = **position relative to parent**.

**Use case:** Image pixel positions

```sql
-- Store pixel position (x, y) relative to image origin
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index, spatial_key)
VALUES (
    image_id,
    pixel_atom_id,
    y * width + x,
    ST_MakePoint(x, y, 0)  -- Local 2D position
);

-- Query pixels in region (50,50) to (100,100)
SELECT 
    ac.component_atom_id,
    ST_X(ac.spatial_key) AS x,
    ST_Y(ac.spatial_key) AS y
FROM atom_composition ac
WHERE ac.parent_atom_id = $image_id
  AND ac.spatial_key && ST_MakeEnvelope(50, 50, 100, 100);
```

**Benefits:**
- Spatial queries within composition
- Geometric operations on components
- Efficient region extraction

---

## Metadata in Compositions

### Composition-Specific Attributes

```json
{
  "compression_type": "delta",
  "quantization_bits": 8,
  "original_dtype": "float32",
  "sparsity_ratio": 0.95
}
```

**Example: Quantized weight**

```sql
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index, metadata)
VALUES (
    layer_id,
    quantized_weight_atom_id,
    idx,
    jsonb_build_object(
        'original_value', 0.123456,
        'quantized_value', 127,
        'quantization_error', 0.000012
    )
);
```

---

## Performance Characteristics

### Storage Efficiency

**Dense vs. Sparse:**

```
Dense 1998D embedding (all zeros except 50 values):
- Traditional: 1998 × 12 bytes = 23,976 bytes
- Hartonomous: 50 × 12 bytes = 600 bytes
- Savings: 97.5%
```

**Deduplication:**

```
1000 documents, each with "the" (atom_id=1234):
- Traditional: 1000 rows
- Hartonomous: 1000 rows BUT same component_atom_id
- Query "What contains 'the'?" = 1 index lookup
```

### Query Performance

**Top-down (parent ? components):**
```sql
-- O(K) where K = number of components
-- Index on (parent_atom_id, sequence_index)
SELECT * FROM atom_composition WHERE parent_atom_id = $id ORDER BY sequence_index;
```

**Bottom-up (component ? parents):**
```sql
-- O(M) where M = number of parents using component
-- Index on (component_atom_id)
SELECT * FROM atom_composition WHERE component_atom_id = $id;
```

**Recursive traversal:**
```sql
-- O(N × log N) where N = total nodes in tree
-- Uses both indexes
WITH RECURSIVE ... (see query above)
```

---

## Common Patterns

### Upsert Composition

```sql
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES ($parent, $component, $index)
ON CONFLICT (parent_atom_id, component_atom_id, sequence_index) DO UPDATE SET
    metadata = atom_composition.metadata || EXCLUDED.metadata,
    spatial_key = COALESCE(EXCLUDED.spatial_key, atom_composition.spatial_key);
```

### Batch Insert

```sql
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
SELECT 
    $parent_id,
    atomize_value(value),
    idx
FROM unnest($values) WITH ORDINALITY AS t(value, idx)
WHERE abs(value) > $threshold;  -- Sparse: skip zeros
```

### Delete Composition

```sql
-- Delete all components of parent (CASCADE handles this)
DELETE FROM atom WHERE atom_id = $parent_id;

-- Delete specific component
DELETE FROM atom_composition
WHERE parent_atom_id = $parent_id
  AND sequence_index = $index;
```

### Reorder Components

```sql
-- Swap indices 2 and 3
UPDATE atom_composition
SET sequence_index = CASE 
    WHEN sequence_index = 2 THEN 3
    WHEN sequence_index = 3 THEN 2
    ELSE sequence_index
END
WHERE parent_atom_id = $parent_id
  AND sequence_index IN (2, 3);
```

---

## Composition Types

### Sequential (Ordered)

**Use:** Text, time series, audio

```sql
-- Preserve order via sequence_index
SELECT * FROM atom_composition WHERE parent_atom_id = $id ORDER BY sequence_index;
```

### Spatial (Positioned)

**Use:** Images, 3D models, point clouds

```sql
-- Query by spatial proximity
SELECT * FROM atom_composition 
WHERE parent_atom_id = $id 
  AND spatial_key && ST_MakeEnvelope(...);
```

### Sparse (Implicit Zeros)

**Use:** Embeddings, model weights, sparse matrices

```sql
-- Only non-zero values stored
SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = $vector_id;
-- Returns: 50 (out of 1998 dimensions)
```

### Hierarchical (Recursive)

**Use:** Documents, file systems, nested structures

```sql
-- Multi-level traversal
WITH RECURSIVE tree AS (...) SELECT * FROM tree;
```

---

## Composition Lifecycle

### 1. Creation

```
Parent Atom Created ? Components Atomized ? Compositions Inserted
```

### 2. Traversal

```
Query Parent ? Fetch Components ? Reconstruct Structure
```

### 3. Modification

```
Add/Remove Component ? Update Compositions ? Maintain Indices
```

### 4. Deletion

```
Delete Parent ? CASCADE Deletes Compositions ? Components Remain (if referenced elsewhere)
```

---

## Key Takeaways

### 1. Hierarchy = Compositions

Complex structures built from simple atoms via parent-child relationships.

### 2. Sparse by Default

Missing `sequence_index` = implicit zero. Only non-zero stored.

### 3. Order Preserved

`sequence_index` maintains sequence (text, vectors, time series).

### 4. Deduplication Works

Same component referenced many times (e.g., 'e' in 1000 documents).

### 5. Spatial Positioning Optional

`spatial_key` enables geometric queries within composition.

### 6. Recursive Traversal

Multi-level hierarchies navigable via recursive CTEs.

---

## Next Steps

Now that you understand compositions, continue with:

1. **[Relations](relations.md)** — How atoms connect semantically
2. **[Spatial Semantics](spatial-semantics.md)** — How positions are computed
3. **[Compression](compression.md)** — Multi-layer encoding strategies

---

**Next: [Relations ?](relations.md)**
