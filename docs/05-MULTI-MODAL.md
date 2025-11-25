# MULTI-MODAL

Text, image, audio, video unified in single geometric space.

---

## Core Concept

**Traditional AI**: Separate embeddings per modality (CLIP bridges text/image)
**Hartonomous**: All modalities share 3D spatial geometry

**Why it works**: Semantic meaning is geometric position, regardless of encoding.

---

## Modality Encoding

### Text
Atoms = characters, words, sentences (hierarchical composition).

```sql
-- Text atomization
SELECT atomize_text('The quick brown fox');
-- Returns: atom_ids for 'T','h','e',' ','q',... (characters)
-- Parent atoms: 'The', 'quick', 'brown', 'fox' (words)
-- Root atom: 'The quick brown fox' (sentence)
```

### Image
Atoms = pixel patches, edges, objects (hierarchical).

```sql
-- Image patch atomization
INSERT INTO atom (atomic_value, spatial_key, metadata)
SELECT
    patch_bytes,
    compute_spatial_position(patch_bytes),
    '{"modality": "image_patch", "x": 10, "y": 20}'::jsonb
FROM extract_image_patches(@image_data, patch_size => 16);
```

**Strategy**: 16×16 pixel patches as base atoms. Larger objects composed from patches.

### Audio
Atoms = phonemes, words, utterances.

**Encoding**: Audio waveform as LINESTRING geometry (time × amplitude).

```sql
-- Audio atom with temporal geometry
INSERT INTO atom (canonical_text, spatial_key, metadata)
VALUES (
    '/kæt/',  -- IPA phoneme
    ST_MakeLine(ARRAY[
        ST_MakePoint(0.0, amplitude_0, freq_0),
        ST_MakePoint(0.01, amplitude_1, freq_1),
        ...
    ]),
    '{"modality": "phoneme", "language": "en"}'::jsonb
);
```

### Video
Atoms = frames (images) + temporal sequence (composition).

```sql
-- Video as composition of frame atoms
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
SELECT
    @video_atom_id,
    frame_atom_id,
    frame_number
FROM video_frames;
```

---

## Cross-Modal Queries

### Text → Image

```sql
-- Find images semantically near text query
SELECT
    a.atom_id,
    a.metadata->>'image_url' AS image,
    ST_Distance(a.spatial_key, @text_atom_position) AS distance
FROM atom a
WHERE a.metadata->>'modality' = 'image'
ORDER BY a.spatial_key <-> @text_atom_position
LIMIT 10;
```

**Example**: Query "sunset over ocean" → returns image atoms in that semantic region.

### Image → Text

```sql
-- Generate caption for image
SELECT
    a.canonical_text,
    a.reference_count,
    ST_Distance(a.spatial_key, @image_atom_position) AS distance
FROM atom a
WHERE a.metadata->>'modality' = 'text'
  AND ST_DWithin(a.spatial_key, @image_atom_position, 0.5)
ORDER BY reference_count DESC, distance ASC
LIMIT 20;
-- Compose top atoms into caption
```

### Audio → Text (Transcription)

```sql
-- Find text atoms near phoneme sequence
SELECT
    canonical_text,
    ST_Distance(spatial_key, @phoneme_centroid) AS distance
FROM atom
WHERE metadata->>'modality' = 'text'
ORDER BY spatial_key <-> @phoneme_centroid
LIMIT 10;
```

---

## Modality Translation = Spatial Proximity

**No separate CLIP model needed**. Geometry is the bridge.

```sql
-- Universal translation function
CREATE FUNCTION translate_modality(
    p_source_atom_id BIGINT,
    p_target_modality TEXT
) RETURNS TABLE(atom_id BIGINT, distance REAL) AS $$
BEGIN
    RETURN QUERY
    SELECT
        a.atom_id,
        ST_Distance(a.spatial_key, src.spatial_key)::REAL
    FROM atom src
    CROSS JOIN atom a
    WHERE src.atom_id = p_source_atom_id
      AND a.metadata->>'modality' = p_target_modality
      AND a.spatial_key IS NOT NULL
    ORDER BY a.spatial_key <-> src.spatial_key
    LIMIT 10;
END;
$$ LANGUAGE plpgsql;
```

---

## Cross-Modal Correlation Detection

Find concepts that appear in multiple modalities:

```sql
-- What images correlate with "cat" text?
SELECT
    img.metadata->>'image_id' AS image,
    COUNT(*) AS co_occurrence,
    AVG(ST_Distance(txt.spatial_key, img.spatial_key)) AS avg_distance
FROM atom txt
JOIN atom img ON ST_DWithin(txt.spatial_key, img.spatial_key, 0.3)
WHERE txt.canonical_text = 'cat'
  AND txt.metadata->>'modality' = 'text'
  AND img.metadata->>'modality' = 'image'
GROUP BY img.metadata->>'image_id'
ORDER BY co_occurrence DESC, avg_distance ASC;
```

---

## Unified Semantic Search

Query across all modalities:

```sql
-- Search for "Paris" (text, images, audio)
SELECT
    atom_id,
    canonical_text,
    metadata->>'modality' AS modality,
    ST_Distance(spatial_key, @query_point) AS distance
FROM atom
WHERE ST_DWithin(spatial_key, @query_point, 1.0)
ORDER BY reference_count DESC, distance ASC
LIMIT 100;
```

**Returns**: Text mentions, Paris images, French audio, all semantically aligned.

---

## Modality Filtering

```sql
-- Metadata patterns
WHERE metadata->>'modality' = 'text'
WHERE metadata->>'modality' = 'image'
WHERE metadata->>'modality' = 'audio'
WHERE metadata->>'modality' = 'video'
WHERE metadata->>'modality' IN ('image', 'video')  -- Visual only
```

---

## Hierarchical Composition Across Modalities

```sql
-- Document with text + images
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (@doc_id, @paragraph1_id, 1),
    (@doc_id, @image1_id, 2),
    (@doc_id, @paragraph2_id, 3),
    (@doc_id, @image2_id, 4);

-- Reconstruct
SELECT
    a.canonical_text,
    a.metadata->>'modality' AS type,
    ac.sequence_index
FROM atom_composition ac
JOIN atom a ON a.atom_id = ac.component_atom_id
WHERE ac.parent_atom_id = @doc_id
ORDER BY ac.sequence_index;
```

---

## Sparse Encoding for Efficiency

**Image patches**: Only store non-background patches.
**Audio**: Gaps in `sequence_index` = silence (implicit zeros).
**Video**: Store keyframes only, interpolate between.

```sql
-- Sparse audio representation
-- sequence_index: 0, 5, 10, 20 → gaps = silence
INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
VALUES
    (@utterance_id, @phoneme1_id, 0),
    (@utterance_id, @phoneme2_id, 5),    -- 1-4 = silence
    (@utterance_id, @phoneme3_id, 10),   -- 6-9 = silence
    (@utterance_id, @phoneme4_id, 20);   -- 11-19 = silence
```

---

## Semantic Position Computation

For new modality atoms, compute position via cross-modal neighbors:

```sql
-- Image patch position = average of semantically similar patches
CREATE FUNCTION compute_image_spatial_position(p_patch_bytes BYTEA)
RETURNS GEOMETRY AS $$
DECLARE
    v_centroid GEOMETRY;
BEGIN
    -- Find similar image patches (histogram similarity, edge detection)
    SELECT ST_Centroid(ST_Collect(spatial_key))
    INTO v_centroid
    FROM (
        SELECT spatial_key
        FROM atom
        WHERE metadata->>'modality' = 'image_patch'
        ORDER BY calculate_patch_similarity(atomic_value, p_patch_bytes) DESC
        LIMIT 100
    ) neighbors;

    RETURN v_centroid;
END;
$$ LANGUAGE plpgsql;
```

---

## Use Cases

**Content moderation**: Detect NSFW across text, image, video via unified queries.

**Accessibility**: Auto-caption images, transcribe audio, describe video.

**Search**: "Show me images of people laughing" works without labeled training data.

**Creative tools**: Generate image from text, music from mood description.

---

## Performance Characteristics

| Operation | Complexity | Time |
|-----------|-----------|------|
| Text → Image search | O(log N) R-tree | 0.5ms |
| Image patch atomization | O(P) patches | 50ms (1024 patches) |
| Audio transcription | O(log N) phoneme lookup | 2ms/second |
| Cross-modal correlation | O(K) neighborhood | 10ms |

N = total atoms, P = patches per image, K = neighbors

---

## Anti-Patterns

**Don't**: Store full image as single atom (exceeds 64-byte limit)
**Do**: Decompose into patches, use composition

**Don't**: Create separate tables per modality
**Do**: Use `metadata->>'modality'` filter

**Don't**: Pre-compute all cross-modal mappings
**Do**: Query-time spatial proximity

---

## GPU Acceleration (Optional)

```sql
-- Batch image patch embedding
CREATE FUNCTION gpu_embed_image_patches(patches BYTEA[])
RETURNS GEOMETRY[] AS $$
    import torch
    from torchvision import models
    device = "cuda" if torch.cuda.is_available() else "cpu"

    # Use pretrained CNN for feature extraction
    model = models.resnet18(pretrained=True).to(device)
    # ... project to 3D via PCA/UMAP
    return spatial_positions
$$ LANGUAGE plpython3u;
```

---

**See Also**: [04-MULTI-MODEL.md](04-MULTI-MODEL.md), [08-INGESTION.md](08-INGESTION.md)
