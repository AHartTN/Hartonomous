# Hartonomous API Reference

## Overview

Hartonomous provides a C-compatible API for FFI interop and a C++ API for native development.

## Error Codes

All C API functions return integer status codes:

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Not found (for lookup operations) |
| -1 | Null pointer argument |
| -2 | Invalid argument / database error |
| -3 | Buffer too small / path doesn't exist |
| -100 | Database support not compiled |

## C API

### Core Types

#### HartonomousAtom

Result structure for codepoint mapping.

```c
typedef struct {
    int64_t hilbert_high;  // Upper 64 bits of 128-bit Hilbert index
    int64_t hilbert_low;   // Lower 64 bits of 128-bit Hilbert index
    int32_t codepoint;     // Original Unicode codepoint
    int32_t x;             // X coordinate [-INT32_MAX, +INT32_MAX]
    int32_t y;             // Y coordinate
    int32_t z;             // Z coordinate
    int32_t w;             // W coordinate
    uint8_t face;          // Tesseract face index (0-7)
} HartonomousAtom;
```

#### HartonomousRelationship

Weighted edge between two nodes.

```c
typedef struct {
    int64_t from_high;     // Source node upper 64 bits
    int64_t from_low;      // Source node lower 64 bits
    int64_t to_high;       // Target node upper 64 bits
    int64_t to_low;        // Target node lower 64 bits
    double weight;         // Relationship strength
    int16_t rel_type;      // Relationship type
    int64_t context_high;  // Context upper 64 bits
    int64_t context_low;   // Context lower 64 bits
} HartonomousRelationship;
```

#### HartonomousTrajectoryPoint

RLE-compressed point in 4D semantic space.

```c
typedef struct {
    int16_t page;          // X: Unicode page (0-7)
    int16_t type;          // Y: Character type (0-7)
    int32_t base;          // Z: Base character codepoint
    uint8_t variant;       // M: Case/diacritical variant (0-31)
    uint32_t count;        // RLE repetition count
} HartonomousTrajectoryPoint;
```

### Codepoint Mapping

#### hartonomous_map_codepoint

Map a Unicode codepoint to 4D tesseract coordinates and Hilbert index.

```c
int hartonomous_map_codepoint(int32_t codepoint, HartonomousAtom* result);
```

**Parameters:**
- `codepoint`: Unicode codepoint (0 to 0x10FFFF, excluding surrogates)
- `result`: Pointer to receive mapping result

**Returns:** 0 on success, -1 if result is NULL, -2 if codepoint invalid

**Example:**
```c
HartonomousAtom atom;
if (hartonomous_map_codepoint('A', &atom) == 0) {
    printf("Hilbert: %lld:%lld\n", atom.hilbert_high, atom.hilbert_low);
    printf("Coords: (%d, %d, %d, %d)\n", atom.x, atom.y, atom.z, atom.w);
}
```

#### hartonomous_map_codepoint_range

Map a range of codepoints efficiently.

```c
int hartonomous_map_codepoint_range(
    int32_t start,
    int32_t end,
    HartonomousAtom* results,
    int32_t results_capacity);
```

**Returns:** Number of successfully mapped codepoints, or -1 on error

#### hartonomous_get_hilbert_index

Get Hilbert index only (faster than full mapping).

```c
int hartonomous_get_hilbert_index(
    int32_t codepoint,
    int64_t* high,
    int64_t* low);
```

#### hartonomous_coords_to_hilbert

Convert 4D coordinates to Hilbert index directly.

```c
int hartonomous_coords_to_hilbert(
    uint32_t x, uint32_t y, uint32_t z, uint32_t w,
    int64_t* high, int64_t* low);
```

#### hartonomous_hilbert_to_coords

Convert Hilbert index back to 4D coordinates.

```c
int hartonomous_hilbert_to_coords(
    int64_t high, int64_t low,
    uint32_t* x, uint32_t* y, uint32_t* z, uint32_t* w);
```

### Database Operations

#### hartonomous_db_init

Initialize database connection and ensure schema exists.

```c
int hartonomous_db_init();
```

Uses `HARTONOMOUS_DB` environment variable or defaults to localhost:5432.
Idempotent - safe to call multiple times.

#### hartonomous_db_stats

Get database statistics.

```c
int hartonomous_db_stats(HartonomousDbStats* stats);
```

**HartonomousDbStats:**
```c
typedef struct {
    int64_t atom_count;
    int64_t composition_count;
    int64_t relationship_count;
    int64_t database_size_bytes;
} HartonomousDbStats;
```

#### hartonomous_ingest

Ingest a file or directory into the substrate.

```c
int hartonomous_ingest(
    const char* path,
    double sparsity,
    HartonomousIngestResult* result);
```

**Parameters:**
- `path`: Path to file or directory
- `sparsity`: Weight sparsity threshold (default 1e-6, skip weights below this)
- `result`: Optional pointer to receive detailed results

**Behavior by path type:**
- Directory with tokenizer + safetensors: AI model ingestion
- Regular directory: Recursive file ingestion
- Single file: Content ingestion
- .safetensors file: Weight matrix extraction

#### hartonomous_encode_and_store

Encode text and store in database.

```c
int hartonomous_encode_and_store(
    const char* text,
    int32_t text_len,
    int64_t* id_high,
    int64_t* id_low);
```

Returns the root NodeRef that uniquely identifies this content.

#### hartonomous_decode

Decode a root ID back to original text.

```c
int hartonomous_decode(
    int64_t id_high,
    int64_t id_low,
    char* buffer,
    int32_t buffer_capacity,
    int32_t* text_len);
```

Bit-perfect round-trip: `decode(encode(x)) == x`

### Spatial Queries

#### hartonomous_find_similar

Find K-nearest semantically similar atoms.

```c
int hartonomous_find_similar(
    int32_t codepoint,
    HartonomousSpatialMatch* results,
    int32_t capacity,
    int32_t* count);
```

#### hartonomous_find_near

Find atoms within distance threshold.

```c
int hartonomous_find_near(
    int32_t codepoint,
    double distance_threshold,
    HartonomousSpatialMatch* results,
    int32_t capacity,
    int32_t* count);
```

#### hartonomous_find_case_variants

Find all case variants of a character.

```c
int hartonomous_find_case_variants(
    int32_t codepoint,
    HartonomousSpatialMatch* results,
    int32_t capacity,
    int32_t* count);
```

'a' finds: 'A', 'à', 'á', 'â', 'ã', 'ä', 'å', 'À', 'Á', etc.

#### hartonomous_find_diacritical_variants

Find all diacritical variants of a base character.

```c
int hartonomous_find_diacritical_variants(
    int32_t codepoint,
    HartonomousSpatialMatch* results,
    int32_t capacity,
    int32_t* count);
```

### Relationship Queries

#### hartonomous_store_relationship

Store a weighted relationship.

```c
int hartonomous_store_relationship(
    int64_t from_high, int64_t from_low,
    int64_t to_high, int64_t to_low,
    double weight,
    int16_t rel_type,
    int64_t context_high, int64_t context_low);
```

**Relationship Types:**
```c
typedef enum {
    HARTONOMOUS_REL_SEMANTIC_LINK = 0,  // General semantic relationship
    HARTONOMOUS_REL_MODEL_WEIGHT = 1,   // From AI model ingestion
    HARTONOMOUS_REL_KNOWLEDGE_EDGE = 2, // Explicit knowledge graph
    HARTONOMOUS_REL_TEMPORAL_NEXT = 3,  // Sequential relationship
    HARTONOMOUS_REL_SPATIAL_NEAR = 4    // Geometric proximity
} HartonomousRelType;
```

Duplicate relationships aggregate weights rather than creating new edges.

#### hartonomous_find_from

Find outgoing relationships from a node.

```c
int hartonomous_find_from(
    int64_t from_high, int64_t from_low,
    HartonomousRelationship* results,
    int32_t capacity,
    int32_t* count);
```

#### hartonomous_find_to

Find incoming relationships to a node.

```c
int hartonomous_find_to(
    int64_t to_high, int64_t to_low,
    HartonomousRelationship* results,
    int32_t capacity,
    int32_t* count);
```

#### hartonomous_find_by_type

Find relationships by type from a node.

```c
int hartonomous_find_by_type(
    int64_t from_high, int64_t from_low,
    int16_t rel_type,
    HartonomousRelationship* results,
    int32_t capacity,
    int32_t* count);
```

#### hartonomous_find_by_weight

Find relationships by weight range within a context.

```c
int hartonomous_find_by_weight(
    double min_weight, double max_weight,
    int64_t context_high, int64_t context_low,
    HartonomousRelationship* results,
    int32_t capacity,
    int32_t* count);
```

#### hartonomous_get_weight

Get weight between two specific nodes.

```c
int hartonomous_get_weight(
    int64_t from_high, int64_t from_low,
    int64_t to_high, int64_t to_low,
    int64_t context_high, int64_t context_low,
    double* weight);
```

Returns 1 if relationship not found (weight set to NaN).

### Trajectory Operations

#### hartonomous_build_trajectory

Build RLE-compressed trajectory from text.

```c
int hartonomous_build_trajectory(
    const char* text,
    int32_t text_len,
    HartonomousTrajectoryPoint* points,
    int32_t capacity,
    int32_t* point_count);
```

"Hello" → H(1), e(1), l(2), o(1) (4 points, not 5)

#### hartonomous_store_trajectory

Store trajectory as relationship with geometric path.

```c
int hartonomous_store_trajectory(
    int64_t from_high, int64_t from_low,
    int64_t to_high, int64_t to_low,
    const HartonomousTrajectoryPoint* points,
    int32_t point_count,
    double weight,
    int16_t rel_type,
    int64_t context_high, int64_t context_low);
```

#### hartonomous_trajectory_to_text

Expand RLE trajectory back to text.

```c
int hartonomous_trajectory_to_text(
    const HartonomousTrajectoryPoint* points,
    int32_t point_count,
    char* buffer,
    int32_t buffer_capacity,
    int32_t* text_len);
```

### Containment Queries

#### hartonomous_contains_substring

Check if any stored content contains a substring.

```c
int hartonomous_contains_substring(
    const char* text,
    int32_t text_len,
    int* exists);
```

#### hartonomous_find_containing

Find compositions containing a substring.

```c
int hartonomous_find_containing(
    const char* text,
    int32_t text_len,
    int64_t* results,  // [high0, low0, high1, low1, ...]
    int32_t capacity,
    int32_t* count);
```

## C++ API

### QueryStore

High-level database interface.

```cpp
#include "hartonomous/db/query_store.hpp"

using namespace hartonomous::db;

QueryStore store;

// Encode and store
NodeRef root = store.encode_and_store("Hello, world!");

// Check existence
bool exists = store.exists(root);

// Decode
std::vector<uint8_t> decoded = store.decode(root);

// Find relationships
std::vector<Relationship> rels = store.find_from(root, 100);
std::vector<Relationship> incoming = store.find_to(root, 100);

// Get weight
std::optional<double> weight = store.get_weight(from, to, context);
```

### ModelIngester

AI model ingestion.

```cpp
#include "hartonomous/model/model_ingest.hpp"

using namespace hartonomous::model;

QueryStore store;
ModelIngester ingester(store, 1e-6);  // sparsity threshold

// Ingest entire model package
ModelResult result = ingester.ingest_package("/path/to/model");

printf("Tokens: %zu\n", result.vocab.token_count);
printf("Tensors: %zu\n", result.tensor_count);
printf("Weights stored: %zu\n", result.stored_weights);
printf("Compression: %.1fx\n", result.total_weights / result.stored_weights);

// Get model context for queries
NodeRef model_id = ingester.model_context();
```

### CompositionStore

In-memory composition cache for encoding.

```cpp
#include "hartonomous/atoms/composition_store.hpp"

CompositionStore cache;

// Build tree
NodeRef a = NodeRef::atom(SemanticDecompose::get_atom_id('a'));
NodeRef b = NodeRef::atom(SemanticDecompose::get_atom_id('b'));
NodeRef ab = cache.get_or_create({a, b});

// Lookup
auto children = cache.find(ab);  // Returns {a, b}
```

### Threading

Parallel execution utilities.

```cpp
#include "hartonomous/threading/threading.hpp"

using namespace hartonomous;

// Physical core count (not hyperthreads)
size_t cores = Threading::physical_core_count();

// Parallel for with work-stealing
Threading::parallel_for(items.size(), [&](size_t i) {
    process(items[i]);
});
```

### SemanticDecompose

Codepoint semantic analysis.

```cpp
#include "hartonomous/atoms/semantic_decompose.hpp"

// Decompose codepoint
SemanticCoord coord = SemanticDecompose::decompose('à');
// coord.page = 0 (Latin)
// coord.type = 5 (Letter)
// coord.base = 0x61 ('a')
// coord.variant = 8 (lowercase + grave)

// Get AtomId
AtomId id = SemanticDecompose::get_atom_id('à');

// Reverse lookup
int32_t codepoint = SemanticDecompose::atom_to_codepoint(id);
// codepoint == 0xE0 ('à')
```

## Thread Safety

All C API functions are thread-safe. Database connections use thread-local storage.

The C++ classes have the following thread-safety guarantees:

| Class | Thread-Safe | Notes |
|-------|-------------|-------|
| QueryStore | Yes | Uses connection pool |
| CompositionStore | Yes | Single lock pattern |
| ModelIngester | No | Use one per thread |
| SemanticDecompose | Yes | Pure functions |
| Threading::parallel_for | Yes | By design |

## Environment Variables

| Variable | Default | Description |
|----------|---------|-------------|
| HARTONOMOUS_DB | postgresql://localhost/hartonomous | Database connection string |
| PGHOST | localhost | PostgreSQL host |
| PGPORT | 5432 | PostgreSQL port |
| PGUSER | (system user) | PostgreSQL user |
| PGPASSWORD | (none) | PostgreSQL password |
| PGDATABASE | hartonomous | PostgreSQL database |
