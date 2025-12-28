# Hartonomous Architecture

## Overview

Hartonomous is a four-layer system that transforms content into a queryable semantic substrate.

```
Content (text, code, model weights)
         ↓
    ATOM LAYER (Unicode → 4D → Hilbert)
         ↓
    COMPOSITION LAYER (Binary trees → Merkle hashes)
         ↓
    RELATIONSHIP LAYER (Edges with weights)
         ↓
    STORAGE LAYER (PostgreSQL + PostGIS)
```

## Layer 1: Atoms

### Purpose
Map every Unicode codepoint to a fixed point in 4D semantic space.

### Key Files
- `src/atoms/semantic_decompose.hpp` - Codepoint → (page, type, base, variant)
- `src/atoms/semantic_hilbert.hpp` - Semantic coordinates → 128-bit Hilbert index
- `src/atoms/atom_id.hpp` - The AtomId type (int64 high, int64 low)

### Semantic Decomposition

Every codepoint decomposes into four coordinates:

| Coordinate | Bits | Range | Meaning |
|------------|------|-------|---------|
| page | 3 | 0-7 | Script family (Latin=0, Greek=1, CJK=2...) |
| type | 3 | 0-7 | Character class (Control, Letter, Number, Punctuation...) |
| base | 21 | 0-2M | Canonical base character |
| variant | 5 | 0-31 | Case, diacritical, stylistic variant |

**Examples:**
```cpp
'a' → (page=0, type=5, base=0x61, variant=0)   // Latin, Letter, 'a', base form
'A' → (page=0, type=5, base=0x61, variant=1)   // Same base, uppercase variant
'à' → (page=0, type=5, base=0x61, variant=8)   // Same base, lowercase + grave
```

### Hilbert Curve Mapping

4D semantic coordinates map to a 128-bit Hilbert index:

```cpp
AtomId id = SemanticHilbert::from_semantic(coord);
// id.high = upper 64 bits
// id.low = lower 64 bits
```

**Properties:**
- Bijective: Every coordinate has exactly one index
- Locality-preserving: Similar coordinates → similar indices
- Deterministic: Same input always produces same output

### Tesseract Surface

Atoms live on the surface of a 4D hypercube (tesseract):

```cpp
// Center-origin coordinates: -BOUNDARY to +BOUNDARY
constexpr int32_t TESSERACT_BOUNDARY = 1073741823; // 2^30 - 1

TesseractSurfacePoint point = TesseractSurface::map_codepoint('a');
// point.x, point.y, point.z, point.w are signed 32-bit
// point.face indicates which of the 8 cubic faces
```

## Layer 2: Compositions

### Purpose
Encode content as binary trees with Merkle hashes for content-addressing.

### Key Files
- `src/atoms/pair_encoding_cascade.hpp` - The CPE algorithm
- `src/atoms/pair_encoding_engine.hpp` - Parallel encoding engine
- `src/atoms/merkle_hash.hpp` - Hash computation
- `src/atoms/node_ref.hpp` - The NodeRef type
- `src/atoms/composition_store.hpp` - In-memory composition cache
- `src/atoms/rle_sequence.hpp` - RLE-compressed sequences

### Cascading Pair Encoding (CPE)

Text is encoded by recursive pair composition:

```
"abc" → 'a' ∘ 'b' → ab
        ab ∘ 'c' → abc
```

Each composition produces a new NodeRef:
```cpp
NodeRef compose(NodeRef left, NodeRef right) {
    auto [high, low] = MerkleHash::compute({left, right});
    return NodeRef::comp(high, low);
}
```

### Merkle Hashing

Order-sensitive FNV-1a based hash:

```cpp
// Position is mixed in first to ensure hash(A,B) ≠ hash(B,A)
for (position = 0; position < children.size(); position++) {
    hash ^= position;
    hash *= FNV_PRIME;
    hash ^= child.id;
    hash *= FNV_PRIME;
}
```

### NodeRef

The universal reference type:

```cpp
struct NodeRef {
    int64_t id_high;  // Upper 64 bits of 128-bit ID
    int64_t id_low;   // Lower 64 bits
    bool is_atom;     // true = AtomId, false = composition hash

    static NodeRef atom(AtomId id);
    static NodeRef comp(int64_t high, int64_t low);
};
```

### RLE Compression

Repeated sequences are compressed:

```
"aaabbc" → [('a', 3), ('b', 2), ('c', 1)]
```

This reduces memory and enables O(log n) random access via cumulative count caching.

## Layer 3: Relationships

### Purpose
Store weighted edges between NodeRefs with context and trajectories.

### Key Files
- `src/db/query_store.hpp` - High-level relationship queries
- `src/db/schema.hpp` - Database schema definitions
- `src/model/model_ingest.hpp` - AI model ingestion

### Relationship Structure

```cpp
struct Relationship {
    NodeRef from;      // Source node
    NodeRef to;        // Target node
    double weight;     // Relationship strength
    RelType rel_type;  // SEMANTIC_LINK, MODEL_WEIGHT, etc.
    NodeRef context;   // Context (model ID, document ID)
};
```

### Relationship Types

| Type | Value | Meaning |
|------|-------|---------|
| REL_DEFAULT | 0 | General semantic relationship |
| REL_MODEL_WEIGHT | 1 | From AI model ingestion |
| REL_ATTENTION | 2 | Attention pattern |
| REL_KNOWLEDGE | 3 | Explicit knowledge edge |
| REL_TEMPORAL | 4 | Sequential relationship |
| REL_SPATIAL | 5 | Geometric proximity |

### Weight Aggregation

Duplicate relationships aggregate rather than duplicate:

```sql
INSERT INTO relationship (from_high, from_low, to_high, to_low, weight, context_high, context_low)
VALUES ($1, $2, $3, $4, $5, $6, $7)
ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low)
DO UPDATE SET
    weight = (relationship.weight + EXCLUDED.weight) / 2,
    obs_count = relationship.obs_count + 1;
```

### Trajectories

Relationships can include 4D paths through semantic space:

```sql
trajectory geometry(LineStringZM)  -- PostGIS 4D line
```

Used for:
- Fréchet distance similarity
- Path intersection queries
- Geometric containment checks

## Layer 4: Storage

### Purpose
Persist everything to PostgreSQL with PostGIS for spatial queries.

### Key Files
- `src/db/connection.hpp` - Connection management
- `src/db/database_store.hpp` - Low-level COPY operations
- `src/db/bulk_store.hpp` - High-performance bulk writes
- `src/db/seeder.hpp` - Schema setup and atom seeding

### Schema

```sql
-- Atoms: pre-computed, never change
CREATE TABLE atom (
    hilbert_high BIGINT NOT NULL,
    hilbert_low BIGINT NOT NULL,
    codepoint INTEGER,
    PRIMARY KEY (hilbert_high, hilbert_low)
);

-- Compositions: content-addressed binary trees
CREATE TABLE composition (
    hilbert_high BIGINT NOT NULL,
    hilbert_low BIGINT NOT NULL,
    left_high BIGINT NOT NULL,
    left_low BIGINT NOT NULL,
    right_high BIGINT NOT NULL,
    right_low BIGINT NOT NULL,
    PRIMARY KEY (hilbert_high, hilbert_low)
);

-- Relationships: weighted edges with context
CREATE TABLE relationship (
    from_high BIGINT NOT NULL,
    from_low BIGINT NOT NULL,
    to_high BIGINT NOT NULL,
    to_low BIGINT NOT NULL,
    weight DOUBLE PRECISION NOT NULL,
    rel_type SMALLINT DEFAULT 0,
    context_high BIGINT DEFAULT 0,
    context_low BIGINT DEFAULT 0,
    obs_count INTEGER DEFAULT 1,
    trajectory geometry(LineStringZM),
    PRIMARY KEY (from_high, from_low, to_high, to_low, context_high, context_low)
);
```

### COPY Protocol

All bulk operations use PostgreSQL's COPY protocol for maximum throughput:

```cpp
conn.start_copy("COPY composition FROM STDIN");
for (const auto& comp : compositions) {
    conn.put_copy_data(format_row(comp));
}
conn.end_copy();
```

### Indexes

```sql
-- B-tree for exact lookups
CREATE INDEX idx_rel_from ON relationship (from_high, from_low);
CREATE INDEX idx_rel_to ON relationship (to_high, to_low);
CREATE INDEX idx_rel_context ON relationship (context_high, context_low);

-- GiST for spatial queries
CREATE INDEX idx_rel_trajectory ON relationship USING GIST(trajectory);
```

## Data Flow

### Content Ingestion

```
Input bytes
    ↓
UTF-8 decode → codepoints
    ↓
Semantic decompose → 4D coordinates
    ↓
Hilbert encode → AtomIds
    ↓
CPE → composition tree
    ↓
Merkle hash → root NodeRef
    ↓
COPY to PostgreSQL
```

### Model Ingestion

```
Model directory
    ↓
Parse tokenizer → token texts
    ↓
CPE each token → Token NodeRefs
    ↓
Load weight tensors (skip embeddings!)
    ↓
Extract significant weights (top ~10%)
    ↓
Map to (from_idx, to_idx, weight)
    ↓
Aggregate duplicates
    ↓
COPY to relationship table
```

### Query Execution

```
Query text
    ↓
CPE → NodeRef
    ↓
B-tree lookup → direct relationships
    ↓
Spatial query → trajectory neighbors
    ↓
Graph traversal → multi-hop paths
    ↓
Decode NodeRefs → output content
```

## Threading Model

### Physical Core Detection

```cpp
// Use only physical cores, not hyperthreads
size_t cores = Threading::physical_core_count();
```

### Work-Stealing Parallel For

```cpp
Threading::parallel_for(items.size(), [&](size_t i) {
    process(items[i]);
});
```

Uses atomic work queue for load balancing across cores.

### Thread-Local Caches

Encoding uses thread-local composition caches to avoid contention:

```cpp
thread_local CompositionStore local_cache;
// Merge to global after batch
```

## Memory Management

### Pre-allocation

All hot paths pre-allocate to avoid runtime allocations:

```cpp
compositions_.reserve(1000000);
pending_.reserve(100000);
copy_buffer_.reserve(80 * expected_rows);
```

### RLE Sequence Caching

RLE sequences cache cumulative counts for O(log n) random access:

```cpp
// Binary search on cached cumulative counts
size_t operator[](size_t logical_index) const;
```

Cache invalidated on modification, rebuilt lazily on next access.

## Error Handling

### Database Errors

All database operations throw on failure:

```cpp
PgResult::expect_ok(const char* context);  // Throws if not PGRES_COMMAND_OK
PgResult::expect_tuples(const char* context);  // Throws if not PGRES_TUPLES_OK
```

### Encoding Errors

Invalid UTF-8 produces replacement character atoms (U+FFFD).
Invalid codepoints return zero-initialized results.

## Performance Characteristics

| Operation | Complexity | Notes |
|-----------|------------|-------|
| Atom lookup | O(1) | Direct computation, no table |
| Composition lookup | O(log n) | B-tree on primary key |
| Relationship lookup | O(log n) | B-tree index |
| Spatial query | O(log n) | GiST R-tree index |
| CPE encoding | O(n) | Linear in content length |
| Bulk COPY | O(n) | Linear in row count |

## Invariants

1. **Determinism**: Same input → same output, always
2. **Losslessness**: decode(encode(x)) = x
3. **Uniqueness**: Each composition has exactly one NodeRef
4. **Locality**: Nearby Hilbert indices → nearby semantic coordinates
5. **Idempotence**: Re-ingesting same content is safe (ON CONFLICT)
