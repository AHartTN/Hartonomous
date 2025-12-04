# Ingestion Pipeline Implementation Plan

**Goal:** Build complete multi-modal ingestion system with extreme granular atomization

---

## Phase 1: Schema Correction (Week 1 - Days 1-2)

### 1.1 Migrate POINTZ → POINTZM
**Current:** `spatial_key GEOMETRY(POINTZ)` - Only X, Y, Z  
**Target:** `spatial_key GEOMETRY(POINTZM)` - X, Y, Z, **M (Hilbert index)**

```sql
-- Migration script
ALTER TABLE atom 
  ALTER COLUMN spatial_key TYPE GEOMETRY(POINTZM, 0);

ALTER TABLE atom_composition 
  ALTER COLUMN spatial_key TYPE GEOMETRY(POINTZM, 0);

-- Update existing atoms to include Hilbert in M dimension
UPDATE atom 
SET spatial_key = ST_MakePointM(
  ST_X(spatial_key),
  ST_Y(spatial_key),
  ST_Z(spatial_key),
  COALESCE((metadata->>'hilbert_index')::BIGINT, 0)
)
WHERE spatial_key IS NOT NULL;

-- Create B-tree index on Hilbert (M dimension) for O(log n) queries
CREATE INDEX idx_atom_hilbert ON atom ((ST_M(spatial_key)));
```

**Files to update:**
- `schema/core/tables/001_atom.sql`
- `schema/core/tables/002_atom_composition.sql`
- All `atomize_*` functions to use `ST_MakePointM(x, y, z, hilbert)`

---

## Phase 2: Core Text Ingestion (Week 1 - Days 3-5)

### 2.1 Text Endpoint
**File:** `api/routes/ingest.py`

```python
@router.post("/v1/ingest/text")
async def ingest_text(
    text: str,
    metadata: Optional[Dict] = None,
    hierarchical: bool = True,
    spatial_positioning: str = "gpu"
):
    """
    Atomize text at character, word, sentence levels.
    Returns: {root_atom_id, atom_count, composition_count}
    """
```

**SQL Functions:**
- ✅ `atomize_text()` - EXISTS (character-level)
- 🔨 `atomize_words()` - ENHANCE (add spatial positioning)
- 🔨 `atomize_sentences()` - CREATE
- 🔨 `atomize_document()` - CREATE

### 2.2 GPU Batch Positioning
**File:** `schema/core/functions/gpu/gpu_batch_text_embed.sql`

```sql
CREATE OR REPLACE FUNCTION gpu_batch_text_embed(
    p_atom_ids BIGINT[],
    p_texts TEXT[]
)
RETURNS TABLE(atom_id BIGINT, spatial_key GEOMETRY)
LANGUAGE plpython3u
AS $$
import torch
import numpy as np
from transformers import AutoModel, AutoTokenizer

# Load lightweight model (e.g., MiniLM)
model = AutoModel.from_pretrained("sentence-transformers/all-MiniLM-L6-v2")
tokenizer = AutoTokenizer.from_pretrained("sentence-transformers/all-MiniLM-L6-v2")

# Batch encode
embeddings = model.encode(p_texts, device="cuda")  # (N, 384)

# Project to 3D + compute Hilbert
for atom_id, emb in zip(p_atom_ids, embeddings):
    # PCA/UMAP to 3D
    x, y, z = project_to_3d(emb)
    
    # Hilbert curve index
    hilbert_idx = hilbert_index_3d(x, y, z)
    
    # Return POINTZM
    yield (atom_id, f"POINTZM({x} {y} {z} {hilbert_idx})")
$$;
```

---

## Phase 3: Document Parser (Week 2)

### 3.1 Document Endpoint
**File:** `api/routes/documents.py`

```python
@router.post("/v1/ingest/document")
async def ingest_document(
    file: UploadFile,
    format: str = "pdf",
    extract_images: bool = True,
    ocr_enabled: bool = False,
    metadata: Optional[Dict] = None
):
    """
    Parse PDF/DOCX/MD/HTML → atomize structure + content.
    Returns: {root_atom_id, structure_tree, atom_count}
    """
```

**Dependencies:**
```bash
pip install pypdf2 pdfplumber python-docx markdown-it-py beautifulsoup4 pytesseract
```

**Parsing Logic:**
1. **PDF**: Extract pages → paragraphs → text + images
2. **DOCX**: Sections → headings → lists → tables
3. **Markdown**: Headers → code blocks → links
4. **HTML**: DOM tree → text nodes + images

**SQL Integration:**
```python
# For each document element:
async with conn.cursor() as cur:
    # Create element atom
    await cur.execute(
        "SELECT atomize_value(%s, %s, %s::jsonb)",
        (element_hash, element_text, element_metadata)
    )
    
    # Atomize content (character-level)
    await cur.execute(
        "SELECT atomize_text(%s, %s::jsonb)",
        (element_text, metadata)
    )
    
    # Link via composition
    await cur.execute(
        "SELECT create_composition(%s, %s, %s, %s::jsonb)",
        (parent_id, child_id, sequence_index, comp_metadata)
    )
```

---

## Phase 4: Image Ingestion (Week 3)

### 4.1 Image Endpoint
**File:** `api/routes/ingest.py`

```python
@router.post("/v1/ingest/image")
async def ingest_image(
    file: UploadFile,
    patch_size: int = 16,
    compression: str = "hilbert_lod",
    skip_background: bool = True,
    metadata: Optional[Dict] = None
):
    """
    Atomize image at pixel or patch level with Hilbert compression.
    Returns: {root_atom_id, pixel_count, compression_ratio}
    """
```

**Atomization Strategies:**

1. **Pixel-level** (extreme):
```python
from PIL import Image
import numpy as np

img = Image.open(file).convert("RGB")
pixels = np.array(img)

async with conn.cursor() as cur:
    for y, x in np.ndindex(pixels.shape[:2]):
        r, g, b = pixels[y, x]
        await cur.execute(
            "SELECT atomize_pixel(%s, %s, %s, %s, %s, %s::jsonb)",
            (r, g, b, x, y, metadata)
        )
```

2. **Patch-based with Hilbert LOD** (recommended):
```sql
CREATE FUNCTION atomize_image_patches(
    p_image_id BIGINT,
    p_patches JSONB[]  -- [{x, y, avg_r, avg_g, avg_b, variance}, ...]
)
RETURNS BIGINT[]
LANGUAGE plpgsql
AS $$
DECLARE
    v_patch JSONB;
    v_patch_atom_id BIGINT;
    v_atom_ids BIGINT[];
BEGIN
    FOREACH v_patch IN ARRAY p_patches LOOP
        -- Skip low-variance patches (background)
        CONTINUE WHEN (v_patch->>'variance')::REAL < 0.01;
        
        -- Atomize patch as average color
        v_patch_atom_id := atomize_pixel(
            (v_patch->>'avg_r')::INTEGER,
            (v_patch->>'avg_g')::INTEGER,
            (v_patch->>'avg_b')::INTEGER,
            (v_patch->>'x')::INTEGER,
            (v_patch->>'y')::INTEGER,
            v_patch
        );
        
        v_atom_ids := array_append(v_atom_ids, v_patch_atom_id);
        
        -- Link to image
        INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
        VALUES (p_image_id, v_patch_atom_id, array_length(v_atom_ids, 1));
    END LOOP;
    
    RETURN v_atom_ids;
END;
$$;
```

### 4.2 Hilbert LOD Compression
**File:** `schema/core/functions/atomization/atomize_hilbert_lod.sql`

Already exists! Just needs integration:
```sql
SELECT atomize_hilbert_lod(
    hilbert_start := rgb_to_hilbert(patch_color_min),
    hilbert_end := rgb_to_hilbert(patch_color_max),
    lod_level := 2,
    representative_atom_id := avg_color_atom_id,
    metadata := jsonb_build_object('variance', patch_variance)
);
```

---

## Phase 5: Audio/Video Ingestion (Week 4)

### 5.1 Audio Endpoint
```python
@router.post("/v1/ingest/audio")
async def ingest_audio(
    file: UploadFile,
    format: str = "wav",
    sparse_threshold: float = 0.001,
    metadata: Optional[Dict] = None
):
    """
    Atomize audio at sample level with sparse encoding.
    """
```

**Sparse Storage:**
```sql
-- Only store non-silent samples
SELECT atomize_audio_sparse(
    p_sample_times := ARRAY[0.001, 0.005, 0.010, ...],
    p_amplitudes := ARRAY[0.5, -0.3, 0.8, ...],
    p_channel := 0
)
WHERE ABS(amplitude) > sparse_threshold;
```

### 5.2 Video Endpoint
```python
@router.post("/v1/ingest/video")
async def ingest_video(
    file: UploadFile,
    fps_sample: int = 1,  # Sample 1 FPS (not all 30 FPS)
    delta_encoding: bool = True,
    metadata: Optional[Dict] = None
):
    """
    Atomize video frames with delta encoding.
    """
```

**Delta Encoding:**
```sql
-- Store pixel changes between frames, not full frames
SELECT atomize_pixel_delta(
    prev_pixel_atom_id := @prev_frame_pixel,
    r_delta := curr_r - prev_r,
    g_delta := curr_g - prev_g,
    b_delta := curr_b - prev_b,
    x := pixel_x,
    y := pixel_y
);
```

---

## Phase 6: AI Model Ingestion (Week 5)

### 6.1 Model Weight Atomization
```python
@router.post("/v1/ingest/model")
async def ingest_model(
    model_path: str,
    quantization: str = "int8",  # int8, fp16, fp32
    sparse_threshold: float = 0.001,
    metadata: Optional[Dict] = None
):
    """
    Atomize AI model weights as atoms.
    """
```

**Weight Storage:**
```python
import torch

model = torch.load(model_path)

for layer_name, layer_weights in model.named_parameters():
    # Quantize to int8 (1 byte per weight)
    quantized = (layer_weights * 127).clamp(-128, 127).to(torch.int8)
    
    # Sparse: Skip near-zero weights
    non_zero_indices = torch.nonzero(torch.abs(quantized) > threshold)
    
    for idx in non_zero_indices:
        weight_value = quantized[tuple(idx)].item()
        
        # Atomize weight
        await cur.execute(
            "SELECT atomize_numeric(%s, %s::jsonb)",
            (weight_value, {
                "modality": "model_weight",
                "model": "llama-7b",
                "layer": layer_name,
                "index": idx.tolist()
            })
        )
```

**Model Composition:**
```
Model (root atom)
  ├─ Layer 1 (atom)
  │   ├─ Weight[0,0] (atom)
  │   ├─ Weight[0,1] (atom)
  │   └─ ...
  ├─ Layer 2 (atom)
  └─ ...
```

---

## Phase 7: Code Atomization Integration (Week 6)

**Already exists:** C# microservice for Roslyn/Tree-sitter AST parsing

**Enhancement:** Ensure proper Hilbert positioning for code atoms

```sql
-- Code atoms should cluster by:
-- X-axis: Language (C#=0.1, Python=0.3, JS=0.5)
-- Y-axis: AST type (class=0.1, method=0.3, field=0.5)
-- Z-axis: Complexity (simple=0.1, complex=0.9)

UPDATE atom
SET spatial_key = ST_MakePointM(
    language_coord(metadata->>'language'),
    ast_type_coord(metadata->>'ast_type'),
    complexity_score(metadata->>'complexity'),
    hilbert_index_3d(x, y, z)
)
WHERE metadata->>'modality' = 'code';
```

---

## Performance Targets

| Operation | Target | Current |
|-----------|--------|---------|
| Text atomization | 50K chars/sec (GPU) | ~1K chars/sec |
| Image atomization | 1M pixels/sec | ~50K pixels/sec |
| Document parsing | 100 pages/sec | N/A |
| Audio sparse storage | 10MB/sec | N/A |
| Model weight ingestion | 1B weights/min | N/A |

---

## Dependencies to Install

```bash
# Document parsing
pip install pypdf2 pdfplumber python-docx markdown-it-py beautifulsoup4 lxml

# OCR
pip install pytesseract
sudo apt-get install tesseract-ocr

# Image/Video processing
pip install pillow opencv-python numpy

# Audio processing
pip install librosa soundfile

# AI model loading
pip install torch transformers onnx

# GPU acceleration
pip install cupy numba
```

---

## Testing Strategy

### Unit Tests
```python
# test_ingestion.py
async def test_text_atomization():
    result = await atomize_text("Hello World")
    assert result["atom_count"] == 11  # H e l l o   W o r l d
    assert "l" in result["deduplicated"]  # 'l' appears 3 times

async def test_image_atomization():
    img = create_test_image(100, 100, color=(255, 0, 0))
    result = await atomize_image(img, patch_size=16)
    assert result["compression_ratio"] > 10  # Hilbert LOD compression
```

### Integration Tests
```bash
# End-to-end ingestion pipeline
pytest tests/integration/test_full_document_ingestion.py
pytest tests/integration/test_multi_modal_ingestion.py
```

### Performance Benchmarks
```python
# benchmark_ingestion.py
@benchmark
async def bench_text_1m_chars():
    text = "a" * 1_000_000
    result = await atomize_text(text, spatial_positioning="gpu")
    assert result["time_ms"] < 50  # 20K chars/ms = 20M chars/sec
```

---

## Deployment Checklist

- [ ] Schema migration (POINTZ → POINTZM)
- [ ] GPU batch positioning functions
- [ ] Text ingestion endpoint
- [ ] Document parser endpoints
- [ ] Image atomization (pixel + patch)
- [ ] Hilbert LOD compression
- [ ] Audio sparse encoding
- [ ] Video delta encoding
- [ ] Model weight ingestion
- [ ] Code atomization integration
- [ ] Performance tests
- [ ] Documentation
- [ ] Docker deployment updates

---

## Next Actions

1. **Schema migration first** - Everything depends on POINTZM
2. **GPU batch positioning** - Core performance bottleneck
3. **Text + Document ingestion** - Most common use case
4. **Image ingestion** - High volume, needs compression
5. **Audio/Video/Models** - Lower priority, advanced features

**Estimated Total Time:** 6 weeks (1 developer, full-time)
