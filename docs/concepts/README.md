# Core Concepts

Understanding the fundamental principles of Hartonomous.

---

## Navigation

### Foundational Concepts

**[Atoms](atoms.md)** — The fundamental unit (?64 bytes, content-addressed, deduplicated)

**[Compositions](compositions.md)** — Hierarchical structures (molecules from atoms)

**[Relations](relations.md)** — Semantic connections (knowledge graph, Hebbian learning)

**[Spatial Semantics](spatial-semantics.md)** — Geometric positioning (Hilbert curves, landmark projection)

### Advanced Topics

**[Compression](compression.md)** — Multi-layer encoding (sparse + delta + bit packing)

**[Provenance](provenance.md)** — Complete audit trail (Neo4j graph tracking)

**[Modalities](modalities.md)** — Multi-modal representation (text, code, images, audio)

---

## Learning Path

```
1. Atoms ????? Everything is atoms (?64 bytes)
      ?
2. Compositions ????? Hierarchy (sentence ? words ? chars)
      ?
3. Relations ????? Semantic graph (how atoms connect)
      ?
4. Spatial Semantics ????? Position = meaning (3D space)
      ?
5. Compression ????? Efficiency (10-100x compression)
      ?
6. Provenance ????? Auditability (full lineage)
      ?
7. Modalities ????? Unified representation (all data types)
```

---

## Core Principles

### 1. Content Addressing
Every atom identified by `SHA-256(atomic_value)`. Same value anywhere = same atom.

```sql
-- Character 'A' appears 1M times ? 1 atom, 1M references
SELECT atom_id, canonical_text, reference_count
FROM atom
WHERE canonical_text = 'A';
-- Result: atom_id=65, reference_count=1000000
```

### 2. Geometric Semantics
Atoms positioned in 3D space. **Close in space = similar in meaning.**

```sql
-- Find atoms near "cat"
SELECT canonical_text, ST_Distance(spatial_key, $cat_position)
FROM atom
ORDER BY distance ASC;
-- Results: cat, kitten, feline, dog, meow, whiskers...
```

### 3. Hierarchical Composition
Complex structures built from simpler atoms.

```
Document
  ?? Sentence 1
  ?   ?? Word: "Machine"
  ?   ?   ?? 'M'
  ?   ?   ?? 'a'
  ?   ?   ?? 'c'
  ?   ?   ?? 'h'
  ?   ?   ?? 'i'
  ?   ?   ?? 'n'
  ?   ?   ?? 'e'
  ?   ?? Word: "learning"
  ?       ?? 'l'
  ?       ?? 'e'
  ?       ?? 'a'
  ?       ?? 'r'
  ?       ?? 'n'
  ?       ?? 'i'
  ?       ?? 'n'
  ?       ?? 'g'
  ?? Sentence 2
      ?? ...
```

### 4. Hebbian Learning
"Neurons that fire together, wire together."

```sql
-- Strengthen relation when atoms co-occur
UPDATE atom_relation
SET weight = weight * 1.1
WHERE source_atom_id = $machine AND target_atom_id = $learning;
```

### 5. Sparse Representation
Only store non-zero values. Missing = implicit zero.

```sql
-- Embedding: [0.0, 0.23, 0.0, -0.14, 0.0, 0.87]
-- Stored: [(1, 0.23), (3, -0.14), (5, 0.87)]  ? 3 rows, not 6
```

### 6. Temporal Versioning
All changes tracked via `valid_from`/`valid_to`.

```sql
-- View atom history
SELECT atom_id, canonical_text, valid_from, valid_to
FROM atom
WHERE atom_id = 12345
ORDER BY valid_from DESC;
```

### 7. Provenance Tracking
Every atom derivation recorded in Neo4j.

```cypher
// How was this atom created?
MATCH path = (atom:Atom {atom_id: 12345})-[:DERIVED_FROM*]->(origin)
RETURN path
```

---

## Key Insights

### Everything Decomposes to Atoms

| Data Type | Atomization Strategy | Example |
|-----------|---------------------|---------|
| **Text** | Character atoms | "Hello" ? ['H','e','l','l','o'] |
| **Numbers** | Byte representation | 0.017 ? atomic_value=0x3C8B4396 |
| **Images** | Pixel RGB atoms | Pixel(0,0) ? atomic_value=0xFF5733 |
| **Code** | Token atoms (via Roslyn/Tree-sitter) | `public class` ? ['public', 'class'] |
| **Audio** | Sample atoms | Sample(0ms) ? atomic_value=0x3F800000 |
| **Embeddings** | Float atoms (sparse) | [0.23, 0.0, -0.14] ? [(0, 0.23), (2, -0.14)] |
| **Models** | Weight atoms | Layer5.Weight[123] ? atomic_value=... |

### Ingestion IS Training

Traditional AI:
```
Training Phase ? Frozen Model ? Deployment Phase ? Inference
```

Hartonomous:
```
Ingestion ? Atomize ? Position ? Relate ? DONE (model updated)
```

**No separate training. Every ingestion updates the semantic space immediately.**

### Position Emerges from Neighbors

**No embedding model needed:**

```sql
-- New atom "kitten" ingested
-- 1. Query existing semantic neighbors
SELECT atom_id, spatial_key
FROM atom
WHERE metadata->>'modality' = 'text'
ORDER BY similarity('kitten', canonical_text) DESC
LIMIT 100;

-- 2. Compute weighted centroid
UPDATE atom
SET spatial_key = ST_Centroid(ST_Collect(neighbor_positions))
WHERE atom_id = $kitten_atom_id;

-- 3. Position automatically near "cat", "feline", "pet"
```

**Position emerges organically from co-occurrence and composition.**

---

## Visual Representation

### Semantic Space (3D)

```
        Z (Specificity)
        ?
        ?    • "kitten" (specific)
        ?   ?
        ?  • "cat" (moderate)
        ? ?
        •????????? Y (Category)
       ? "animal" (general)
      ?
     X (Modality)
```

- **X-axis**: Modality (text, image, audio)
- **Y-axis**: Category (animals, technology, etc.)
- **Z-axis**: Specificity (general ? specific)

**Distance = semantic dissimilarity.**

### Composition Hierarchy

```
[Document Atom]
       ? COMPOSED_OF
[Sentence Atom 1] [Sentence Atom 2] [Sentence Atom 3]
       ? COMPOSED_OF
[Word Atom: "machine"] [Word Atom: "learning"]
       ? COMPOSED_OF
['m'] ['a'] ['c'] ['h'] ['i'] ['n'] ['e']
```

**Each level is queryable, traversable, and spatially positioned.**

---

## Performance Implications

### Content Addressing = Deduplication

```sql
-- 1 billion occurrences of character 'e'
-- Traditional storage: 1B × 1 byte = 1 GB
-- Hartonomous: 1 atom + 1B references = ~8MB
```

**Result**: ~125x storage reduction for common patterns.

### Spatial Indexing = Fast Queries

```sql
-- K-nearest neighbors (K=10)
-- Traditional: O(N) scan (all atoms)
-- Hartonomous: O(log N) GiST index
```

**Result**: 1000x speedup for semantic queries.

### Sparse Encoding = Compression

```sql
-- Embedding: 1998D vector (mostly zeros)
-- Traditional: 1998 × 4 bytes = 7992 bytes
-- Hartonomous (sparse): ~50 non-zero × 12 bytes = 600 bytes
```

**Result**: ~13x compression for sparse data.

---

## Next Steps

Start with **[Atoms](atoms.md)** to understand the fundamental unit, then progress through compositions, relations, and spatial semantics.

Each concept builds on the previous, forming a complete picture of how Hartonomous represents and reasons about knowledge.

---

**Begin: [Atoms ?](atoms.md)**
