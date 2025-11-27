# Dimension Strategy Analysis: X vs Y vs M for Hilbert Index

## Problem Statement

**Current proposal:** `POINTZM(x, y, z, m)` where M = Hilbert index  
**Question:** Should Hilbert go in M, or X/Y, freeing M for other uses?

---

## PostGIS Dimension Behavior

### Key Findings from Testing:

1. **ST_DWithin() only uses X,Y dimensions** (ignores Z, M)
   - `ST_DWithin(POINTZM(0,0,0,100), POINTZM(0,0,10,999), 5)` = TRUE
   - Distance calculated as: `sqrt((x2-x1)² + (y2-y1)²)` 
   - **Z and M dimensions are metadata, not spatial!**

2. **All dimensions are extractable:**
   - `ST_X()`, `ST_Y()`, `ST_Z()`, `ST_M()` all work
   - Can create B-tree index on any: `CREATE INDEX ON atom ((ST_M(spatial_key)))`

3. **GiST spatial index only uses X,Y:**
   - `CREATE INDEX USING GIST (spatial_key)` → 2D R-tree
   - Z and M not included in spatial bounding boxes

---

## Dimension Strategy Options

### Option 1: Hilbert in M (Current Proposal)

```sql
spatial_key GEOMETRY(POINTZM, 0)
-- X = Landmark projection X (modality)
-- Y = Landmark projection Y (category) 
-- Z = Landmark projection Z (specificity)
-- M = Hilbert index (computed from X,Y,Z)
```

**Pros:**
- ✅ Semantic meaning clear: X,Y,Z = landmark space, M = index
- ✅ Preserves 3D landmark coordinates for visualization
- ✅ M dimension conceptually separate (computed value)

**Cons:**
- ❌ ST_DWithin() ignores M → can't use for Hilbert range queries
- ❌ Need separate B-tree index: `CREATE INDEX ON atom ((ST_M(spatial_key)))`
- ❌ Two indexes: GiST (X,Y) + B-tree (M) = redundant storage

---

### Option 2: Hilbert in X, free M for custom use

```sql
spatial_key GEOMETRY(POINTZM, 0)
-- X = Hilbert index (1D space-filling curve)
-- Y = Landmark projection Y (category)
-- Z = Landmark projection Z (specificity)
-- M = Custom (encoding type, LOD level, timestamp, etc.)
```

**Pros:**
- ✅ GiST index on spatial_key automatically indexes Hilbert (X dimension)
- ✅ ST_DWithin() works for Hilbert range queries (if Y fixed)
- ✅ Frees M dimension for powerful metadata:
  - **Encoding type**: sparse=0, delta=1, RLE=2, LOD_level=3
  - **Timestamp**: valid_from as double (1735307784.123)
  - **Reference count**: atomic mass in geometry itself
  - **Compression ratio**: 0.0-1.0 scale

**Cons:**
- ❌ Loses direct 3D landmark visualization
- ❌ X no longer has semantic meaning (just index)
- ❌ Harder to debug: "What does X=42857 mean?"

---

### Option 3: Hilbert in Y, free M

```sql
spatial_key GEOMETRY(POINTZM, 0)
-- X = Landmark projection X (modality)
-- Y = Hilbert index (1D space-filling curve)
-- Z = Landmark projection Z (specificity)
-- M = Custom (encoding type, LOD, etc.)
```

**Pros:**
- ✅ Similar benefits to Option 2
- ✅ ST_DWithin() works if X is fixed (modality filtering)
- ✅ Frees M for metadata

**Cons:**
- ❌ Same loss of 3D visualization
- ❌ Arbitrary choice (why Y over X?)

---

### Option 4: Separate Hilbert Column (Best?)

```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    
    -- 3D Landmark space (pure semantic coordinates)
    spatial_key GEOMETRY(POINTZM, 0),
      -- X = Modality
      -- Y = Category
      -- Z = Specificity
      -- M = Encoding type / LOD level / custom metadata
    
    -- 1D Hilbert index (computed from X,Y,Z)
    hilbert_index BIGINT NOT NULL,
    
    -- B-tree index for O(log n) Hilbert range queries
    CREATE INDEX idx_atom_hilbert ON atom (hilbert_index);
);
```

**Pros:**
- ✅ **Separation of concerns:** Landmark space ≠ Hilbert index
- ✅ 3D visualization preserved (X,Y,Z still semantic)
- ✅ M dimension free for rich metadata (encoding, LOD, timestamp)
- ✅ Explicit Hilbert column = self-documenting schema
- ✅ B-tree index on dedicated column (cleaner)
- ✅ Can have multiple Hilbert schemes (RGB Hilbert, semantic Hilbert)

**Cons:**
- ❌ Extra 8 bytes per row (BIGINT)
- ❌ "Not in geometry" → less elegant?

**Counter-argument:**
- 8 bytes is nothing compared to 64-byte atomic_value
- Clarity > elegance
- PostGIS geometry is for **spatial** operations; Hilbert is **indexing**

---

## M Dimension Use Cases (If Freed)

### 1. Encoding Type Enum
```sql
-- M dimension encodes compression strategy
M = 0  → Raw (uncompressed)
M = 1  → Sparse (threshold-based)
M = 2  → Delta (from previous)
M = 3  → RLE (run-length encoding)
M = 4  → Hilbert LOD (level 0)
M = 5  → Hilbert LOD (level 1)
...
```

**Query:**
```sql
-- Find all delta-encoded pixels
SELECT * FROM atom 
WHERE ST_M(spatial_key) = 2;
```

---

### 2. LOD Level (Level of Detail)
```sql
-- M = LOD level for progressive loading
M = 0  → Full resolution (pixel-level)
M = 1  → 2x downsampled (4 pixels → 1 atom)
M = 2  → 4x downsampled (16 pixels → 1 atom)
M = 3  → 8x downsampled (64 pixels → 1 atom)
```

**Query:**
```sql
-- Load coarse image first (progressive JPEG style)
SELECT * FROM atom
WHERE parent_atom_id = @image_id
  AND ST_M(spatial_key) >= 3  -- Coarse first
ORDER BY ST_M(spatial_key) DESC;

-- Then refine
SELECT * FROM atom
WHERE parent_atom_id = @image_id
  AND ST_M(spatial_key) BETWEEN 1 AND 2;
```

---

### 3. Temporal Version (Timestamp)
```sql
-- M = UNIX timestamp (double precision)
M = 1735307784.123  → 2025-01-27 12:36:24.123 UTC

-- Valid_from encoded in geometry itself!
```

**Query:**
```sql
-- Time-travel query without joining atom_history
SELECT * FROM atom
WHERE ST_M(spatial_key) <= extract(epoch from '2025-01-01'::timestamp);
```

---

### 4. Reference Count (Atomic Mass)
```sql
-- M = log10(reference_count) for weight visualization
M = 0  → 1 reference (unique)
M = 1  → 10 references
M = 2  → 100 references
M = 3  → 1,000 references (common)
M = 6  → 1,000,000 references (universal like "the")
```

**Query:**
```sql
-- Find high-importance atoms
SELECT * FROM atom
WHERE ST_M(spatial_key) > 4  -- More than 10k references
ORDER BY ST_M(spatial_key) DESC;
```

---

### 5. Compression Ratio
```sql
-- M = compression achieved (0.0 - 1.0 scale)
M = 0.0  → No compression (raw)
M = 0.5  → 50% compressed
M = 0.95 → 95% compressed (Hilbert LOD)
```

---

## Encoding Strategy Enhancement

### Current Sparse Implementation (Good!)
```sql
-- atomize_audio_sparse.sql
IF ABS(p_samples[i]) > p_threshold THEN  -- Customizable threshold ✅
    -- Store atom
END IF;
```

**Already correct:** Threshold-based, not repeat-based!

---

### Additional Encoding Strategies Needed

#### 1. Run-Length Encoding (RLE)
```sql
CREATE FUNCTION atomize_rle(
    p_values ANYARRAY,
    p_threshold REAL DEFAULT 0.001
)
RETURNS BIGINT[]
AS $$
DECLARE
    v_prev_val ANYELEMENT;
    v_run_length INTEGER := 1;
    v_atom_ids BIGINT[];
BEGIN
    FOR i IN 1..array_length(p_values, 1) LOOP
        IF p_values[i] = v_prev_val OR ABS(p_values[i] - v_prev_val) < p_threshold THEN
            v_run_length := v_run_length + 1;
        ELSE
            -- Store run: (value, length)
            v_atom_ids := array_append(v_atom_ids, 
                atomize_value(
                    pack_rle(v_prev_val, v_run_length),
                    canonical_text := v_prev_val || ' x' || v_run_length,
                    metadata := jsonb_build_object(
                        'encoding', 'rle',
                        'value', v_prev_val,
                        'run_length', v_run_length
                    )
                )
            );
            
            v_prev_val := p_values[i];
            v_run_length := 1;
        END IF;
    END LOOP;
    
    RETURN v_atom_ids;
END;
$$;
```

**Use case:** Video frames (many repeated pixels), audio silence

---

#### 2. Sparse with Custom Comparator
```sql
CREATE FUNCTION atomize_sparse_custom(
    p_values ANYARRAY,
    p_is_significant REGPROC  -- Custom function: (value) → boolean
)
RETURNS BIGINT[]
AS $$
BEGIN
    FOR i IN 1..array_length(p_values, 1) LOOP
        IF execute('SELECT ' || p_is_significant || '($1)', p_values[i]) THEN
            -- Store significant value
            v_atom_ids := array_append(v_atom_ids, atomize_value(...));
        END IF;
    END LOOP;
    
    RETURN v_atom_ids;
END;
$$;

-- Example: Color variance significance
CREATE FUNCTION is_significant_color(rgb INTEGER[])
RETURNS BOOLEAN AS $$
    SELECT (rgb[1] - 128)^2 + (rgb[2] - 128)^2 + (rgb[3] - 128)^2 > 100;
$$ LANGUAGE sql;

-- Usage
SELECT atomize_sparse_custom(
    pixel_colors,
    'is_significant_color(int[])'
);
```

---

#### 3. Entropy-Based Encoding
```sql
-- Only store high-entropy regions (information-rich)
CREATE FUNCTION atomize_entropy_sparse(
    p_values REAL[],
    p_window_size INTEGER DEFAULT 8,
    p_min_entropy REAL DEFAULT 0.5
)
RETURNS BIGINT[]
AS $$
DECLARE
    v_entropy REAL;
BEGIN
    FOR i IN 1..(array_length(p_values, 1) - p_window_size + 1) LOOP
        -- Compute Shannon entropy of window
        v_entropy := compute_entropy(p_values[i:i+p_window_size]);
        
        IF v_entropy > p_min_entropy THEN
            -- High information content → store
            v_atom_ids := array_append(v_atom_ids, ...);
        END IF;
    END LOOP;
END;
$$;
```

---

## Recommendation: Option 4 (Separate Hilbert Column)

```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),
    canonical_text TEXT,
    
    -- 3D Landmark Space (semantic coordinates)
    spatial_key GEOMETRY(POINTZM, 0),
      -- X = Modality (code/text/image/audio/video)
      -- Y = Category (class/method/field/pixel/sample)
      -- Z = Specificity (abstract → concrete)
      -- M = Encoding metadata:
      --     0 = raw
      --     1 = sparse (threshold-based)
      --     2 = delta
      --     3 = RLE
      --     4+ = Hilbert LOD level (4=L0, 5=L1, ...)
    
    -- 1D Hilbert Index (for O(log n) queries)
    hilbert_index BIGINT NOT NULL,
    
    -- Standard fields
    reference_count BIGINT NOT NULL DEFAULT 1,
    metadata JSONB,
    created_at TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_from TIMESTAMPTZ NOT NULL DEFAULT now(),
    valid_to TIMESTAMPTZ NOT NULL DEFAULT 'infinity'
);

-- Indexes
CREATE INDEX idx_atom_spatial ON atom USING GIST (spatial_key);  -- 2D (X,Y)
CREATE INDEX idx_atom_hilbert ON atom (hilbert_index);           -- B-tree 1D
CREATE INDEX idx_atom_encoding ON atom ((ST_M(spatial_key)));    -- Filter by encoding
CREATE INDEX idx_atom_hash ON atom (content_hash);
```

---

## Migration Path

```sql
-- Phase 1: Add hilbert_index column
ALTER TABLE atom ADD COLUMN hilbert_index BIGINT;

-- Phase 2: Migrate POINTZ → POINTZM with M=encoding
ALTER TABLE atom 
    ALTER COLUMN spatial_key TYPE GEOMETRY(POINTZM, 0);

-- Phase 3: Populate hilbert_index from existing metadata
UPDATE atom 
SET hilbert_index = COALESCE(
    (metadata->>'hilbert_index')::BIGINT,
    hilbert_encode_3d(ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key))
);

-- Phase 4: Set M dimension based on metadata->>'encoding'
UPDATE atom
SET spatial_key = ST_MakePointM(
    ST_X(spatial_key),
    ST_Y(spatial_key),
    ST_Z(spatial_key),
    CASE metadata->>'encoding'
        WHEN 'raw' THEN 0
        WHEN 'sparse' THEN 1
        WHEN 'delta' THEN 2
        WHEN 'rle' THEN 3
        ELSE 0
    END
);

-- Phase 5: Add constraints
ALTER TABLE atom ALTER COLUMN hilbert_index SET NOT NULL;
CREATE INDEX idx_atom_hilbert ON atom (hilbert_index);
CREATE INDEX idx_atom_encoding ON atom ((ST_M(spatial_key)));
```

---

## Summary

**Recommendation:** Separate `hilbert_index` column + M dimension for encoding metadata

**Rationale:**
1. **Clarity:** Hilbert index is indexing mechanism, not spatial coordinate
2. **Flexibility:** M dimension becomes powerful metadata channel
3. **Performance:** Dedicated B-tree index on hilbert_index
4. **Extensibility:** Can add multiple Hilbert schemes later (RGB, semantic, temporal)

**M Dimension Priority Use:**
- **Primary:** Encoding type (0=raw, 1=sparse, 2=delta, 3=RLE, 4+=LOD_level)
- **Secondary (future):** Timestamp, reference_count, compression_ratio

**Encoding strategies to implement:**
- ✅ Sparse (threshold-based) - EXISTS
- ✅ Delta - EXISTS
- ✅ Hilbert LOD - EXISTS
- 🔨 RLE (run-length)
- 🔨 Entropy-based sparse
- 🔨 Custom comparator sparse
