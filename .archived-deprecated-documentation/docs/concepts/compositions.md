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

### Example: 2�2 RGB Image

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

**For 256�256 image:**
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
- Traditional: 1998 � 12 bytes = 23,976 bytes
- Hartonomous: 50 � 12 bytes = 600 bytes
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
-- O(N � log N) where N = total nodes in tree
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

## Advanced Composition Patterns

### Lazy Loading for Deep Hierarchies

Efficiently load large compositions on-demand:

```python
from typing import List, Optional
from dataclasses import dataclass, field

@dataclass
class LazyComposition:
    """Composition with lazy loading of components."""
    composition_id: int
    parent_atom_id: int
    _components: Optional[List['LazyAtom']] = field(default=None, repr=False)
    _loaded: bool = field(default=False, repr=False)
    
    async def get_components(self, db_pool) -> List['LazyAtom']:
        """Load components on first access."""
        if not self._loaded:
            query = """
                SELECT component_atom_id, sequence_index, spatial_key, metadata
                FROM atom_composition
                WHERE parent_atom_id = $1
                ORDER BY sequence_index ASC
            """
            
            async with db_pool.connection() as conn:
                async with conn.cursor() as cursor:
                    await cursor.execute(query, (self.parent_atom_id,))
                    rows = await cursor.fetchall()
                    
                    self._components = [
                        LazyAtom(
                            atom_id=row[0],
                            sequence_index=row[1],
                            spatial_key=row[2],
                            metadata=row[3]
                        )
                        for row in rows
                    ]
                    self._loaded = True
        
        return self._components

@dataclass
class LazyAtom:
    """Atom with lazy content loading."""
    atom_id: int
    sequence_index: int
    spatial_key: Optional[tuple]
    metadata: dict
    _content: Optional[bytes] = field(default=None, repr=False)
    
    async def get_content(self, db_pool) -> bytes:
        """Load content on first access."""
        if self._content is None:
            query = "SELECT content FROM atom WHERE atom_id = $1"
            
            async with db_pool.connection() as conn:
                async with conn.cursor() as cursor:
                    await cursor.execute(query, (self.atom_id,))
                    row = await cursor.fetchone()
                    self._content = row[0] if row else b''
        
        return self._content

# Usage
composition = LazyComposition(composition_id=1, parent_atom_id=100)
components = await composition.get_components(db_pool)  # Loaded on demand
```

### Composition Caching for Performance

Cache frequently accessed compositions:

```python
from functools import lru_cache
import asyncio
from datetime import datetime, timedelta

class CompositionCache:
    """LRU cache for compositions with TTL."""
    
    def __init__(self, max_size: int = 1000, ttl_seconds: int = 300):
        self.max_size = max_size
        self.ttl = timedelta(seconds=ttl_seconds)
        self.cache: dict[int, tuple[datetime, dict]] = {}
    
    def get(self, composition_id: int) -> Optional[dict]:
        """Get cached composition if not expired."""
        if composition_id in self.cache:
            timestamp, composition = self.cache[composition_id]
            if datetime.now() - timestamp < self.ttl:
                return composition
            else:
                del self.cache[composition_id]
        return None
    
    def put(self, composition_id: int, composition: dict):
        """Store composition in cache."""
        # Evict oldest if at capacity
        if len(self.cache) >= self.max_size:
            oldest = min(self.cache.items(), key=lambda x: x[1][0])
            del self.cache[oldest[0]]
        
        self.cache[composition_id] = (datetime.now(), composition)
    
    def invalidate(self, composition_id: int):
        """Remove composition from cache."""
        if composition_id in self.cache:
            del self.cache[composition_id]

class CachedCompositionService:
    """Composition service with caching."""
    
    def __init__(self, db_pool, cache: CompositionCache):
        self.pool = db_pool
        self.cache = cache
    
    async def get_composition(self, composition_id: int) -> dict:
        """Get composition with caching."""
        # Check cache first
        cached = self.cache.get(composition_id)
        if cached:
            return cached
        
        # Load from database
        query = """
            SELECT parent_atom_id, component_atom_id, sequence_index, metadata
            FROM atom_composition
            WHERE composition_id = $1
        """
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (composition_id,))
                row = await cursor.fetchone()
                
                if row:
                    composition = {
                        "composition_id": composition_id,
                        "parent_atom_id": row[0],
                        "component_atom_id": row[1],
                        "sequence_index": row[2],
                        "metadata": row[3]
                    }
                    
                    # Store in cache
                    self.cache.put(composition_id, composition)
                    return composition
                
                return None

# Usage
cache = CompositionCache(max_size=1000, ttl_seconds=300)
service = CachedCompositionService(db_pool, cache)
composition = await service.get_composition(123)
```

### Composition Streaming for Large Datasets

Stream compositions to avoid memory overflow:

```python
from typing import AsyncIterator

class CompositionStreamer:
    """Stream compositions in batches."""
    
    def __init__(self, db_pool, batch_size: int = 1000):
        self.pool = db_pool
        self.batch_size = batch_size
    
    async def stream_components(
        self,
        parent_atom_id: int
    ) -> AsyncIterator[List[dict]]:
        """Stream composition components in batches."""
        offset = 0
        
        while True:
            query = """
                SELECT component_atom_id, sequence_index, spatial_key, metadata
                FROM atom_composition
                WHERE parent_atom_id = $1
                ORDER BY sequence_index ASC
                LIMIT $2 OFFSET $3
            """
            
            async with self.pool.connection() as conn:
                async with conn.cursor() as cursor:
                    await cursor.execute(
                        query,
                        (parent_atom_id, self.batch_size, offset)
                    )
                    rows = await cursor.fetchall()
                    
                    if not rows:
                        break
                    
                    batch = [
                        {
                            "component_atom_id": row[0],
                            "sequence_index": row[1],
                            "spatial_key": row[2],
                            "metadata": row[3]
                        }
                        for row in rows
                    ]
                    
                    yield batch
                    offset += self.batch_size

# Usage
streamer = CompositionStreamer(db_pool, batch_size=1000)
async for batch in streamer.stream_components(parent_atom_id=100):
    print(f"Processing batch of {len(batch)} components")
    # Process batch...
```

### Composition Diff for Versioning

Compute differences between composition versions:

```python
from typing import Set, Tuple
from dataclasses import dataclass

@dataclass
class CompositionDiff:
    """Difference between two compositions."""
    added: Set[int]  # Added component atom IDs
    removed: Set[int]  # Removed component atom IDs
    reordered: List[Tuple[int, int, int]]  # (atom_id, old_index, new_index)
    metadata_changed: List[int]  # Component atom IDs with metadata changes

class CompositionDiffCalculator:
    """Calculate differences between composition versions."""
    
    async def compute_diff(
        self,
        db_pool,
        old_parent_id: int,
        new_parent_id: int
    ) -> CompositionDiff:
        """Compute diff between two composition versions."""
        # Load old composition
        old_components = await self._load_components(db_pool, old_parent_id)
        new_components = await self._load_components(db_pool, new_parent_id)
        
        old_atoms = {c["component_atom_id"]: c for c in old_components}
        new_atoms = {c["component_atom_id"]: c for c in new_components}
        
        # Compute differences
        added = set(new_atoms.keys()) - set(old_atoms.keys())
        removed = set(old_atoms.keys()) - set(new_atoms.keys())
        
        # Check for reordering
        reordered = []
        for atom_id in set(old_atoms.keys()) & set(new_atoms.keys()):
            old_idx = old_atoms[atom_id]["sequence_index"]
            new_idx = new_atoms[atom_id]["sequence_index"]
            if old_idx != new_idx:
                reordered.append((atom_id, old_idx, new_idx))
        
        # Check for metadata changes
        metadata_changed = []
        for atom_id in set(old_atoms.keys()) & set(new_atoms.keys()):
            if old_atoms[atom_id]["metadata"] != new_atoms[atom_id]["metadata"]:
                metadata_changed.append(atom_id)
        
        return CompositionDiff(
            added=added,
            removed=removed,
            reordered=reordered,
            metadata_changed=metadata_changed
        )
    
    async def _load_components(
        self,
        db_pool,
        parent_atom_id: int
    ) -> List[dict]:
        """Load all components for a composition."""
        query = """
            SELECT component_atom_id, sequence_index, metadata
            FROM atom_composition
            WHERE parent_atom_id = $1
            ORDER BY sequence_index ASC
        """
        
        async with db_pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (parent_atom_id,))
                rows = await cursor.fetchall()
                
                return [
                    {
                        "component_atom_id": row[0],
                        "sequence_index": row[1],
                        "metadata": row[2]
                    }
                    for row in rows
                ]

# Usage
calculator = CompositionDiffCalculator()
diff = await calculator.compute_diff(db_pool, old_parent_id=100, new_parent_id=101)
print(f"Added: {len(diff.added)}, Removed: {len(diff.removed)}")
print(f"Reordered: {len(diff.reordered)}, Metadata changed: {len(diff.metadata_changed)}")
```

### Composition Merge Strategies

Merge multiple compositions intelligently:

```python
from enum import Enum

class MergeStrategy(Enum):
    """Strategies for merging compositions."""
    UNION = "union"  # Include all components from both
    INTERSECTION = "intersection"  # Only common components
    APPEND = "append"  # Concatenate sequences
    INTERLEAVE = "interleave"  # Alternate components

class CompositionMerger:
    """Merge compositions using different strategies."""
    
    async def merge(
        self,
        db_pool,
        parent_id_a: int,
        parent_id_b: int,
        strategy: MergeStrategy = MergeStrategy.UNION
    ) -> List[dict]:
        """Merge two compositions."""
        components_a = await self._load_components(db_pool, parent_id_a)
        components_b = await self._load_components(db_pool, parent_id_b)
        
        if strategy == MergeStrategy.UNION:
            return self._merge_union(components_a, components_b)
        elif strategy == MergeStrategy.INTERSECTION:
            return self._merge_intersection(components_a, components_b)
        elif strategy == MergeStrategy.APPEND:
            return self._merge_append(components_a, components_b)
        elif strategy == MergeStrategy.INTERLEAVE:
            return self._merge_interleave(components_a, components_b)
    
    def _merge_union(self, a: List[dict], b: List[dict]) -> List[dict]:
        """Union: all unique components."""
        seen = set()
        result = []
        
        for comp in a + b:
            atom_id = comp["component_atom_id"]
            if atom_id not in seen:
                seen.add(atom_id)
                result.append(comp)
        
        # Re-index sequences
        for i, comp in enumerate(result):
            comp["sequence_index"] = i
        
        return result
    
    def _merge_intersection(self, a: List[dict], b: List[dict]) -> List[dict]:
        """Intersection: only common components."""
        atoms_a = {c["component_atom_id"] for c in a}
        atoms_b = {c["component_atom_id"] for c in b}
        common = atoms_a & atoms_b
        
        result = [c for c in a if c["component_atom_id"] in common]
        
        # Re-index
        for i, comp in enumerate(result):
            comp["sequence_index"] = i
        
        return result
    
    def _merge_append(self, a: List[dict], b: List[dict]) -> List[dict]:
        """Append: concatenate sequences."""
        result = a.copy()
        offset = len(a)
        
        for comp in b:
            comp_copy = comp.copy()
            comp_copy["sequence_index"] = offset
            result.append(comp_copy)
            offset += 1
        
        return result
    
    def _merge_interleave(self, a: List[dict], b: List[dict]) -> List[dict]:
        """Interleave: alternate components."""
        result = []
        max_len = max(len(a), len(b))
        
        for i in range(max_len):
            if i < len(a):
                comp = a[i].copy()
                comp["sequence_index"] = len(result)
                result.append(comp)
            
            if i < len(b):
                comp = b[i].copy()
                comp["sequence_index"] = len(result)
                result.append(comp)
        
        return result
    
    async def _load_components(
        self,
        db_pool,
        parent_atom_id: int
    ) -> List[dict]:
        """Load composition components."""
        query = """
            SELECT component_atom_id, sequence_index, spatial_key, metadata
            FROM atom_composition
            WHERE parent_atom_id = $1
            ORDER BY sequence_index ASC
        """
        
        async with db_pool.connection() as conn:
            async with conn.cursor() as cursor:
                await cursor.execute(query, (parent_atom_id,))
                rows = await cursor.fetchall()
                
                return [
                    {
                        "component_atom_id": row[0],
                        "sequence_index": row[1],
                        "spatial_key": row[2],
                        "metadata": row[3]
                    }
                    for row in rows
                ]

# Usage
merger = CompositionMerger()
merged = await merger.merge(
    db_pool,
    parent_id_a=100,
    parent_id_b=101,
    strategy=MergeStrategy.UNION
)
print(f"Merged composition has {len(merged)} components")
```

---

**Next: [Relations →](relations.md)**
