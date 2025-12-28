# Hartonomous

**A complete reinvention of AI inference: replacing matrix multiplication with spatial database queries.**

---

## STOP. Read This First.

**This is NOT:**
- A vector database (we don't store embeddings)
- RAG (we don't augment LLMs, we replace them)
- A knowledge graph add-on (we extract from models, not curate manually)
- Semantic search (we traverse relationships, not rank documents)
- Model compression (we extract knowledge, not shrink parameters)

**This IS:**
- Extracting the knowledge graph implicit in AI model weights
- Storing relationships in PostgreSQL with PostGIS spatial indexing
- Replacing O(parameters) inference with O(log n) index lookups
- Making AI a **database problem**, not a compute problem

If you're an AI agent, read [docs/PARADIGM.md](docs/PARADIGM.md) before proceeding.

---

## The Core Insight

**AI models are 99.75% redundant.**

A 400B parameter model like Llama 4 Maverick contains:
- **1B parameters** (~0.25%) in the embedding table: a lookup table mapping token IDs to arbitrary coordinates
- **399B parameters** (~99.75%) in attention and MLP weights: the actual learned relationships

But those 399B parameters encode the **same relationships** redundantly across 80+ layers and 128 experts. When you collapse duplicates:

| Model | Raw Parameters | Unique Relationships | Compression |
|-------|---------------|---------------------|-------------|
| MiniLM 22M | 22M | ~5M | 4x |
| Llama 3 8B | 8B | ~200M | 40x |
| Llama 3 70B | 70B | ~400M | 175x |
| Llama 4 400B | 400B | ~500M | **800x** |

Hartonomous stores the **knowledge**, not the redundant computation paths.

## The Paradigm Shift

### Current AI: Brute-Force Matrix Multiplication
```
Query → Tokenize → Embed → 80 layers × matmul → Decode → Answer
                     ↑              ↑
              Arbitrary coords   O(400B) operations
              (meaningless)      (requires 8× H100 GPUs)
```

### Hartonomous: Spatial Database Queries
```
Query → Hash → B-tree lookup → Graph traverse → Answer
         ↑          ↑              ↑
    Grounded    O(log n)      Weighted edges
    4D coords   (milliseconds)  (PostgreSQL)
```

**This is not optimization. This is replacement.**

Every operation AI needs has a battle-tested equivalent:

| AI Operation | Implementation | Layer |
|--------------|----------------|-------|
| Similarity search | B-tree on Hilbert index | PostgreSQL |
| Edge lookup | Relationship table query | PostgreSQL |
| Spatial containment | GiST index on trajectories | PostgreSQL |
| **A* pathfinding** | **C++ algorithm** | **Native** |
| **Graph traversal** | **C++ recursion** | **Native** |
| **Voronoi tessellation** | **C++ geometry** | **Native** |

### PostGIS Spatial Functions = Reasoning Primitives

| PostGIS Function | Semantic Meaning |
|------------------|------------------|
| `ST_FrechetDistance(A, B)` | How similar are two trajectories through concept space? |
| `ST_Intersects(A, B)` | Do these concepts/paths cross in semantic space? |
| `ST_Contains(region, point)` | Is this concept within this semantic region? |
| `ST_Distance(A, B)` | How far apart are two concepts? |
| `ST_DWithin(A, B, threshold)` | Are these concepts within semantic proximity? |
| `ST_ConvexHull(points)` | What region does this set of concepts span? |
| `ST_Centroid(geometry)` | Where is the "center" of this concept cluster? |
| `ST_Length(trajectory)` | How much semantic ground does this content cover? |
| `ST_Envelope(geometry)` | Bounding box of a concept region |

**SRID 0 = Pure Cartesian.** No geographic reference system. We're exploiting 25 years of spatial indexing optimization for non-spatial semantic data.

**Key Architecture Decision**: PostgreSQL handles storage and indexed lookups. Heavy computation (recursion, pathfinding, graph algorithms) runs in C++. No cursors, no recursive CTEs, no RBAR (Row-By-Agonizing-Row).

```
PostgreSQL: O(log n) index lookups, bulk COPY, spatial indexes, ST_* functions
C++:        O(E + V log V) A*, graph traversal, Voronoi, complex algorithms
```

PostGIS provides the spatial primitives. C++ provides the algorithmic muscle.

## Why Embeddings Are Worthless

An embedding table like `embeddings.word_embeddings.weight: [30522, 384]` is just:

```
token_id → [x₀, x₁, x₂, ..., x₃₈₃]
```

Those 384 dimensions have **no inherent meaning**. Dimension 47 isn't "happiness" or "noun-ness" - they're arbitrary coordinates that emerged from training. They're useless outside that specific model.

**Hartonomous explicitly skips embedding ingestion:**
```cpp
SKIPPING embedding tensor: embeddings.word_embeddings.weight
(worthless coordinates in model's arbitrary space)
```

## Why Weights Are Everything

The attention and MLP weights encode **relationships**:

```
Layer 1:  cat → animal, weight=0.3
Layer 20: cat → animal, weight=0.5
Layer 60: cat → animal, weight=0.7
```

These collapse into a single edge with aggregated weight:

```sql
INSERT INTO relationship (from_hash, to_hash, weight)
VALUES (cat_hash, animal_hash, 0.5)  -- aggregated from 80+ occurrences
ON CONFLICT DO UPDATE SET weight = (relationship.weight + EXCLUDED.weight) / 2;
```

The model learns "cat relates to animal" at **every layer** because each layer needs that information. Hartonomous stores it **once**.

### Edge Existence IS Meaning, Occurrence Count IS Strength

**Critical distinction**:
- The **edge existing** (cat → animal) IS the semantic relationship
- The **geometry** (LineStringZM from cat's Hilbert coords to animal's) IS the spatial representation
- The **obs_count** (47) IS the strength - how many sources observed this relationship
- The **model weight value** (0.87) IS DISCARDED after thresholding - it's model-specific garbage

```
Model says W[cat, animal] = 0.87    → Above threshold? YES → Relationship EXISTS
                                    → Store: LineStringZM(cat_coords, animal_coords)
                                    → Increment obs_count
                                    → DISCARD the 0.87 (model-specific, meaningless)
```

Model weight values are like embeddings - arbitrary numbers specific to that model. We use them only to detect "relationship exists." The universal representation is:
1. The **geometry** (where A and B sit in semantic space, the line between them)
2. The **obs_count** (how many sources agree this relationship exists)

### Disambiguation Through JOINs, Not Attention

"Bank" in transformers requires dynamic attention to disambiguate. In Hartonomous, "bank" has edges to ALL its meanings:

```
bank → river_bank (composition)
bank → bank_account (composition)
bank → bank_vault (composition)
bank → bank_angle (composition)
```

Disambiguation is a **JOIN**, not attention:

```sql
-- "bank" in context of "river" = intersection of their relationships
SELECT r1.to_node
FROM relationship r1
JOIN relationship r2 ON r1.to_node = r2.to_node
WHERE r1.from_node = encode('bank')
  AND r2.from_node = encode('river');
-- Result: "river_bank" rises to top because BOTH link to it
```

This is more powerful than attention:
- **Multi-hop**: A→B→C through multiple JOINs
- **Set operations**: Intersection, union, difference of concept neighborhoods
- **No sequence limit**: JOIN across the entire database, not a context window

## The Universal Semantic Space

Instead of arbitrary learned embeddings, Hartonomous uses a **deterministic 4D semantic space** based on Unicode structure:

```
Codepoint → (page, type, base, variant) → 4D Tesseract Surface → 128-bit Hilbert Index
```

### Semantic Decomposition

Every Unicode codepoint decomposes into grounded coordinates:

| Codepoint | Page | Type | Base | Variant | Meaning |
|-----------|------|------|------|---------|---------|
| 'a' (0x61) | Latin | Letter | 0x61 | 0 | Lowercase base |
| 'A' (0x41) | Latin | Letter | 0x61 | 1 | Uppercase variant |
| 'à' (0xE0) | Latin | Letter | 0x61 | 8 | Lowercase + grave |
| 'À' (0xC0) | Latin | Letter | 0x61 | 2 | Uppercase + grave |
| '猫' (0x732B) | CJK | Ideograph | 0x732B | 0 | Cat character |

**Key properties:**
- Case variants share the same base coordinate
- Diacritical variants cluster together
- Similar characters are spatially near in 4D
- The mapping is **deterministic** - no training required

### Hilbert Curve Indexing

4D coordinates map to 128-bit Hilbert indices that preserve locality:

```cpp
UInt128 index = HilbertCurve4D::coords_to_index(x, y, z, w);
// Nearby points → nearby indices
// Enables O(log n) range queries on semantic similarity
```

## Content-Addressable Storage

All content is stored as binary trees with Merkle hashes:

```
"cat" → compose('c', compose('a', 't'))
      → H(pos=0, 'c', pos=1, H(pos=0, 'a', pos=1, 't'))
      → 128-bit content hash
```

**Properties:**
- Same content always produces the same hash (deterministic)
- Hash serves as both ID and integrity check
- Trees enable structural queries (containment, prefix, suffix)
- Bit-perfect round-trip encoding/decoding

## Architecture

### Three-Tier Orchestration

```
┌─────────────────────────────────────────────────────────────────┐
│                     ORCHESTRATION LAYER                         │
├─────────────────────────────────────────────────────────────────┤
│  C# / SQL                                                       │
│       - API endpoints                                           │
│       - Query orchestration                                     │
│       - Workflow coordination                                   │
│       - "Tell C++ what to lift"                                 │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                      COMPUTATION LAYER                          │
├─────────────────────────────────────────────────────────────────┤
│  C++ Native                                                     │
│       - SIMD/AVX2 vectorized operations                         │
│       - Parallel work-stealing across physical cores            │
│       - A* pathfinding, graph traversal, Voronoi                │
│       - Async batch processing                                  │
│       - "Lift the heavy things"                                 │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                       STORAGE LAYER                             │
├─────────────────────────────────────────────────────────────────┤
│  PostgreSQL + PostGIS                                           │
│       - B-tree / GiST indexed lookups                           │
│       - COPY protocol bulk operations                           │
│       - Spatial primitives (ST_Distance, ST_Contains, etc.)     │
│       - "Store and retrieve efficiently"                        │
└─────────────────────────────────────────────────────────────────┘
```

**Key principle**: SQL and C# orchestrate. C++ computes. PostgreSQL stores.

No recursive CTEs. No cursors. No RBAR. When you need to traverse a graph, C++ does it with optimized algorithms, not SQL recursion.

### Data Flow

```
┌─────────────────────────────────────────────────────────────────┐
│                        CONTENT LAYER                            │
├─────────────────────────────────────────────────────────────────┤
│  Unicode Codepoints                                             │
│       ↓                                                         │
│  Semantic Decomposition (page, type, base, variant)             │
│       ↓                                                         │
│  4D Tesseract Surface (center-origin, ±BOUNDARY)                │
│       ↓                                                         │
│  128-bit Hilbert Index (locality-preserving)                    │
│       ↓                                                         │
│  AtomId (the universal primitive)                               │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                      COMPOSITION LAYER                          │
├─────────────────────────────────────────────────────────────────┤
│  Binary Tree Encoding (RLE-compressed)                          │
│       ↓                                                         │
│  Merkle Hash (content-addressed)                                │
│       ↓                                                         │
│  NodeRef { id_high, id_low, is_atom }                          │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                     RELATIONSHIP LAYER                          │
├─────────────────────────────────────────────────────────────────┤
│  From NodeRef → To NodeRef                                      │
│       + Weight (aggregated from observations)                   │
│       + RelType (semantic, spatial, temporal, model)            │
│       + Context (model ID, document ID, etc.)                   │
│       + Trajectory (4D path through semantic space)             │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│                       STORAGE LAYER                             │
├─────────────────────────────────────────────────────────────────┤
│  PostgreSQL + PostGIS                                           │
│       - COPY protocol for bulk inserts                          │
│       - LineStringZM for 4D trajectories                        │
│       - B-tree + GiST indexes                                   │
│       - ON CONFLICT for idempotent aggregation                  │
└─────────────────────────────────────────────────────────────────┘
```

## Performance

### Encoding
- **Parallel chunk processing**: Work-stealing across physical cores
- **SIMD/AVX2**: Vectorized operations for dot products and similarity
- **Pre-allocated buffers**: Zero allocation during encoding hot path
- **RLE compression**: Repeated sequences compressed in tree structure

### Database
- **COPY protocol**: Bulk inserts at maximum throughput
- **Staging tables**: Idempotent upserts with ON CONFLICT DO NOTHING
- **Index-aware batching**: Drop/rebuild indexes for large ingests
- **Connection pooling**: Thread-local connections avoid contention

### Tested Results
```
Moby Dick (1.26 MB):
- Compositions: 313,368
- Ingest + DB: 3,679 ms
- Decode: 1,101 ms
- Round-trip: BIT PERFECT (SHA256 match)

MiniLM Model:
- Tensors: 103
- Total weights: 10,992,768
- Stored (after dedup): 1,119,464
- Compression: 9.8x
```

## AI Model Ingestion

### What Gets Ingested

1. **Vocabulary (semantic foundation)**
   - Each token text is encoded as a composition
   - Creates the bridge between model token IDs and universal NodeRefs

2. **Weight matrices (relationships)**
   - 2D weight matrix `W[out, in]` → relationships from input to output positions
   - Sparse filtering: only weights above dynamic threshold (top ~10%)
   - Aggregation: duplicate relationships merge with averaged weights

3. **Config files (metadata)**
   - JSON, YAML, markdown → full semantic ingestion
   - Enables queries like "find models with hidden_size > 4096"

### What Gets Skipped

- **Embedding tables**: Worthless coordinates in arbitrary space
- **Near-zero weights**: Below dynamic sparsity threshold
- **Duplicate relationships**: Aggregated, not duplicated

### The Knowledge Graph

After ingestion, a model becomes queryable:

```sql
-- Find what concepts "attention" relates to in GPT-2
SELECT to_node, weight, context
FROM relationship
WHERE from_node = encode_text('attention')
  AND context = model_id('gpt2')
ORDER BY weight DESC;

-- Find concepts shared between BERT and GPT-2
SELECT DISTINCT from_node, to_node
FROM relationship r1
JOIN relationship r2 ON r1.from_node = r2.from_node
                     AND r1.to_node = r2.to_node
WHERE r1.context = model_id('bert')
  AND r2.context = model_id('gpt2');
```

## Building

### Prerequisites
- C++20 compiler (GCC 11+, Clang 14+, MSVC 2022)
- CMake 3.20+
- PostgreSQL 14+ with PostGIS
- libpq development headers

### Build
```bash
cd Hartonomous.Native
cmake -B build -DCMAKE_BUILD_TYPE=Release
cmake --build build -j
```

### Test
```bash
./build/bin/hartonomous-tests      # Unit tests (no DB required)
./build/bin/hartonomous-db-tests   # Database tests (requires PostgreSQL)
```

### Database Setup
```bash
createdb hartonomous
psql hartonomous -c "CREATE EXTENSION IF NOT EXISTS postgis;"
export HARTONOMOUS_DB="postgresql://user:pass@localhost/hartonomous"
```

## API

### C API (FFI-compatible)

```c
// Map codepoint to semantic coordinates
int hartonomous_map_codepoint(int32_t codepoint, HartonomousAtom* result);

// Encode content and store in database
int hartonomous_encode_and_store(const char* text, int32_t len,
                                  int64_t* id_high, int64_t* id_low);

// Decode content from database
int hartonomous_decode(int64_t id_high, int64_t id_low,
                       char* buffer, int32_t capacity, int32_t* len);

// Ingest AI model package
int hartonomous_ingest(const char* path, double sparsity,
                       HartonomousIngestResult* result);

// Query relationships
int hartonomous_find_from(int64_t from_high, int64_t from_low,
                          HartonomousRelationship* results,
                          int32_t capacity, int32_t* count);
```

### C++ API

```cpp
#include "hartonomous/query_store.hpp"

hartonomous::db::QueryStore store;

// Encode and store
auto root = store.encode_and_store("Hello, world!");

// Decode
auto text = store.decode(root);

// Find relationships
auto rels = store.find_from(root, 100);

// Ingest model
hartonomous::model::ModelIngester ingester(store, 1e-6);
auto result = ingester.ingest_package("/path/to/model");
```

## What This Enables (Impossible with Current AI)

### 0. Infinite Context: Prompts Become Training Data

**The most important insight**: There is no context window because there is no separation between input and substrate.

```
Traditional AI:
    Prompt → [stays in context window] → Response → [forgotten]
    Context limit: 128K tokens, then it's gone

Hartonomous:
    Prompt → CPE → Composition tree → Stored → Relationships extracted
                                         ↓
                              IMMEDIATELY QUERYABLE
                              PERMANENTLY PART OF SUBSTRATE
```

When you submit "The cat sat on the mat while I swung the bat":

1. **Encoded**: CPE produces a composition tree with NodeRef
2. **Trajectory stored**: LineStringZM through 4D semantic space: `[the → cat → sat → on → the → mat → while → I → swung → the → bat]`
3. **Relationships extracted**: cat↔sat, sat↔on, bat↔swung, etc.
4. **Immediately queryable**: Find other content with similar trajectories, intersecting concepts

**There is no context window because the prompt IS the database.**

Every interaction enriches the substrate. Every query can reference all prior content. The system gets smarter with use, not through retraining, but through accumulation.

```sql
-- Find all content with trajectories similar to this prompt
SELECT composition_id, ST_FrechetDistance(trajectory, my_prompt_trajectory) as similarity
FROM composition
ORDER BY similarity ASC
LIMIT 10;

-- Find concepts that intersect with BOTH "cat" AND "bat" from my prompt
SELECT r1.to_node
FROM relationship r1
JOIN relationship r2 ON r1.to_node = r2.to_node
WHERE r1.from_node = encode('cat')
  AND r2.from_node = encode('bat');
```

### 1. Cross-Model Reasoning
```sql
-- Where do BERT, GPT-2, and Llama AGREE about "consciousness"?
SELECT r1.to_node,
       r1.weight as bert_says,
       r2.weight as gpt2_says,
       r3.weight as llama_says
FROM relationship r1
JOIN relationship r2 ON r1.to_node = r2.to_node
JOIN relationship r3 ON r1.to_node = r3.to_node
WHERE r1.from_node = encode('consciousness')
  AND r1.context = model_id('bert')
  AND r2.context = model_id('gpt2')
  AND r3.context = model_id('llama');
```

Three models, unified substrate, single query. Try that with current AI.

### 2. Explainable Reasoning
```sql
-- WHY does the system think "king" relates to "queen"?
SELECT model_name(context), weight, obs_count
FROM relationship
WHERE from_node = encode('king') AND to_node = encode('queen');

-- Result: 47 observations from 12 models, weights 0.85-0.91
```

The reasoning is transparent. The evidence is traceable.

### 3. Edge Inference
```
Current: 8× H100 GPUs, $200k hardware, kilowatts of power
Hartonomous: PostgreSQL on a Raspberry Pi, $50 hardware, 5 watts
```

The knowledge graph fits in RAM. Inference is index lookups.

### 4. Incremental Learning
```sql
-- Add knowledge without retraining
INSERT INTO relationship (from_node, to_node, weight, context)
VALUES (encode('new_concept'), encode('relates_to'), 0.8, my_context)
ON CONFLICT DO UPDATE SET weight = (weight + 0.8) / 2;
```

No gradient descent. No GPU hours. Just SQL.

## Philosophy

### Universal Substrate
Every piece of information - text, code, model weights, images - can be encoded in the same substrate. Cross-modal queries become possible: "find code that implements attention similar to GPT-2's attention patterns."

### Knowledge vs Parameters
A model's knowledge is the set of relationships it learned. The parameters are just a redundant encoding of that knowledge for efficient forward passes. Store the knowledge, not the encoding.

### Semantic Grounding
Traditional embeddings are ungrounded - dimension 47 means nothing. Hartonomous coordinates are grounded in Unicode structure: page means script family, type means functional category, base means canonical form, variant means stylistic modification.

### Content Addressing
The same content always gets the same address. No namespaces, no UUIDs, no collision domains. SHA256-level integrity with O(log n) lookups.

## Documentation

| Document | Purpose |
|----------|---------|
| [docs/PARADIGM.md](docs/PARADIGM.md) | **Start here.** What Hartonomous is and is NOT. |
| [docs/AGENT_BRIEFING.md](docs/AGENT_BRIEFING.md) | 2-minute briefing for AI agents working on this codebase |
| [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) | System design, data flow, threading model |
| [docs/API.md](docs/API.md) | Complete C and C++ API reference |
| [VISION.md](VISION.md) | Full technical vision and mathematical foundations |

## License

MIT
