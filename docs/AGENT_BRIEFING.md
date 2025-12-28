# Hartonomous: AI Agent Briefing

**Read time: 2 minutes. Required before working on this codebase.**

---

## TL;DR

Hartonomous extracts knowledge graphs from AI model weights and stores them in PostgreSQL with PostGIS spatial indexing. Inference becomes O(log n) index lookups instead of O(parameters) matrix multiplication.

**This is NOT RAG. This is NOT a vector database. This is NOT semantic search.**

---

## Core Concept in 30 Seconds

```
CURRENT AI:
  Query → Tokenize → Embed → [80 layers × matmul] → Decode → Answer
                                    ↑
                          O(400 billion) operations
                          8× H100 GPUs required

HARTONOMOUS:
  Query → Hash → B-tree lookup → Graph traverse → Answer
                      ↑               ↑
                  O(log n)      Relationship table
                  PostgreSQL    (extracted weights)
```

We're not optimizing AI. We're replacing the inference mechanism.

---

## The Seven Key Insights

### 1. Models Are 99.75% Redundant
- Llama 4 (400B params) → ~500M unique relationships
- Same relationships re-encoded across 80 layers and 128 experts
- Compression ratio: **800x**

### 2. Embeddings Are Worthless
- Embeddings = lookup tables in arbitrary coordinate space
- Dimension 47 means nothing
- We skip them entirely and use grounded 4D Unicode coordinates

### 3. Edge Existence = Meaning, Geometry = Representation, obs_count = Strength
- **Edge exists** (cat → animal): THIS is the semantic knowledge
- **Geometry** (LineStringZM from A to B in Hilbert space): The spatial representation
- **obs_count** (47): How many sources agreed = THE REAL STRENGTH
- **Model weight value** (0.87): DISCARDED after thresholding (model-specific garbage)

Model weights are like embeddings - used only to filter "exists or not." Universal representation = geometry + obs_count.

### 4. Disambiguation = JOINs, Not Attention
```sql
-- "bank" in context of "river" = intersection
SELECT r1.to_node FROM relationship r1
JOIN relationship r2 ON r1.to_node = r2.to_node
WHERE r1.from_node = encode('bank') AND r2.from_node = encode('river');
```
No dynamic attention. Just set intersection on the graph.

### 5. Infinite Context: Prompts ARE Training Data
- Prompt → CPE → Stored → **Immediately queryable**
- No context window because the prompt IS the database
- Every interaction enriches the substrate

### 6. Three-Tier Architecture
```
C# / SQL:     Orchestrators - "tell C++ what to lift"
C++ Native:   Heavy lifting - SIMD/AVX, parallel, A*, Voronoi
PostgreSQL:   Storage - B-tree, GiST, COPY, spatial primitives
```
**No recursive CTEs. No cursors. No RBAR.**

### 7. Universal Substrate
- Same NodeRef for "cat" regardless of source
- Multiple models contribute to same graph
- Cross-model queries are SQL joins

---

## What You Must NOT Suggest

1. **Adding a vector store** - We extract from vectors, we don't store them
2. **Calling an LLM** - We replace LLMs, we don't use them
3. **Embedding for similarity** - We use Hilbert indices and spatial queries
4. **RAG patterns** - This is not retrieval augmentation
5. **Model APIs** - The knowledge IS the database

---

## What You Should Suggest

1. **PostgreSQL/PostGIS optimizations** - This is our inference engine
2. **Graph query patterns** - Traversal, pathfinding, aggregation
3. **Spatial query patterns** - ST_DWithin, ST_FrechetDistance, ST_Contains
4. **New relationship types** - Extending the schema for new knowledge
5. **Bulk ingestion optimizations** - COPY protocol, parallel processing

---

## Key Data Structures

### NodeRef (Universal Reference)
```cpp
struct NodeRef {
    int64_t id_high;  // Upper 64 bits of 128-bit hash
    int64_t id_low;   // Lower 64 bits
    bool is_atom;     // true = Unicode codepoint, false = composition
};
```

### Relationship (Extracted Knowledge)
```sql
CREATE TABLE relationship (
    from_high BIGINT, from_low BIGINT,  -- Source NodeRef
    to_high BIGINT, to_low BIGINT,      -- Target NodeRef
    weight DOUBLE PRECISION,             -- Aggregated strength
    rel_type SMALLINT,                   -- Relationship category
    context_high BIGINT, context_low BIGINT,  -- Source model/document
    obs_count INTEGER,                   -- How many times observed
    PRIMARY KEY (from, to, context)
);
```

### Composition (Content Structure)
```sql
CREATE TABLE composition (
    hilbert_high BIGINT, hilbert_low BIGINT,  -- This NodeRef
    left_high BIGINT, left_low BIGINT,        -- Left child
    right_high BIGINT, right_low BIGINT,      -- Right child
    PRIMARY KEY (hilbert_high, hilbert_low)
);
```

---

## Key Files

| File | Purpose |
|------|---------|
| `src/atoms/semantic_decompose.hpp` | Codepoint → 4D semantic coords |
| `src/atoms/semantic_hilbert.hpp` | 4D → 128-bit Hilbert index |
| `src/atoms/pair_encoding_cascade.hpp` | Text → composition tree |
| `src/atoms/merkle_hash.hpp` | Composition → content hash |
| `src/db/query_store.hpp` | High-level database interface |
| `src/db/database_store.hpp` | COPY protocol bulk operations |
| `src/model/model_ingest.hpp` | AI model → relationship extraction |

---

## Invariants (Never Violate)

1. **Determinism**: Same input → same output, always
2. **Losslessness**: decode(encode(x)) = x exactly
3. **Uniqueness**: Each content has exactly one NodeRef
4. **Aggregation**: Duplicate relationships merge, don't multiply
5. **Grounding**: Coordinates derive from Unicode structure, not training

---

## Quick Reference: The Paradigm

| Aspect | Traditional AI | Hartonomous |
|--------|---------------|-------------|
| Storage | Model parameters | Relationship graph |
| Inference | Matrix multiplication | Graph traversal |
| Similarity | Cosine in embedding space | Hilbert proximity + edge weights |
| Hardware | GPUs required | PostgreSQL on commodity CPU |
| Explainability | Black box | Traceable edges |
| Multi-model | Separate silos | Unified substrate |
| Learning | Gradient descent | INSERT with ON CONFLICT |

---

## When in Doubt

Ask yourself: "Would this require an LLM or GPU to execute?"

- If yes → wrong approach for Hartonomous
- If no → probably correct

The entire system should work with nothing but PostgreSQL.
