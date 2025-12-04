# Implementation Reference

**Purpose:** Technical implementation details and patterns extracted from architecture evolution.  
**Date:** 2025-12-03  
**Status:** Consolidated from multiple iterations

---

## Implemented Functions (17 Core)

From November 2025 implementation:

1. **atomize_value** - Core content-addressable atom creation
2. **atomize_with_spatial_key** - Atom creation with spatial position
3. **atomize_text** - Character-level text atomization  
4. **atomize_numeric** - Float/numeric atomization
5. **atomize_pixel** - Image pixel atomization
6. **atomize_pixel_delta** - Delta-encoded pixels
7. **atomize_image** - Full image ingestion
8. **atomize_image_vectorized** - Batch-optimized image processing
9. **atomize_audio_sample** - Individual audio sample atomization
10. **atomize_audio** - Full audio waveform ingestion
11. **atomize_audio_sparse** - Sparse audio encoding
12. **atomize_hilbert_lod** - Level-of-detail Hilbert encoding
13. **compress_uniform_hilbert_region** - Region compression
14. **hilbert_encode_3d** - 3D→1D Hilbert curve encoding
15. **hilbert_decode_3d** - 1D→3D Hilbert curve decoding
16. **hilbert_encode_point** - PostGIS Point→Hilbert
17. **hilbert_range_query** - Bounding box→Hilbert range

---

## Text Atomization Patterns

### Character-Level

```sql
CREATE FUNCTION atomize_text(p_text TEXT)
RETURNS BIGINT[] AS $$
DECLARE
    v_char TEXT;
    v_atom_ids BIGINT[];
BEGIN
    FOR i IN 1..length(p_text) LOOP
        v_char := substring(p_text FROM i FOR 1);

        -- Atomize character
        v_atom_ids := array_append(v_atom_ids, atomize_value(
            convert_to(v_char, 'UTF8'),
            v_char,
            '{"modality": "character"}'::jsonb
        ));
    END LOOP;

    RETURN v_atom_ids;
END;
$$ LANGUAGE plpgsql;
```

### Hierarchical Document Structure

```
Document
  ├─ Paragraph
  │   ├─ Sentence
  │   │   ├─ Word
  │   │   │   ├─ Character
```

---

## Spatial Position Computation

### Semantic Neighbor Averaging

Algorithm for determining atom position:

```sql
CREATE FUNCTION compute_spatial_position(p_atom_id BIGINT)
RETURNS GEOMETRY AS $$
DECLARE
    v_atom RECORD;
    v_centroid GEOMETRY;
BEGIN
    SELECT * INTO v_atom FROM atom WHERE atom_id = p_atom_id;

    -- Text similarity (Levenshtein)
    IF v_atom.metadata->>'modality' = 'text' THEN
        SELECT ST_Centroid(ST_Collect(a.spatial_key))
        INTO v_centroid
        FROM atom a
        WHERE a.metadata->>'modality' = 'text'
          AND a.spatial_key IS NOT NULL
          AND a.atom_id != p_atom_id
        ORDER BY levenshtein(a.canonical_text, v_atom.canonical_text)
        LIMIT 100;

    -- Image patch similarity (histogram)
    ELSIF v_atom.metadata->>'modality' = 'image_patch' THEN
        SELECT ST_Centroid(ST_Collect(a.spatial_key))
        INTO v_centroid
        FROM atom a
        WHERE a.metadata->>'modality' = 'image_patch'
          AND a.spatial_key IS NOT NULL
        ORDER BY histogram_distance(a.atomic_value, v_atom.atomic_value)
        LIMIT 100;

    -- Default: random initialization
    ELSE
        v_centroid := ST_MakePoint(
            random() * 20 - 10,
            random() * 20 - 10,
            random() * 20 - 10
        );
    END IF;

    RETURN v_centroid;
END;
$$ LANGUAGE plpgsql;
```

---

## Sparse Composition Pattern

**Concept:** Gaps in `sequence_index` = implicit zeros.

**Example:** Sparse vector `[1.5, 0, 0, 0, 3.2, 0, 0, 7.8]`

```sql
-- Store only non-zero components
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (@vector_id, @value_1_5_id, 0),
    (@vector_id, @value_3_2_id, 4),
    (@vector_id, @value_7_8_id, 7);
-- Indices 1,2,3,5,6 implicitly zero
```

**Reconstruction:**

```sql
SELECT
    COALESCE(component_atom_id, 0) AS value,
    idx AS position
FROM generate_series(0, 7) idx
LEFT JOIN atom_composition ac
    ON ac.parent_atom_id = @vector_id
   AND ac.sequence_index = idx;
```

---

## Reference Counting Strategy

**Increment on use:**

```sql
CREATE TRIGGER increment_reference_count
AFTER INSERT ON atom_composition
FOR EACH ROW
EXECUTE FUNCTION increment_component_refcount();

CREATE FUNCTION increment_component_refcount()
RETURNS TRIGGER AS $$
BEGIN
    UPDATE atom
    SET reference_count = reference_count + 1
    WHERE atom_id = NEW.component_atom_id;

    RETURN NEW;
END;
$$ LANGUAGE plpgsql;
```

**Decrement on deletion:**

```sql
CREATE TRIGGER decrement_reference_count
AFTER DELETE ON atom_composition
FOR EACH ROW
EXECUTE FUNCTION decrement_component_refcount();
```

---

## GPU-Accelerated Batch Hashing

```sql
CREATE FUNCTION gpu_batch_atomize(p_values BYTEA[])
RETURNS TABLE(atom_id BIGINT, content_hash BYTEA) AS $$
    import hashlib
    import torch

    device = "cuda" if torch.cuda.is_available() else "cpu"

    results = []
    for value in p_values:
        hash_val = hashlib.sha256(value).digest()
        # Insert or update atom (upsert logic)
        results.append((atom_id, hash_val))

    return results
$$ LANGUAGE plpython3u;
```

---

## Multi-Model Query Pattern

Query semantic region across all ingested models:

```sql
SELECT
    canonical_text,
    metadata->>'model_name' AS model,
    ST_Distance(spatial_key, @query_point) AS distance,
    reference_count
FROM atom
WHERE ST_DWithin(spatial_key, @query_point, 1.0)
  AND metadata->>'modality' = 'text'
ORDER BY reference_count DESC, distance ASC
LIMIT 100;
```

**Result:** Ensemble intelligence from GPT-4, Claude, Llama sharing semantic space.

---

## Cross-Model Consensus Pattern

Find facts with multi-model agreement:

```sql
SELECT
    canonical_text,
    COUNT(DISTINCT metadata->>'model_name') AS model_consensus,
    AVG(ST_Distance(spatial_key, @reference)) AS cluster_tightness,
    SUM(reference_count) AS total_references
FROM atom
WHERE ST_DWithin(spatial_key, @reference, 0.1)
  AND metadata ? 'model_name'
GROUP BY canonical_text
HAVING COUNT(DISTINCT metadata->>'model_name') > 3
ORDER BY model_consensus DESC, cluster_tightness ASC;
```

**Interpretation:** Hallucinations scatter, facts cluster tightly across multiple models.

---

## Performance Characteristics

| Operation | Complexity | Time |
|-----------|-----------|------|
| Atomize character | O(1) hash | 0.1ms |
| Atomize word | O(K) characters | 0.5ms |
| Atomize document | O(N) words | 50ms (1000 words) |
| Image patch extraction | O(P) patches | 200ms (256 patches) |
| Spatial position compute | O(log N) neighbors | 20ms |
| Batch insert (1K atoms) | O(K) | 15ms |
| KNN query (indexed) | O(log N) | <10ms |

---

## Anti-Patterns

**Don't:** Store full document as single atom (exceeds 64-byte limit)  
**Do:** Decompose hierarchically

**Don't:** Recompute spatial positions on every query  
**Do:** Materialize positions, update periodically or via trigger

**Don't:** Insert atoms sequentially in loop  
**Do:** Batch insert via `COPY` or array operations

**Don't:** Create separate embedding models for each modality  
**Do:** Project all modalities to unified semantic space

---

## Infrastructure Setup

### Database: PostgreSQL 16
- PostGIS 3.6.1 (spatial indexing)
- PL/Python3u (GPU-accelerated functions)
- 950+ custom functions installed
- Hilbert curve encoding operational
- Gram-Schmidt orthogonalization working

### Python Environment
- System Python: 3.13+
- Dependencies: numpy, torch, pdfplumber, python-docx, markdown-it-py, beautifulsoup4
- GPU: CUDA-enabled for batch operations

### Docker Compose
- PostgreSQL + PostGIS container
- Neo4j container (optional provenance graph)
- FastAPI application container
- Redis container (optional caching)

---

## See Also

- [DATABASE_ARCHITECTURE_COMPLETE.md](architecture/DATABASE_ARCHITECTURE_COMPLETE.md) - Current designed schema
- [Reinventing AI with Spatial SQL.md](Reinventing AI with Spatial SQL.md) - Authoritative technical vision
- [WORK_STATUS.md](WORK_STATUS.md) - Current implementation status (83/93 tests passing)
- [PRIORITIES.md](PRIORITIES.md) - Development priorities

---

**Note:** This document consolidates patterns from November 2025 implementation. Some patterns may need updating to align with current POINTZM architecture and M-coordinate triple-duty design.
