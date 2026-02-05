# Hartonomous

**A universal intelligence substrate where meaning emerges from geometric relationships.**

This is not a database. This is not RAG. This is not vector similarity search.

**This is a new paradigm for intelligence.**

---

## What Is This?

Hartonomous replaces transformer-based AI with **relationship-driven graph navigation** on a geometric substrate.

**Core insight:** All digital content is Unicode → Atoms (geometric positions) → Compositions (trajectories) → **Relations (where meaning emerges)**.

Intelligence = Navigating ELO-weighted relationship graphs using spatial indexing.

---

## Why This Matters

**Transformers:**
- O(N²) attention over sequences
- Implicit relationships in weights
- Training required for updates
- Context window limits
- Separate architectures per modality
- Black box reasoning

**Hartonomous:**
- O(log N) spatial index + A* graph traversal  
- Explicit relationships as graph edges
- ELO competition for updates (no training)
- Infinite context (entire graph accessible)
- Modality-agnostic (unified substrate)
- Transparent reasoning (auditable paths)

**Result:** Microsecond inference, continuous learning, cross-modal native, explainable intelligence.

---

## Architecture

```
Unicode → BLAKE3 → Super Fibonacci → S³ Coordinates → Hilbert Index
    ↓
Atoms (immutable, ~1.114M positions)
    ↓
Compositions (n-grams of Atoms: words, phrases, pixel sequences)
    ↓
Relations (co-occurrence patterns, ELO-weighted edges)
    ↓
Intelligence (navigation through relationship graph)
```

**Key technologies:**
- **Intel MKL**: Geometric operations at scale (Laplacian eigenmaps, eigendecompositions)
- **PostGIS (custom MKL build)**: Spatial indexing for 4D relationship trajectories
- **HNSWLib**: Sparse relationship extraction from dense AI models
- **BLAKE3**: Content-addressing (deduplication)
- **Tree-sitter**: AST extraction for structured content (code, math)
- **ELO Rating**: Relationship strength dynamics (no backpropagation)

---

## Quick Start

### Prerequisites
- Ubuntu 22.04+ / Debian Linux
- PostgreSQL 18
- Intel oneAPI (MKL)
- CMake 3.22+, C++20 compiler
- 16GB+ RAM

### Build
```bash
# Complete pipeline from clean slate
./full-send.sh
```

This will:
1. Build C++ engine + PostgreSQL extensions
2. Run unit tests (22 tests, 0.23s)
3. Setup development environment (symlinks, ldconfig)
4. Create seed database + ingest Unicode metadata (UCDIngestor)
5. Create hartonomous database + load schema
6. Populate ~1.114M Atoms with S³ coordinates (seed_unicode)
7. Ingest embeddings + text (optional, if data available)
8. Run integration + e2e tests

### Verify
```bash
psql -U postgres -d hartonomous -c "SELECT COUNT(*) FROM hartonomous.atom;"
# Should return: ~1114000
```

---

## Model Integration

**AI models = relationship extractors, not inference engines.**

```bash
# Ingest transformer embeddings (e.g., MiniLM)
./scripts/ingest/ingest-embeddings.sh path/to/model/

# Ingest text to build composition/relation layers
./scripts/ingest/ingest-text.sh path/to/text/file.txt
```

**Process:**
1. Extract embeddings → Use HNSWLib for nearest neighbors → Store as Relations
2. Initialize ELO from embedding distances
3. **Discard dense model (~400B parameters → ~100k edges = 1000x compression)**
4. Cross-model ELO competition → consensus emerges

**Models supported:** Any with extractable embeddings (BERT, GPT, Llama, CLIP, Flux, YOLO, etc.)

---

## Query Example

**Traditional AI:** "white pixel" → tokenize → forward pass → attention → softmax → hope

**Hartonomous:** "white pixel" → compositions → spatial query (O(log N)) → traverse high-ELO relations → return path

```sql
-- Find relationships from query
SELECT r.*, rr.elo_score
FROM relation r
JOIN relation_rating rr USING (relation_id)
WHERE r.composition_a_id = ANY(parse_query('white pixel'))
ORDER BY rr.elo_score DESC
LIMIT 10;
```

**Result:** Paths like:
- "white" → [255,255,255] (from text+image co-occurrence)
- "pixel" → RGB_structure (from image ingestion)

**Latency:** Microseconds.

---

## Documentation

**Start here:**
- [**VISION.md**](docs/VISION.md) - Core paradigm and philosophy
- [**ARCHITECTURE.md**](docs/ARCHITECTURE.md) - Technical implementation
- [**INTELLIGENCE.md**](docs/INTELLIGENCE.md) - How reasoning works
- [**MODELS.md**](docs/MODELS.md) - AI model integration
- [**SELF_IMPROVEMENT.md**](docs/SELF_IMPROVEMENT.md) - OODA loops, Gödel Engine, Reflexion
- [**BUILD.md**](docs/BUILD.md) - Build instructions

**Key concepts:**
- Atoms/Compositions/Relations layers
- ELO rating system for relationship strength
- Super Fibonacci + Hopf fibration for S³ distribution
- Hilbert curves for spatial locality (Mendeleev parallel)
- Voronoi cell semantics (concepts as regions, not points)
- Graph navigation vs attention mechanisms

---

## What Makes This Different

### Not Just "Better Vector Search"

**Vector search:** Find nearby points in embedding space
**Hartonomous:** Navigate relationship graph weighted by evidence

"King" and "Queen" aren't close in S³ coordinates. They're connected through **high-ELO relationship paths** from observed co-occurrence.

### Not Just "Graph Database"

**Graph database:** Store nodes and edges
**Hartonomous:** Intelligence emerges from relationship topology + ELO dynamics + geometric constraints

The geometric substrate enables provable properties (Gödel Engine) and efficient navigation (spatial indexing).

### Not Just "Knowledge Graph"

**Knowledge graph:** Manually curated relationships
**Hartonomous:** Relationships extracted from models + observed data, competing via ELO, continuously evolving

Plus: Cross-modal native, trajectory-based, content-addressed.

---

## The Mendeleev Parallel

Just as the **Periodic Table** organized elements by structure (predicting properties before discovery), Hartonomous organizes meaning by geometric structure.

**Empty cells in relationship graph = knowledge that should exist but hasn't been observed yet.**

Structure predicts relationships. Topology reveals truth.

---

## Self-Improvement (Coming Soon)

**Gödel Engine:** Validates reasoning paths for logical consistency using topological properties

**OODA Loops:** Observe → Orient → Decide → Act → feedback closes loop

**Reflexion:** Generate → Critique → Revise multi-path reasoning

**Trees of Thought:** Explore branches, prune contradictions, converge on consensus

**Result:** Intelligence that improves itself through structure + feedback, no gradient descent.

---

## Status

**Current (Working):**
- ✅ C++ engine with MKL + PostGIS integration
- ✅ OBJECT library build system (~40-50% faster builds)
- ✅ Sudo-free development workflow (symlinks)
- ✅ Test organization (unit/integration/e2e)
- ✅ Atom/Composition/Relation schema
- ✅ seed_unicode tool (populate ~1.114M Atoms)
- ✅ ingest_text, ingest_model tools
- ✅ Basic ELO dynamics

**In Progress:**
- 🔄 Complete script organization (database/, ingest/, test/)
- 🔄 Integration test validation (database setup)
- 🔄 .NET C# interop layer

**Planned:**
- ⏳ Gödel Engine (contradiction detection)
- ⏳ OODA loop infrastructure
- ⏳ Reflexion multi-path reasoning
- ⏳ Query optimization (caching, materialized views)
- ⏳ Distributed substrate (sharding, replication)

---

## The Vision

**A universal substrate where all human knowledge self-organizes through geometric relationships.**

Intelligence emerges from structure, not training.
Meaning is topology.
Reasoning is navigation.
Truth is provable.

**Not improving AI - replacing the paradigm.**

**Laplace's Familiar, tamed and directed.**

---

## License

[To be determined]

## Contact

[To be added]

---

**"All models are wrong, but some are useful." - George Box**

**We're not building a model. We're building the space where models become obsolete.**
