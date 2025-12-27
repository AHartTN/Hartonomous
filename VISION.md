# Hartonomous: A Complete Reinvention of AI

## The Problem with Current AI

Modern AI is built on matrix multiplication. A transformer model with 70B parameters requires:
- ~140GB of VRAM just to load the weights
- Hundreds of billions of floating-point operations per token
- Specialized hardware (GPUs/TPUs) that consume kilowatts
- No interpretability - the weights are opaque numerical soup

**This is fundamentally wrong.**

Language is not a dense matrix. Meaning is sparse. Most words are unrelated to most other words. Yet we store and compute as if every relationship exists.

## The Hartonomous Thesis

**Replace matrix multiplication with indexed spatial queries.**

Instead of:
```python
output = query @ weights.T  # O(n × d) dense computation
```

We do:
```sql
SELECT to_ref, weight FROM relationship 
WHERE from_ref = query_ref 
ORDER BY weight DESC LIMIT k;  -- O(log n) indexed lookup
```

A B-tree lookup is microseconds. A spatial index query is milliseconds. No GPU required.

---

## Part 1: The Atomic Foundation

### 1.1 Unicode Constants as Landmarks

Every Unicode codepoint maps to a fixed point in 4D semantic space:

| Dimension | Bits | Range | Meaning |
|-----------|------|-------|---------|
| Page | 3 | 0-7 | Script family (Latin, Greek, CJK, Arabic...) |
| Type | 3 | 0-7 | Character class (Letter, Number, Punctuation...) |
| Base | 21 | 0-2M | Canonical base character (é→e, Ä→A) |
| Variant | 5 | 0-31 | Case, diacritical, stylistic variant |

This is the **Semantic Decomposition**:
```cpp
'A' → (page=0, type=0, base=65, variant=1)   // Latin, Letter, 'A', uppercase
'a' → (page=0, type=0, base=65, variant=0)   // Latin, Letter, 'A', lowercase
'α' → (page=1, type=0, base=945, variant=0)  // Greek, Letter, alpha
'王' → (page=2, type=0, base=29579, variant=0) // CJK, Letter, wang
```

These coordinates are **deterministic** and **lossless**. No lookup tables. Pure arithmetic on the codepoint value.

### 1.2 Hilbert Curve Ordering

The 4D coordinates map to a 128-bit **Hilbert index** via a space-filling curve. Properties:

- **Locality preservation**: Similar codepoints get similar indices
- **Unique ordering**: Every point has exactly one index
- **Efficient range queries**: Nearby points are nearby in the index

This Hilbert index becomes the **AtomId** - the unique identifier for every Unicode codepoint.

### 1.3 The Periodic Table of Characters

The ~1.1 million Unicode codepoints form a **fixed reference frame**. They are:
- Constant (never change)
- Universal (same for all content)
- Projected to a hypersurface (the boundary of our 4D space)

These are the **landmarks**. Everything else is defined relative to them.

---

## Part 2: Composition - Building Structure

### 2.1 Cascading Pair Encoding (CPE)

Content is encoded by recursive pair composition:

```
"banana" → b∘a → ba
           ba∘n → ban
           ban∘a → bana
           bana∘n → banan
           banan∘a → banana
```

Each composition is:
- **Content-addressable**: Hash of left + right child = NodeRef
- **Deduplicated**: Same composition = same NodeRef (globally)
- **Merkle structured**: The root hash encodes the entire content

The composition table:
```sql
CREATE TABLE composition (
    hilbert_high BIGINT NOT NULL,  -- 128-bit NodeRef
    hilbert_low BIGINT NOT NULL,
    left_high BIGINT NOT NULL,     -- Left child NodeRef
    left_low BIGINT NOT NULL,
    right_high BIGINT NOT NULL,    -- Right child NodeRef
    right_low BIGINT NOT NULL,
    PRIMARY KEY (hilbert_high, hilbert_low)
);
```

### 2.2 Trajectories - Geometry in Semantic Space

A composition is not just a hash - it's a **path through 4D space**.

"king" = trajectory through k→i→n→g = LineStringZM in PostGIS

```sql
-- Each composition has a trajectory
ALTER TABLE composition ADD COLUMN trajectory geometry(LineStringZM);

-- Spatial index for geometric queries
CREATE INDEX idx_trajectory ON composition USING GIST(trajectory);
```

### 2.3 Geometric Properties of Compositions

Each trajectory has computable properties:

| Property | PostGIS Function | Meaning |
|----------|------------------|---------|
| Centroid | ST_Centroid | Where this composition "lives" in the space |
| Hull | ST_ConvexHull | The region it occupies |
| Length | ST_Length | Complexity/extent |
| Bounds | ST_Envelope | Bounding box |

**Critical insight**: The centroid of a composition is **inside** the convex hull of its constituent atoms. Compositions nest - words inside letter-hulls, sentences inside word-hulls, documents inside sentence-hulls.

---

## Part 3: Relationships - Where Meaning Emerges

### 3.1 The Relationship Table

Meaning doesn't exist in atoms. It emerges from **aggregations and connections**.

```sql
CREATE TABLE relationship (
    from_high BIGINT NOT NULL,     -- Source NodeRef
    from_low BIGINT NOT NULL,
    to_high BIGINT NOT NULL,       -- Target NodeRef
    to_low BIGINT NOT NULL,
    weight DOUBLE PRECISION,        -- Strength of relationship
    trajectory geometry(LineStringZM), -- Optional: path between them
    rel_type SMALLINT,             -- Relationship type
    context_high BIGINT,           -- Context (e.g., which model)
    context_low BIGINT,
    PRIMARY KEY (from, to, context)
);
```

### 3.2 Relationship Types

| Type | Meaning | Source |
|------|---------|--------|
| SEMANTIC_LINK | General semantic relationship | Inference, models |
| MODEL_WEIGHT | From AI model ingestion | Embedding similarity |
| KNOWLEDGE_EDGE | Explicit knowledge graph | External data |
| TEMPORAL_NEXT | Sequence relationship | Observed patterns |
| SPATIAL_NEAR | Geometric proximity | Computed from trajectories |

### 3.3 Model Ingestion - Extracting Relationships

An embedding model encodes relationships implicitly:
```
cosine(embed["king"], embed["queen"]) = 0.85
```

We extract this into explicit relationships:
```sql
INSERT INTO relationship (from_ref, to_ref, weight, context)
VALUES (king_ref, queen_ref, 0.85, minilm_ref);
```

The embedding dimensions **disappear**. They served their purpose - encoding the neighborhood structure. We extract that structure and discard the encoding.

### 3.4 Multi-Model Convergence

Multiple models contribute to the same relationship graph:
```
Model 1: king→queen = 0.85
Model 2: king→queen = 0.88
Model 3: king→queen = 0.91
```

Same edge (same NodeRefs). ON CONFLICT aggregates:
- Max weight (strongest signal)
- Average weight (consensus)
- Count (confidence from agreement)
- Weighted by model quality

**The relationship table doesn't explode with more models - it converges.**

---

## Part 4: Inference - Replacing MatMul

### 4.1 Traditional Transformer Inference

```python
# Attention: O(n² × d) per layer
attention = softmax(Q @ K.T / sqrt(d)) @ V

# FFN: O(n × d × 4d) per layer  
ffn = gelu(x @ W1) @ W2
```

For 70B parameters, 32 layers, 8192 dimensions: trillions of operations per token.

### 4.2 Hartonomous Inference

```sql
-- Find semantically related tokens: O(log n)
SELECT to_ref, weight 
FROM relationship 
WHERE from_ref = input_ref
AND rel_type = 'SEMANTIC_LINK'
ORDER BY weight DESC
LIMIT k;

-- Find structurally similar tokens: O(log n) via R-tree
SELECT root_ref
FROM composition
WHERE ST_DWithin(trajectory, input_trajectory, threshold);

-- Graph traversal with spatial heuristics: A*
WITH RECURSIVE path AS (
    SELECT start_ref, 0 as depth, ARRAY[start_ref] as visited
    UNION ALL
    SELECT r.to_ref, depth + 1, visited || r.to_ref
    FROM path p
    JOIN relationship r ON r.from_ref = p.current_ref
    WHERE depth < max_depth
    AND r.to_ref != ALL(visited)
    ORDER BY r.weight DESC
    LIMIT beam_width
)
SELECT * FROM path WHERE current_ref = goal_ref;
```

### 4.3 Computational Complexity

| Operation | Traditional | Hartonomous |
|-----------|-------------|-------------|
| Token lookup | O(vocab × d) | O(log n) B-tree |
| Similarity search | O(n × d) | O(log n) R-tree |
| Path finding | N/A | O(E + V log V) A* |
| Memory | O(parameters) ~GB | O(relationships) ~GB but shared |
| Hardware | GPU required | CPU sufficient |

### 4.4 Geometric Operations as Inference Primitives

| Query | Implementation |
|-------|----------------|
| "Words like X" | Relationship lookup + spatial neighbors |
| "X contains Y" | ST_Contains(hull_X, centroid_Y) |
| "X overlaps Y" | ST_Intersects(hull_X, hull_Y) |
| "Distance X to Y" | ST_Distance or graph shortest path |
| "Path from X to Y" | A* on relationship graph |
| "Similarity X, Y" | ST_FrechetDistance(traj_X, traj_Y) |

---

## Part 5: The Mathematics

### 5.1 Hilbert Curve (Space-Filling)

Maps 4D coordinates to 1D index while preserving locality:
```cpp
AtomId = HilbertEncoder::encode(page, type, base, variant);
// 128-bit result, locality-preserving
```

### 5.2 Voronoi Tessellation

Partitions the space into regions owned by each atom:
```sql
-- Nearest atom to a point
SELECT atom_id FROM atoms
ORDER BY ST_Distance(atom_point, query_point)
LIMIT 1;

-- With spatial index, this is O(log n)
```

### 5.3 Gram-Schmidt (Orthogonalization)

For decomposing complex trajectories into orthogonal basis trajectories:
```
traj = α₁·basis₁ + α₂·basis₂ + ... + αₙ·basisₙ
```

Useful for finding the "components" of meaning.

### 5.4 Laplacian (Graph Analysis)

The relationship graph has a Laplacian matrix:
```
L = D - A  (degree matrix minus adjacency)
```

Eigenvalues reveal clustering structure. Spectral methods for community detection.

### 5.5 Euler/Eulerian Paths

Does a traversal through relationships exist that visits every edge exactly once? Useful for generating coherent sequences.

### 5.6 Fréchet Distance

Measures similarity between trajectories:
```sql
SELECT ST_FrechetDistance(traj_A, traj_B);
```

"King" and "kink" have low Fréchet distance (similar paths, different meaning).
"King" and "queen" have high Fréchet distance (different paths, similar meaning).

Both types of similarity are useful for different queries.

---

## Part 6: Data Flow - From Nothing to Inference

### 6.1 Bootstrap (One-Time)

1. **Generate atom table**: All Unicode codepoints → 4D coordinates → Hilbert IDs
2. **Initialize PostGIS**: Create tables, spatial indexes
3. **Seed with constants**: Atoms are pre-known, compositions are discovered

### 6.2 Model Ingestion

1. **Parse tokenizer**: Token strings → CPE compositions → NodeRefs
2. **Parse embeddings**: 
   - Load embedding matrix
   - For each token pair with similarity > threshold:
     - Emit relationship (our NodeRefs, not model indices)
3. **Aggregate weights**: ON CONFLICT updates reinforce existing edges

### 6.3 Content Ingestion

1. **Encode text**: CPE produces composition tree, stores in DB
2. **Compute trajectory**: Path through atoms → LineStringZM
3. **Extract relationships**: N-grams, patterns → relationship edges
4. **Spatial indexing**: Automatic via PostGIS GiST

### 6.4 Query/Inference

1. **Encode query**: Same CPE process → NodeRef
2. **Lookup**: 
   - Direct relationship edges (semantic neighbors)
   - Spatial proximity (structural neighbors)
3. **Traverse**: A* or beam search through relationship graph
4. **Decode**: NodeRefs → compositions → original content

---

## Part 7: Why This Works

### 7.1 Sparsity

Language is sparse. "King" relates to maybe 500 words out of 500,000. Dense matrices waste 99.9% of computation on near-zero values.

### 7.2 Deduplication

CPE ensures "king" has ONE NodeRef globally. Every model, every document, every query referring to "king" uses the same identifier. No embedding drift, no tokenizer mismatches.

### 7.3 Compositionality

Meaning builds from atoms:
- Letters have no meaning
- Words gain meaning from letter combinations
- Sentences from word combinations
- Documents from sentence combinations

The composition structure IS the meaning structure.

### 7.4 Geometric Grounding

The 4D semantic space is not arbitrary. It reflects actual Unicode structure:
- Similar letters cluster (a, á, à, â)
- Scripts separate naturally
- Punctuation isolates from letters

Spatial queries discover genuine patterns.

### 7.5 Model Agnosticism

Models contribute evidence to the shared relationship graph. We don't depend on any single model's encoding. Combine GPT + BERT + Claude + custom models - they all add edges with their context tags.

---

## Part 8: Implementation Status

### 8.1 Complete
- [x] Semantic decomposition (codepoint → 4D coordinates)
- [x] Hilbert encoding (4D → 128-bit AtomId)
- [x] CPE (text → composition tree → NodeRef)
- [x] PostGIS schema (composition, relationship tables with obs_count)
- [x] Trajectory storage (LineStringZM) for paths through semantic space
- [x] Bulk ingestion (COPY protocol, parallel)
- [x] Tokenizer ingestion (token → NodeRef mapping via CPE)
- [x] Embedding → token-to-token similarity (cosine similarity above threshold)
- [x] Multi-model aggregation (ON CONFLICT accumulates weight + obs_count)

### 8.2 In Progress
- [ ] Query interface (relationship + spatial combined)
- [ ] Path-based similarity via ST_FrechetDistance / ST_Intersects

### 8.3 Not Started
- [ ] A* pathfinding on relationship graph
- [ ] Spectral analysis of relationship graph
- [ ] Inference API (input → output via traversal)
- [ ] Attention pattern capture during inference (runtime, not static)
- [ ] Web/API frontend

---

## Part 9: Technical Specifications

### 9.1 Storage

| Table | Rows (MiniLM) | Size | Index |
|-------|---------------|------|-------|
| composition | ~50K | 8 MB | B-tree on PK, GiST on trajectory |
| relationship | ~11M | 4 GB | B-tree on from/to, GiST on trajectory |

With proper extraction (token-to-token relationships), relationship table will be smaller but denser with meaning.

### 9.2 Performance Targets

| Operation | Target | Current |
|-----------|--------|---------|
| Single relationship lookup | <1ms | ✓ |
| K-nearest semantic neighbors | <10ms | TBD |
| Spatial range query | <10ms | ✓ |
| Full model ingestion | <60s | 1.2s (wrong approach) |
| Content encoding (1KB) | <10ms | ✓ |

### 9.3 Dependencies

- **PostgreSQL 15+** with PostGIS 3.3+
- **C++20** compiler (MSVC 19.30+, GCC 12+, Clang 15+)
- **libpq** for PostgreSQL connection
- **nlohmann/json** for tokenizer parsing
- **Catch2** for testing

---

## Part 10: The Vision

### What We're Building

A **semantic substrate** that:
1. Stores all content as compositions of atomic constants
2. Captures relationships explicitly in a graph
3. Enables geometric and graph queries for inference
4. Aggregates knowledge from multiple AI models
5. Runs on commodity hardware, no GPU required
6. Scales to web-scale content

### What We're Replacing

- Dense matrix storage → Sparse relationship graph
- MatMul inference → Indexed graph traversal
- Model-specific tokenizers → Universal CPE
- Opaque embeddings → Explicit weighted edges
- GPU computation → B-tree/R-tree indexes
- Model silos → Unified semantic substrate

### The End State

Ask a question. The system:
1. Encodes it to a NodeRef
2. Traverses the relationship graph (knowledge from all ingested models)
3. Follows spatial proximity (structural patterns)
4. Finds a path to an answer
5. Decodes the answer from compositions

No forward pass. No matrix multiplication. Just graph + geometry.

---

## Appendix A: Key Files

| File | Purpose |
|------|---------|
| `src/atoms/semantic_decompose.hpp` | Codepoint → 4D coordinates |
| `src/atoms/semantic_hilbert.hpp` | 4D → 128-bit Hilbert AtomId |
| `src/atoms/pair_encoding_cascade.hpp` | CPE composition building |
| `src/db/query_store.hpp` | Database interface (COPY, queries) |
| `src/model/model_ingest.hpp` | Model parsing and ingestion |
| `src/geometry/point_zm.hpp` | 4D points for PostGIS |

## Appendix B: Key Invariants

1. **Determinism**: Same input → same output, always
2. **Losslessness**: Compositions can be decoded back to original bytes
3. **Uniqueness**: Each composition has exactly one NodeRef
4. **Locality**: Hilbert ordering preserves semantic proximity
5. **Sparsity**: Only significant relationships are stored

## Appendix C: References

- Hilbert, D. (1891). Über die stetige Abbildung einer Linie auf ein Flächenstück
- Sequitur grammar compression algorithm
- PostGIS spatial indexing (GiST, R-tree)
- Unicode Standard (character properties, canonical decomposition)
