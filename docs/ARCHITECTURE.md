# Hartonomous Technical Architecture

## System Overview

Hartonomous is a geometric intelligence substrate built on three core technologies:

1. **C++ Engine**: High-performance geometric computing with Intel MKL
2. **PostgreSQL Extensions**: Spatial indexing and graph operations
3. **Relationship Graph**: ELO-weighted edges defining semantic space

---

## Component Architecture

### 1. Engine Core (`engine_core.so`)

**Pure Mathematics & Geometry - No Database Dependencies**

**Dependencies:**
- Intel MKL (Math Kernel Library): Linear algebra, FFT, optimized BLAS/LAPACK
- Eigen: Matrix operations, decompositions
- Spectra: Large-scale eigenvalue problems (Laplacian eigenmaps)
- HNSWlib: Approximate nearest neighbor search for relationship extraction
- BLAKE3: Content-addressing and deterministic hashing

**Responsibilities:**
- Super Fibonacci distribution on S³ (uniform point placement)
- Hopf fibration (4D → 3D projection)
- Hilbert curve indexing (space-filling curves for spatial locality)
- S³ quaternion operations (rotations, distances, geodesics)
- BLAKE3 deterministic hashing (content addressing)

**Why MKL:**
Not just "fast math" - continuous geometric operations at scale:
- Eigendecompositions for Laplacian eigenmaps
- Matrix operations for rotation/projection
- FFT for spatial frequency analysis
- Highly optimized for Intel CPUs

---

### 2. Engine I/O (`engine_io.so`)

**Database Integration Layer**

**Dependencies:**
- `engine_core` (all geometric operations)
- PostgreSQL libpq (database connectivity)
- PostGIS (custom build with MKL integration)
- Tree-sitter (parsing structured content: code, math, markup)
- nlohmann/json (configuration and data interchange)

**Responsibilities:**
- Database connection management
- Ingestion pipeline (text → atoms → compositions → relations)
- Tree-sitter AST extraction for structured content
- Batch operations for performance
- Transaction management

**Why Custom PostGIS + MKL:**
Industrial-strength spatial operations with Intel optimization:
- R-tree indexing for 4D geometric queries
- Spatial joins on trajectories
- Distance calculations in non-Euclidean geometry
- MKL acceleration for matrix-heavy geometric operations

---

### 3. Unified Engine (`engine.so`)

**C# Interop Layer**

Links both `engine_core` and `engine_io` into single library for .NET marshaling.

Provides C-compatible API:
```c
h_db_connection_t hartonomous_db_create(const char* conn_string);
bool hartonomous_ingest_text(h_ingester_t ingester, const char* text, HIngestionStats* stats);
void hartonomous_db_destroy(h_db_connection_t handle);
```

---

### 4. PostgreSQL Extensions

#### Extension: `s3.so`

**S³ Sphere Operations**

Links: `engine_core` + `PostGIS`

Provides SQL functions:
- `codepoint_to_s3(int)`: Map Unicode → S³ coordinates
- `s3_distance(s3_point, s3_point)`: Geodesic distance
- `s3_interpolate(s3_point, s3_point, float)`: Quaternion SLERP
- `compute_centroid(s3_point[])`: Geometric center on sphere

Uses PostGIS geometry types for spatial indexing compatibility.

#### Extension: `hartonomous.so`

**Full Intelligence Substrate**

Links: `engine_io` (which includes `engine_core`)

Provides SQL functions:
- `blake3_hash(text)`: Content addressing
- `ingest_text(text)`: Full ingestion pipeline
- `semantic_search(text)`: Relationship-driven query
- `hartonomous_version()`: Extension info

---

### 5. UCDIngestor

**Unicode Character Database Importer**

Separate binary that populates seed database with Unicode metadata:
- UCD (Unicode Character Database): Properties, categories, scripts
- UCA (Unicode Collation Algorithm): Semantic sequences, sort orders
- Stroke counts, decompositions, variants
- Han unification data, CJK rad icals

This metadata enables semantic sequencing for Super Fibonacci distribution.

**Workflow:**
1. Create `ucd_seed` database (temporary)
2. Run UCDIngestor → populate with Unicode metadata
3. `seed_unicode` tool reads seed DB, generates Atom records in `hartonomous` DB
4. Seed DB can be dropped after Atoms are locked

---

### 6. Ingestion Tools

Built binaries in `Engine/tools/`:

#### `seed_unicode`
- Reads UCD/UCA metadata from seed database
- Applies Super Fibonacci distribution
- Generates ~1.114M Atom + Physicality records with S³ coordinates
- **Locks the geometric foundation (Atoms become immutable)**

#### `ingest_model`
- Loads transformer embedding weights (e.g., MiniLM 30k×384)
- Uses HNSWLib for approximate nearest neighbor search
- Extracts semantic edges (which tokens are proximate in embedding space)
- Stores edges as Relations with initial ELO ratings
- **Discards dense model, keeps sparse relationship graph**

#### `ingest_text`
- Parses text into Unicode codepoints
- Maps to Atoms (via BLAKE3 lookup)
- Creates Compositions (n-grams: words, phrases)
- Records trajectories via CompositionSequence
- Detects co-occurrences, creates Relations
- Updates ELO ratings via competition
- Records provenance in RelationEvidence

#### `walk_test`
- Tests trajectory navigation
- Validates geometric operations
- Performance benchmarking

---

## Database Schema

### Schema: `hartonomous`

**Foundation Tables:**

#### `atom`
- `atom_id` (SERIAL PRIMARY KEY)
- `codepoint` (INTEGER UNIQUE) - Unicode codepoint
- `hash` (BYTEA) - BLAKE3(codepoint)
- `s3_coords` (GEOMETRY(PointZ)) - S³ position as PostGIS geometry
- `hilbert_index` (UINT128) - Space-filling curve index

**Indexed on:** `codepoint`, `hash`, `hilbert_index`, spatial index on `s3_coords`

#### `physicality`
- `physicality_id` (SERIAL PRIMARY KEY)
- `atom_id` (FK → atom)
- `mass` (DOUBLE) - Semantic "weight" (from UCD properties)
- `charge` (DOUBLE) - Not electrical; abstract property
- `metadata` (JSONB) - UCD properties, stroke count, etc.

#### `composition`
- `composition_id` (SERIAL PRIMARY KEY)
- `hash` (BYTEA UNIQUE) - BLAKE3(atom sequence)
- `length` (INTEGER) - Number of atoms
- `occurrences` (INTEGER) - Total times observed across all content
- `trajectory` (GEOMETRY(LineStringZ)) - Path through S³

**Deduplication:** Same n-gram stored once regardless of source. "the" appears millions of times across all ingested content but exists as ONE composition record.

**Indexed on:** `hash`, spatial index on `trajectory`

#### `composition_sequence`
- `sequence_id` (SERIAL PRIMARY KEY)
- `composition_id` (FK → composition)
- `position` (INTEGER) - Index in sequence (0-based)
- `atom_id` (FK → atom)
- `occurrences` (INTEGER) - Run-length encoding for repeated atoms

**Run-Length Encoding Example:**
"Mississippi" breaks down to:
- Position 0: 'M' (occurs 1x)
- Position 1: 'i' (occurs 1x) 
- Position 2: 's' (occurs 2x) ← "ss" compressed
- Position 4: 'i' (occurs 1x)
- Position 5: 's' (occurs 2x) ← second "ss" compressed
- Position 7: 'i' (occurs 1x)
- Position 8: 'p' (occurs 2x) ← "pp" compressed
- Position 10: 'i' (occurs 1x)

**Cascading Deduplication:** "ss" is ITSELF a composition, stored once, referenced by hash. Mississippi's composition_sequence references the "ss" composition hash twice at positions 2 and 5.

#### `relation`
- `relation_id` (SERIAL PRIMARY KEY)
- `composition_a_id` (FK → composition)
- `composition_b_id` (FK → composition)  
- `hash` (BYTEA UNIQUE) - BLAKE3(comp_a || comp_b || ordering)
- `trajectory` (GEOMETRY(LineStringZ)) - Path through composition space
- `distance` (DOUBLE) - Geometric distance (spatial, NOT semantic)
- `occurrences` (INTEGER) - Total observations across all evidence

**Critical:** ONE relation record regardless of how many times observed. "the" → "dog" appears in:
- Moby Dick (text)
- BERT layer 1-12, heads 1-16 (192 occurrences in one model)
- GPT-3 layer 1-96, heads 1-96 (9,216 occurrences)
- Llama-3 with 8 experts × 32 layers × 32 heads (8,192 occurrences)
- Millions more from other sources

**All collapse to ONE relation record.** Evidence table tracks every source.

**Indexed on:** `hash`, both composition FKs, spatial index on `trajectory`

#### `relation_sequence`
- `sequence_id` (SERIAL PRIMARY KEY)
- `relation_id` (FK → relation)
- `position` (INTEGER)
- `composition_id` (FK → composition)
- `occurrences` (INTEGER)

Higher-order n-grams: relations of composition relations.

#### `content`
- `content_id` (SERIAL PRIMARY KEY)
- `source_type` (TEXT) - 'model', 'text', 'image', 'audio', 'code', 'user_prompt'
- `source_identifier` (TEXT) - 'Llama-3.1-70B', 'moby_dick.txt', 'user_123_prompt_456'
- `ingestion_time` (TIMESTAMP)
- `metadata` (JSONB) - Full context: file hash, model version, hyperparameters, etc.
- `hash` (BYTEA UNIQUE) - BLAKE3 of original content for verification

**Purpose:** Track ingestion events for surgical deletion and provenance. This enables:
- Delete entire model's knowledge
- Delete specific document
- Remove user's contribution (GDPR right to deletion)
- Audit trail: "Where did this relation come from?"

#### `relation_evidence`
- `evidence_id` (SERIAL PRIMARY KEY)
- `relation_id` (FK → relation)
- `content_id` (FK → content) - Links to ingestion event
- `weight` (DOUBLE) - Model confidence OR occurrence count in content
- `position` (INTEGER) - Where in source (for content), layer/head (for models)
- `context` (TEXT) - Surrounding content for auditability
- `timestamp` (TIMESTAMP)

**This is the key to deduplication magic:**

Same relation observed 10,000+ times across models:
```
relation_id=12345 ("the" → "dog")
  evidence: content_id=1 (BERT),    weight=0.89, position="layer1-head5"
  evidence: content_id=1 (BERT),    weight=0.91, position="layer3-head2"  
  evidence: content_id=1 (BERT),    weight=0.87, position="layer8-head7"
  ... 189 more from BERT ...
  evidence: content_id=2 (GPT-3),   weight=0.93, position="layer10-head1"
  ... 9,215 more from GPT-3 ...
  evidence: content_id=3 (Llama-3), weight=0.88, position="expert2-layer5-head3"
  ... 8,191 more from Llama-3 ...
  evidence: content_id=4 (moby_dick), weight=1.0, position=12457
  evidence: content_id=4 (moby_dick), weight=1.0, position=98234
  ... thousands more ...
```

**10,000+ evidence entries → 1 relation record = 10,000x compression.**

#### `relation_rating`
- `rating_id` (SERIAL PRIMARY KEY)
- `relation_id` (FK → relation UNIQUE)
- `elo_score` (DOUBLE DEFAULT 1500.0) - Semantic strength (NOT geometric distance)
- `confidence` (DOUBLE) - Aggregate confidence from evidence
- `last_updated` (TIMESTAMP) - When ELO last recalculated

**ELO = Aggregate of Evidence:**
```sql
-- ELO calculated from ALL evidence for this relation
UPDATE relation_rating 
SET elo_score = calculate_elo_from_evidence(relation_id),
    confidence = AVG(evidence.weight),
    last_updated = NOW()
WHERE relation_id = :id;
```

**Cross-model consensus emerges:**
- BERT says: 0.89
- GPT-3 says: 0.93  
- Llama-3 says: 0.88
- 10,000 text occurrences
- → Final ELO: 1750 (strong confidence)

**Surgical deletion:**
```sql
-- Delete Llama-3's knowledge
DELETE FROM relation_evidence
WHERE content_id IN (SELECT id FROM content WHERE source_identifier = 'Llama-3.1-70B');

-- Recalculate ELO for affected relations (from remaining evidence)
UPDATE relation_rating rr
SET elo_score = calculate_elo_from_evidence(r.relation_id),
    last_updated = NOW()
FROM relation r
WHERE r.rating_id = rr.rating_id
  AND r.relation_id IN (SELECT DISTINCT relation_id FROM deleted_evidence);

-- Prune orphaned relations (no evidence left)
DELETE FROM relation
WHERE relation_id NOT IN (SELECT DISTINCT relation_id FROM relation_evidence);
```

**This enables:**
- ✅ GDPR-compliant complete deletion
- ✅ Remove biased/toxic models surgically  
- ✅ "Fear of failure" concept can be targeted and erased
- ✅ A/B test model quality (ingest, compare ELO evolution, remove loser)
- ✅ Full audit trail preserved

---

## Compression Architecture

### Cascading Deduplication with Run-Length Encoding

**Every tier deduplicates via BLAKE3 content-addressing:**

1. **Atom Tier** (~1.114M fixed)
   - Each Unicode codepoint stored ONCE
   - Referenced millions of times across content
   - No compression (foundation layer)

2. **Composition Tier** (millions unique, billions of references)
   - "the" appears 1 billion times → stored ONCE
   - "Mississippi" stored once, sub-compositions ("ss", "iss", "pp") also stored once
   - Run-length encoding: repeated sequences compressed at storage
   - Deduplication: Same hash = same n-gram = one record

3. **Relation Tier** (millions unique, trillions of references)
   - "the" → "dog" appears across:
     - Every English text corpus
     - Every language model (12-96 layers × 8-96 heads)
     - Every MoE model (64 experts × 32 layers × 32 heads)
   - ONE relation record
   - 10,000+ evidence entries

**Total compression:**
- Content: 90-95% (natural language redundancy + run-length encoding)
- AI models: 10,000-100,000x (same relationships repeated across layers/heads/experts)

### Two Storage Modes: Dense vs Sparse

#### Dense Storage (Content: Text, Images, Audio, Code)

**Requirement: Bit-perfect reconstruction.**

- Ingest Moby Dick (22,093 lines)
- Store as complete Relation sequences (ordered)
- Export Moby Dick
- BLAKE3 hash match ✓

**Implementation:**
- `relation_sequence` preserves EXACT ordering
- `occurrences` tracks all repetitions
- No gaps allowed (every position recorded)
- Content provenance via `content` table

**Example:**
```
Content: "Call me Ishmael."
↓
Atoms: [C][a][l][l][ ][m][e][ ][I][s][h][m][a][e][l][.]
↓
Compositions: ["Call"]["me"]["Ishmael"]["."]
↓
Relations: ["Call"→"me"], ["me"→"Ishmael"], ["Ishmael"→"."]
↓
Export via relation_sequence traversal:
["Call"→"me"→"Ishmael"→"."] 
↓
Decompose to atoms:
[C][a][l][l][ ][m][e][ ][I][s][h][m][a][e][l][.]
↓
"Call me Ishmael."
↓
BLAKE3: 0x... (matches original)
```

#### Sparse Storage (AI Models)

**Requirement: Extract relationships, discard rest.**

- Ingest BERT (110M parameters)
- Extract ~100k semantic edges (k-NN via HNSWLib)
- Store as Relations with evidence
- **Cannot reconstruct original model**

**Implementation:**
- Only semantically significant relations stored
- Gaps expected (export 0s where no edge)
- Evidence tracks layer/head/weight but discards actual parameters
- 10,000x+ compression

**Example:**
```
Model: BERT-base layer 3, head 5
Attention weights: [30k × 30k matrix = 900M values]
↓
k-NN extraction: Top 50 neighbors per token
↓
Relations: ~1.5M edges with initial ELO from distances
↓
Deduplicate against existing substrate
↓
~100k NEW relations (rest already exist from other models)
↓
Evidence entries: 1.5M (all point to deduplicated relations)
↓
Original 900M values → 100k relations = 9,000x compression
```

**Key difference:**
- **Content = lossless** (round-trip perfect)
- **Models = lossy** (extract knowledge, discard implementation)

---

## Performance Architecture

### Spatial Indexing (PostGIS + Hilbert)

**Why Hilbert Curves:**
- Map 4D space → 1D while preserving locality
- Nearby in 4D → nearby in 1D → cache-friendly
- Like Mendeleev's periodic table: structure reveals relationships

**Query Pattern:**
```sql
-- Find relations in spatial neighborhood
SELECT r.* FROM relation r
WHERE ST_DWithin(r.trajectory, query_point, threshold)
  AND hilbert_index BETWEEN low AND high
ORDER BY elo_score DESC;
```

O(log N) spatial lookup + efficient scanning thanks to Hilbert locality.

### MKL Acceleration

**Not just BLAS:**
- Laplacian eigenmaps: Transform embedding spaces to substrate coordinates
- Gram-Schmidt orthonormalization: Align model embeddings
- Large-scale eigenvalue decompositions via Spectra + MKL
- Optimized for Intel CPUs (AVX-512 when available)

### OBJECT Libraries (Build Optimization)

**Compile once, link many:**
- `engine_core_objs` → links to `engine_core.so`, `engine_io.so`, `engine.so`
- `engine_io_objs` → links to `engine_io.so`, `engine.so`
- ~40-50% build time reduction
- No duplicate compilation

---

## Development Workflow

### Sudo-Free Iteration

**One-time setup:**
```bash
./scripts/build/build-all.sh          # Initial build
./scripts/build/install-local.sh      # Install to project/install/
./scripts/build/install-dev-symlinks.sh  # Symlink system dirs → install/
```

**Fast iteration:**
```bash
./rebuild.sh  # Rebuild + copy to install/ + ldconfig (only ldconfig needs sudo)
```

Libraries installed via symlinks:
- `/usr/local/lib/libengine*.so` → `project/install/lib/`
- `/usr/lib/postgresql/18/lib/*.so` → `project/install/lib/`

### Test Organization

```
Engine/tests/
  unit/          # 22 tests, 0.23s, no database
  integration/   # 4 tests, requires hartonomous DB
  e2e/           # 4 tests, full pipeline
```

**Run independently:**
```bash
./scripts/test/run-unit-tests.sh           # Fast feedback
./scripts/test/run-integration-tests.sh    # After DB setup
./scripts/test/run-all-tests.sh            # Complete validation
```

---

## Deployment Pipeline

### Phase 1: Build
```bash
./scripts/build/build-all.sh
```
- C++ engine (82 targets)
- PostgreSQL extensions
- Ingestion tools

### Phase 2: Foundation
```bash
./scripts/database/setup-seed-db.sh
./UCDIngestor/setup_db.sh
./scripts/database/create-hartonomous-db.sh
./scripts/database/load-extensions.sh
```
- Seed database with Unicode metadata
- Hartonomous database with schema
- Load PostGIS + custom extensions

### Phase 3: Lock Atoms
```bash
./scripts/database/populate-atoms.sh
```
- Run `seed_unicode` tool
- Generate ~1.114M Atom records
- **Foundation becomes immutable**

### Phase 4: Ingest Knowledge
```bash
./scripts/ingest/ingest-embeddings.sh  # MiniLM, Flux2, etc.
./scripts/ingest/ingest-text.sh        # Documents, code, etc.
```
- Extract relationships from models (sparse)
- Build composition/relation layers from content

### Phase 5: Validate
```bash
./scripts/test/run-all-tests.sh
```

---

## Query Architecture

See [INTELLIGENCE.md](INTELLIGENCE.md) for reasoning mechanics.

**Query types:**
1. **Spatial queries**: Find trajectories in geometric neighborhood
2. **Graph traversal**: Navigate relations weighted by ELO
3. **Multi-hop reasoning**: Chain relationships A→B→C→D
4. **Cross-modal**: Unified queries across text/image/code

**Performance target:** Microseconds for simple queries, milliseconds for complex reasoning.

---

## Why This Architecture

**What we're NOT doing:**
- ❌ Forward passes through neural networks
- ❌ Attention over sequences
- ❌ Gradient descent
- ❌ Separate models per modality

**What we ARE doing:**
- ✅ Spatial index lookups (O(log N))
- ✅ Graph traversal with A* (heuristic-guided)
- ✅ ELO competition (evolutionary)
- ✅ Unified substrate (modality-agnostic)

**Result:** Orders of magnitude faster, transparent reasoning, continuous learning.

---

## Next Steps

- Read [INTELLIGENCE.md](INTELLIGENCE.md) for reasoning mechanics
- Read [MODELS.md](MODELS.md) for AI model integration
- Read [BUILD.md](BUILD.md) for build instructions
