# INGESTION

Atomization patterns. Model ingestion. Sparse storage.

---

## Core Principle

**Ingestion IS training**. No backpropagation, no gradient descent. Just decompose → store → position.

---

## Text Atomization

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

**Result**: "Hello" → atoms for 'H', 'e', 'l', 'l', 'o'

### Word-Level Composition

```sql
-- Compose characters into words
CREATE FUNCTION atomize_words(p_text TEXT)
RETURNS BIGINT AS $$
DECLARE
    v_words TEXT[];
    v_word TEXT;
    v_word_atom_id BIGINT;
    v_char_ids BIGINT[];
BEGIN
    v_words := string_to_array(p_text, ' ');

    FOR i IN 1..array_length(v_words, 1) LOOP
        v_word := v_words[i];

        -- Atomize word itself
        v_word_atom_id := atomize_value(
            convert_to(v_word, 'UTF8'),
            v_word,
            '{"modality": "word"}'::jsonb
        );

        -- Atomize constituent characters
        v_char_ids := atomize_text(v_word);

        -- Create composition
        FOR j IN 1..array_length(v_char_ids, 1) LOOP
            INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
            VALUES (v_word_atom_id, v_char_ids[j], j);
        END LOOP;
    END LOOP;

    RETURN v_word_atom_id;
END;
$$ LANGUAGE plpgsql;
```

### Sentence/Document Hierarchy

```
Document
  ├─ Paragraph
  │   ├─ Sentence
  │   │   ├─ Word
  │   │   │   ├─ Character
```

```sql
-- Full hierarchical ingestion
CREATE FUNCTION ingest_document(p_doc_text TEXT, p_metadata JSONB)
RETURNS BIGINT AS $$
DECLARE
    v_doc_atom_id BIGINT;
    v_para_ids BIGINT[];
    v_paragraphs TEXT[];
BEGIN
    -- Create document atom
    v_doc_atom_id := atomize_value(
        digest(p_doc_text, 'sha256'),
        NULL,  -- No canonical text for large docs
        p_metadata || '{"modality": "document"}'::jsonb
    );

    -- Split into paragraphs
    v_paragraphs := string_to_array(p_doc_text, E'\n\n');

    FOR i IN 1..array_length(v_paragraphs, 1) LOOP
        v_para_ids := array_append(v_para_ids, atomize_words(v_paragraphs[i]));

        -- Link paragraph to document
        INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
        VALUES (v_doc_atom_id, v_para_ids[i], i);
    END LOOP;

    RETURN v_doc_atom_id;
END;
$$ LANGUAGE plpgsql;
```

---

## Image Atomization

### Patch-Based

```sql
-- Extract 16x16 patches from image
CREATE FUNCTION atomize_image(p_image_bytes BYTEA, p_metadata JSONB)
RETURNS BIGINT AS $$
DECLARE
    v_image_atom_id BIGINT;
    v_patch BYTEA;
    v_patch_atom_id BIGINT;
    v_patch_position GEOMETRY;
BEGIN
    -- Create image atom
    v_image_atom_id := atomize_value(
        digest(p_image_bytes, 'sha256'),
        NULL,
        p_metadata || '{"modality": "image"}'::jsonb
    );

    -- Extract patches (pseudo-code, actual implementation uses PL/Python)
    FOR x IN 0..(image_width / 16 - 1) LOOP
        FOR y IN 0..(image_height / 16 - 1) LOOP
            v_patch := extract_patch(p_image_bytes, x, y, 16, 16);

            -- Skip background patches (all white/black)
            CONTINUE WHEN is_background(v_patch);

            -- Atomize patch
            v_patch_atom_id := atomize_value(
                v_patch,
                NULL,
                jsonb_build_object('modality', 'image_patch', 'x', x, 'y', y)
            );

            -- Compute spatial position via neighbor averaging
            v_patch_position := compute_image_spatial_position(v_patch);

            UPDATE atom SET spatial_key = v_patch_position
            WHERE atom_id = v_patch_atom_id;

            -- Link patch to image
            INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
            VALUES (v_image_atom_id, v_patch_atom_id, y * (image_width / 16) + x);
        END LOOP;
    END LOOP;

    RETURN v_image_atom_id;
END;
$$ LANGUAGE plpgsql;
```

---

## Audio Atomization

### Phoneme-Based

```sql
-- Atomize audio via phonemes
CREATE FUNCTION atomize_audio(p_audio_waveform BYTEA, p_metadata JSONB)
RETURNS BIGINT AS $$
DECLARE
    v_audio_atom_id BIGINT;
    v_phonemes TEXT[];
    v_phoneme TEXT;
    v_phoneme_atom_id BIGINT;
BEGIN
    -- Create audio atom
    v_audio_atom_id := atomize_value(
        digest(p_audio_waveform, 'sha256'),
        NULL,
        p_metadata || '{"modality": "audio"}'::jsonb
    );

    -- Extract phonemes (via speech-to-text, returns IPA)
    v_phonemes := extract_phonemes(p_audio_waveform);

    FOR i IN 1..array_length(v_phonemes, 1) LOOP
        v_phoneme := v_phonemes[i];

        v_phoneme_atom_id := atomize_value(
            convert_to(v_phoneme, 'UTF8'),
            v_phoneme,
            '{"modality": "phoneme"}'::jsonb
        );

        -- Link phoneme to audio
        INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
        VALUES (v_audio_atom_id, v_phoneme_atom_id, i);
    END LOOP;

    RETURN v_audio_atom_id;
END;
$$ LANGUAGE plpgsql;
```

---

## Spatial Position Computation

### Semantic Neighbor Averaging

**Key algorithm**: New atom's position = weighted average of semantically similar atoms.

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

## Sparse Composition

**Concept**: Gaps in `sequence_index` = implicit zeros.

**Example**: Sparse vector `[1.5, 0, 0, 0, 3.2, 0, 0, 7.8]`

```sql
-- Store only non-zero components
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (@vector_id, @value_1_5_id, 0),
    (@vector_id, @value_3_2_id, 4),
    (@vector_id, @value_7_8_id, 7);
-- Indices 1,2,3,5,6 implicitly zero
```

**Reconstruction**:

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

## Model Output Ingestion

### GPT-4 Output

```sql
-- Ingest GPT-4 response
CREATE FUNCTION ingest_gpt4_response(p_prompt TEXT, p_response TEXT)
RETURNS BIGINT AS $$
DECLARE
    v_response_atom_id BIGINT;
    v_prompt_atom_id BIGINT;
BEGIN
    -- Atomize prompt
    v_prompt_atom_id := ingest_document(p_prompt, '{"model": "user"}'::jsonb);

    -- Atomize response
    v_response_atom_id := ingest_document(p_response, '{"model": "gpt-4"}'::jsonb);

    -- Create causal relation
    INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight)
    VALUES (
        v_prompt_atom_id,
        v_response_atom_id,
        (SELECT atom_id FROM atom WHERE canonical_text = 'prompt_response'),
        0.9
    );

    RETURN v_response_atom_id;
END;
$$ LANGUAGE plpgsql;
```

### DALL-E Image

```sql
-- Ingest generated image
CREATE FUNCTION ingest_dalle_image(p_prompt TEXT, p_image_bytes BYTEA)
RETURNS BIGINT AS $$
DECLARE
    v_image_atom_id BIGINT;
    v_prompt_atom_id BIGINT;
BEGIN
    v_prompt_atom_id := ingest_document(p_prompt, '{"model": "user"}'::jsonb);
    v_image_atom_id := atomize_image(p_image_bytes, '{"model": "dall-e-3"}'::jsonb);

    -- Link prompt to image
    INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id)
    VALUES (
        v_prompt_atom_id,
        v_image_atom_id,
        (SELECT atom_id FROM atom WHERE canonical_text = 'generates')
    );

    RETURN v_image_atom_id;
END;
$$ LANGUAGE plpgsql;
```

---

## Batch Ingestion

```sql
-- Batch atomize for performance
CREATE FUNCTION batch_ingest_documents(p_docs TEXT[], p_metadata JSONB)
RETURNS BIGINT[] AS $$
DECLARE
    v_doc TEXT;
    v_doc_ids BIGINT[];
BEGIN
    FOREACH v_doc IN ARRAY p_docs LOOP
        v_doc_ids := array_append(v_doc_ids, ingest_document(v_doc, p_metadata));
    END LOOP;

    RETURN v_doc_ids;
END;
$$ LANGUAGE plpgsql;
```

**Optimization**: Use `COPY` for bulk inserts.

```sql
-- Bulk insert atoms from CSV
COPY atom (content_hash, atomic_value, canonical_text, metadata)
FROM '/path/to/atoms.csv' CSV HEADER;
```

---

## Reference Counting Strategy

**Increment on use**:

```sql
-- Each composition increments component reference_count
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

**Decrement on deletion**:

```sql
CREATE TRIGGER decrement_reference_count
AFTER DELETE ON atom_composition
FOR EACH ROW
EXECUTE FUNCTION decrement_component_refcount();
```

---

## Provenance Metadata

Track ingestion source:

```json
{
  "model": "gpt-4",
  "version": "2024-01",
  "timestamp": "2025-01-15T10:30:00Z",
  "source": "api",
  "user_id": 12345,
  "session_id": "abc-def-ghi"
}
```

**Query provenance**:

```sql
-- Find all atoms from specific session
SELECT * FROM atom
WHERE metadata->>'session_id' = 'abc-def-ghi';
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

## Anti-Patterns

**Don't**: Store full document as single atom (exceeds 64-byte limit)
**Do**: Decompose hierarchically

**Don't**: Recompute spatial positions on every query
**Do**: Materialize positions, update periodically

**Don't**: Insert atoms sequentially in loop
**Do**: Batch insert via `COPY` or array operations

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

---

**See Also**: [04-MULTI-MODEL.md](04-MULTI-MODEL.md), [09-INFERENCE.md](09-INFERENCE.md)
