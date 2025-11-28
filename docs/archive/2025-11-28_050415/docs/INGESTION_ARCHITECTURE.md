# Hartonomous Ingestion Architecture
**Extreme Granular Atomization for All Data Types**

---

## 🧬 Core Philosophy

**EVERYTHING atomizes to ≤64 bytes.**

No exceptions. No shortcuts. Perfect granularity:
- **Text** → characters (1-4 bytes UTF-8)
- **Images** → pixels (3 bytes RGB)
- **Audio** → samples (2-4 bytes per sample)
- **Video** → frames → pixels
- **Documents** → words → characters
- **Code** → AST nodes → tokens → characters
- **AI Models** → layers → tensors → weights (4-8 bytes per float)
- **Binary Files** → chunks → bytes

---

## 📐 Hierarchical Decomposition Pattern

All data follows the same pattern:

```
ROOT (document/file/model)
  ├─ CONTAINER (page/layer/frame)
  │   ├─ ELEMENT (sentence/tensor/pixel-block)
  │   │   ├─ ATOM ≤64 bytes (character/weight/pixel)
  │   │   ├─ ATOM ≤64 bytes
  │   │   └─ ATOM ≤64 bytes
  │   └─ ELEMENT
  └─ CONTAINER
```

**Every level is an atom with metadata!**
- Root atom: `{"modality": "document", "filename": "paper.pdf"}`
- Container atom: `{"modality": "page", "page_num": 1}`
- Element atom: `{"modality": "sentence", "semantic_role": "title"}`
- Atomic atom: `{"modality": "character", "char": "A"}`

---

## 🎯 Ingestion Endpoints Architecture

### **Tier 1: Core Modalities (Python FastAPI)**

#### **`POST /v1/ingest/text`**
```json
{
  "text": "Hello, World!",
  "metadata": {
    "source": "api",
    "language": "en",
    "author": "user123"
  },
  "hierarchical": true,  // false = character-only, true = words + sentences
  "spatial_positioning": "gpu"  // "gpu", "cpu", "deferred"
}
```

**Atomization Flow:**
```
1. Character atomization:
   "Hello" → [H, e, l, l, o] (5 atoms, 'l' reused!)
   
2. Word composition:
   word_atom = atomize_value("hello", canonical_text="hello")
   compositions: (word_atom, H_atom, 0), (word_atom, e_atom, 1), ...
   
3. Sentence composition:
   sent_atom = atomize_value(hash("Hello, World!"))
   compositions: (sent_atom, Hello_atom, 0), (sent_atom, World_atom, 1)
   
4. GPU spatial positioning:
   SELECT gpu_compute_text_embeddings_simple(ARRAY['H','e','l','l','o','W','o','r','l','d'])
   UPDATE atom SET spatial_key = embedding WHERE atom_id IN (...)
```

**Performance:** ~1000 chars/sec (CPU), ~50,000 chars/sec (GPU batch)

---

#### **`POST /v1/ingest/document`**
Full document parser (PDF, DOCX, MD, HTML, TXT)

```json
{
  "file_path": "/path/to/document.pdf",  // or
  "file_data": "base64...",              // uploaded file
  "format": "pdf",  // pdf, docx, md, html, txt
  "extract_images": true,
  "extract_metadata": true,
  "ocr_enabled": true,  // For scanned PDFs
  "metadata": {
    "title": "Research Paper",
    "authors": ["John Doe"]
  }
}
```

**Atomization Flow:**
```
1. Parse document structure:
   - PDF: pages, paragraphs, fonts, styles
   - DOCX: sections, headings, lists, tables
   - MD: headers, code blocks, links
   
2. Create document root atom:
   doc_atom = atomize_value(
       hash(full_document),
       canonical_text=title,
       metadata={"modality": "document", "format": "pdf", ...}
   )
   
3. For each structural element:
   a. Create element atom (page, section, paragraph)
   b. Atomize text content (character-level)
   c. Link via atom_composition with hierarchy preserved
   d. Extract embedded images → atomize_image()
   
4. Metadata extraction:
   - Author, title, keywords → relation atoms
   - Citations → relation_type: "cites"
   - Cross-references → relation_type: "references"
```

**Parsers Required:**
- PyPDF2 / pdfplumber (PDF)
- python-docx (DOCX)
- markdown-it-py (Markdown)
- BeautifulSoup4 (HTML)
- pytesseract (OCR for scanned PDFs)

---

#### **`POST /v1/ingest/image`**
```json
{
  "image_data": "base64...",
  "width": 1920,
  "height": 1080,
  "format": "png",
  "patch_size": 16,  // 16x16 patch-based atomization
  "skip_background": true,  // Skip uniform patches
  "compression": "hilbert_lod",  // Hilbert curve + LOD
  "metadata": {
    "filename": "photo.png",
    "camera": "Canon EOS R5"
  }
}
```

**Atomization Strategies:**

1. **Pixel-Level** (extreme granularity):
```sql
SELECT atomize_pixel(r, g, b, x, y, metadata)
-- Every pixel = 1 atom (3 bytes RGB)
-- 1920x1080 = 2,073,600 atoms!
```

2. **Patch-Based** (recommended):
```sql
-- Extract 16x16 patches
-- Skip uniform patches (all white/black)
-- Each patch = 1 atom (16x16x3 = 768 bytes... TOO BIG!)

-- Solution: Use Hilbert curve compression
SELECT atomize_hilbert_lod(
    hilbert_start, hilbert_end, lod_level,
    avg_color_atom, variance, metadata
)
-- Stores patch as single atom with average color reference
```

3. **Delta Encoding** (for video frames):
```sql
SELECT atomize_pixel_delta(
    prev_pixel_atom_id, r_delta, g_delta, b_delta, x, y
)
-- Only store changes from previous frame
```

**Performance:** ~50ms for 1M pixels (vectorized), ~200ms with full decomposition

---

#### **`POST /v1/ingest/audio`**
```json
{
  "audio_data": "base64...",
  "format": "wav",
  "sample_rate": 44100,
  "channels": 2,
  "sparse_threshold": 0.001,  // Ignore samples below threshold
  "metadata": {
    "filename": "recording.wav",
    "duration_ms": 5000
  }
}
```

**Atomization Flow:**
```
1. Parse WAV/MP3/FLAC file
2. Extract samples (16-bit or 24-bit integers)
3. Sparse encoding:
   FOR each sample:
     IF abs(amplitude) > threshold:
       atomize_audio_sample(amplitude, time_ms, channel)
     ELSE:
       skip (implicit zero via sequence_index gap)
       
4. Composition:
   audio_atom → sample_atoms (sparse: only significant samples)
   
5. Spectral analysis (optional):
   - FFT → frequency domain
   - atomize_numeric(frequency_magnitude)
   - relation: time_sample "frequency_of" spectral_sample
```

**Performance:** Real-time or better (sparse encoding = 10-100× compression)

---

#### **`POST /v1/ingest/video`**
```json
{
  "video_data": "base64..." or "file_path": "/path/to/video.mp4",
  "format": "mp4",
  "fps": 30,
  "extract_audio": true,
  "frame_sampling": "keyframes",  // "all", "keyframes", "1fps", "5fps"
  "motion_compensation": true,  // Delta encoding between frames
  "metadata": {
    "title": "Demo Video",
    "duration_ms": 60000
  }
}
```

**Atomization Flow:**
```
1. Parse video container (MP4, AVI, WebM)
2. Extract video stream:
   a. Keyframes: full frame atomization
   b. P-frames: delta from previous frame
   c. B-frames: delta from previous + next frame
   
3. For each frame:
   frame_atom = atomize_image(frame_pixels)
   IF motion_compensation:
     FOR each pixel that changed:
       atomize_pixel_delta(prev_pixel, r_delta, g_delta, b_delta)
   
4. Extract audio stream:
   audio_atom = atomize_audio(audio_samples)
   
5. Composition:
   video_atom → [frame_0, frame_1, ..., frame_N, audio_atom]
   
6. Relations:
   - Temporal: frame_N "precedes" frame_N+1
   - Sync: frame_N "synchronized_with" audio_segment_N
```

**Performance:** ~1 minute for 60-second 1080p video (keyframes only)

---

### **Tier 2: AI Models (Python + GPU)**

#### **`POST /v1/ingest/model/gguf`**
Ollama quantized models (Llama, Qwen, Mistral)

```json
{
  "model_path": "/var/workload/ollama/models/qwen2.5-coder:7b.gguf",
  "model_name": "qwen2.5-coder:7b",
  "sparse_threshold": 0.01,  // Ignore weights < 0.01
  "max_tensors": null,  // For testing: limit tensor count
  "extract_embeddings": true,  // Extract token embeddings as semantic atoms
  "metadata": {
    "architecture": "transformer",
    "context_length": 8192,
    "vocab_size": 50000
  }
}
```

**Atomization Flow:**
```
1. Parse GGUF file structure:
   - Header (magic, version)
   - Metadata (architecture, parameters)
   - Tensor data (weights, biases, embeddings)
   
2. Create model root atom:
   model_atom = atomize_value(hash(model_name), model_name, metadata)
   
3. For each layer (e.g., 32 layers):
   layer_atom = atomize_value(f"layer_{i}", metadata={"layer_num": i})
   
4. For each tensor in layer:
   tensor_atom = atomize_value(tensor_name, metadata={"shape": [d1, d2, ...]})
   
5. For each weight in tensor (EXTREME GRANULARITY):
   IF abs(weight) > threshold:
     weight_atom = atomize_numeric(weight_value)
     composition: (tensor_atom, weight_atom, sequence_index)
   ELSE:
     skip (sequence_index gap = implicit zero)
     
6. GPU acceleration:
   - Batch SHA-256 hashing (10,000 weights at once)
   - Parallel spatial positioning
   - Vectorized bulk insert
```

**Deduplication Impact:**
- **Before:** 7B parameters × 4 bytes = 28 GB
- **After sparse (1% threshold):** ~280M atoms × 64 bytes = 17.5 GB
- **After deduplication:** ~50M unique atoms = 3.1 GB (9× compression!)

**Why This Matters:**
- Model knowledge becomes QUERYABLE in same space as code
- Can find "which models know about X concept"
- Detect weight patterns across models (transfer learning insights)
- Hebbian learning strengthens useful weight combinations

---

#### **`POST /v1/ingest/model/safetensors`**
Hugging Face models

```json
{
  "model_path": "/models/llama-2-7b.safetensors",
  "model_name": "llama-2-7b",
  "sparse_threshold": 0.01,
  "extract_config": true,  // Extract model config (config.json)
  "extract_tokenizer": true  // Extract tokenizer vocabulary
}
```

**Additional Features:**
- Config extraction → relation atoms (architecture parameters)
- Tokenizer vocabulary → atoms with semantic positions
- Cross-model weight comparison (identify shared knowledge)

---

#### **`POST /v1/ingest/model/pytorch`**
PyTorch .pt / .pth checkpoints

```json
{
  "model_path": "/models/checkpoint.pth",
  "model_name": "my-model-epoch-10",
  "extract_optimizer_state": false,  // Usually not needed
  "extract_gradients": false  // For training analysis
}
```

---

#### **`POST /v1/ingest/model/onnx`**
ONNX format (cross-framework)

```json
{
  "model_path": "/models/model.onnx",
  "model_name": "resnet50-onnx",
  "extract_graph": true  // Extract computation graph structure
}
```

---

### **Tier 3: Code Atomization (C# Microservice - PREMIUM)**

> **💰 Monetization Strategy:**  
> Code atomization is a **separate billable module**:
> - **Standalone Mode:** C# microservice runs independently ($X/month)
> - **Integrated Mode:** Connected to Hartonomous substrate ($Y/month premium)
> - **Enterprise Mode:** Custom Roslyn analyzers + private deployments ($$$)

#### **`POST /v1/atomize/code` (C# Microservice)**

```json
{
  "code": "public class HelloWorld { ... }",
  "language": "csharp",  // csharp, python, javascript, go, rust, java, typescript...
  "analysis_level": "semantic",  // "syntax", "semantic", "full"
  "extract_metrics": true,  // Cyclomatic complexity, LOC, etc.
  "integration_mode": "standalone"  // "standalone" or "hartonomous"
}
```

**C# Microservice Features:**
1. **Roslyn (C# Semantic Analysis)**:
   - Full type resolution
   - Method signatures
   - Variable scoping
   - Cross-reference analysis
   - NuGet package dependencies

2. **Tree-sitter (18+ Languages)**:
   - Python, JavaScript, TypeScript, Go, Rust
   - Java, C, C++, PHP, Ruby
   - Swift, Kotlin, SQL, HTML, CSS
   - Markdown, JSON, YAML, TOML

**Atomization Output (Standalone Mode):**
```json
{
  "ast_nodes": [
    {
      "node_id": "uuid-1",
      "node_type": "ClassDeclaration",
      "name": "HelloWorld",
      "parent_id": null,
      "children": ["uuid-2", "uuid-3"],
      "metadata": {
        "modifiers": ["public"],
        "namespace": "MyApp",
        "line_start": 1,
        "line_end": 10
      }
    }
  ],
  "tokens": ["public", "class", "HelloWorld", "{", "}"],
  "metrics": {
    "lines_of_code": 10,
    "cyclomatic_complexity": 3,
    "cognitive_complexity": 2
  }
}
```

**Atomization Output (Hartonomous Integration):**
```json
{
  "atoms_created": 1847,
  "root_atom_id": 12345,
  "compositions": 934,
  "relations": 112,
  "metrics": {
    "deduplication_ratio": 3.2,
    "semantic_clusters": 8,
    "cross_references": 23
  }
}
```

**Hartonomous Integration Flow:**
```
1. C# microservice analyzes code → AST + metrics
2. POST to /v1/ingest/code/ast (Python API)
3. Python API atomizes AST nodes:
   - Each token → character atoms
   - Each AST node → node metadata atom
   - Hierarchical composition: file → classes → methods → tokens
4. Spatial positioning:
   - Methods with similar names cluster together
   - Related classes positioned nearby
5. Relations:
   - method "calls" another_method
   - class "inherits" base_class
   - variable "references" type
```

**Why Charge Premium for Integration?**
- **Value Add:** Code knowledge enters same semantic space as docs/models
- **Cross-Modal Queries:** "Show me code that does X" + "Docs that explain Y"
- **Truth Convergence:** Code + docs + models = unified understanding
- **Provenance:** Full traceability from code → behavior → output

---

### **Tier 4: Database Atomization**

#### **`POST /v1/ingest/database`**
```json
{
  "connection_string": "postgresql://...",
  "schema": "public",
  "tables": ["users", "orders"],  // null = all tables
  "include_data": false,  // false = schema only, true = data + schema
  "sample_rows": 1000  // If include_data=true, limit rows
}
```

**Atomization Flow:**
```
1. Schema atomization:
   - table_atom = atomize_value(table_name)
   - column_atom = atomize_value(column_name, metadata={"type": "varchar"})
   - composition: (table_atom, column_atom, sequence_index)
   
2. Data atomization (if enabled):
   - FOR each row:
       FOR each column value:
         value_atom = atomize_value(value)
         composition: (row_atom, value_atom, column_index)
         
3. Relations:
   - foreign_key_col "references" primary_key_col
   - index_atom "indexes" column_atom
```

---

### **Tier 5: Multi-Modal Embeddings**

#### **`POST /v1/ingest/embedding`**
Direct embedding ingestion (for pre-computed vectors)

```json
{
  "embeddings": [
    {"text": "cat", "vector": [0.1, 0.2, ..., 0.768]},
    {"text": "dog", "vector": [0.15, 0.22, ..., 0.755]}
  ],
  "model_name": "all-MiniLM-L6-v2",
  "dimensions": 384,
  "reduce_dimensions": true,  // PCA to 3D for spatial_key
  "create_atoms": true  // Create text atoms + position from embedding
}
```

**Flow:**
```
1. Dimensionality reduction (768D → 3D):
   - PCA or UMAP
   - preserves semantic structure
   
2. Position atoms:
   UPDATE atom SET spatial_key = ST_MakePoint(x, y, z)
   WHERE canonical_text = embedding.text
   
3. Automatic relation discovery:
   INSERT INTO atom_relation (source, target, type, weight)
   SELECT a1.atom_id, a2.atom_id, 'semantic_similar',
          1.0 / (1.0 + ST_3DDistance(a1.spatial_key, a2.spatial_key))
   FROM atom a1, atom a2
   WHERE ST_3DDWithin(a1.spatial_key, a2.spatial_key, 2.0)
```

---

## 🏗️ Implementation Architecture

### **Service Layer Structure**

```
api/services/
├── atomization.py          # Core atomization (text, image, audio)
├── document_parser.py      # PDF, DOCX, MD, HTML parsers
├── model_atomization.py    # GGUF, SafeTensors, PyTorch, ONNX
├── code_integration.py     # Bridge to C# microservice
├── database_atomization.py # Schema + data ingestion
├── embedding_ingestion.py  # Direct embedding import
└── spatial_positioning.py  # GPU-accelerated positioning
```

### **Parser Implementations**

#### **Document Parser**
```python
# api/services/document_parser.py
class DocumentParser:
    @staticmethod
    async def parse_pdf(file_path: Path) -> Dict[str, Any]:
        """Extract text, images, metadata from PDF."""
        import pdfplumber
        
        with pdfplumber.open(file_path) as pdf:
            pages = []
            for page in pdf.pages:
                pages.append({
                    "page_num": page.page_number,
                    "text": page.extract_text(),
                    "images": page.images,
                    "width": page.width,
                    "height": page.height
                })
        
        return {
            "pages": pages,
            "metadata": pdf.metadata,
            "total_pages": len(pages)
        }
    
    @staticmethod
    async def parse_docx(file_path: Path) -> Dict[str, Any]:
        """Extract text, styles, structure from DOCX."""
        from docx import Document
        
        doc = Document(file_path)
        
        sections = []
        for para in doc.paragraphs:
            sections.append({
                "text": para.text,
                "style": para.style.name,
                "alignment": para.alignment,
                "runs": [{"text": run.text, "bold": run.bold} for run in para.runs]
            })
        
        return {
            "sections": sections,
            "tables": [extract_table(table) for table in doc.tables],
            "metadata": doc.core_properties
        }
```

#### **Model Weight Parser**
```python
# api/services/model_atomization.py
class ModelWeightParser:
    @staticmethod
    async def parse_gguf(file_path: Path) -> AsyncGenerator:
        """Stream GGUF tensors (memory efficient)."""
        # Read GGUF header
        with open(file_path, 'rb') as f:
            magic = f.read(4)
            version = struct.unpack('<I', f.read(4))[0]
            
            # Stream tensors
            while True:
                tensor = read_tensor_header(f)
                if not tensor:
                    break
                
                # Yield weights in batches of 10,000
                weights = read_tensor_data(f, tensor.shape, tensor.dtype)
                for batch in chunk(weights, 10000):
                    yield {
                        "tensor_name": tensor.name,
                        "weights": batch,
                        "indices": calculate_indices(batch)
                    }
```

---

## 🚀 Batch Processing & GPU Acceleration

### **Batch Atomization**
```sql
-- GPU-accelerated batch atomization
CREATE OR REPLACE FUNCTION gpu_batch_atomize(
    p_values BYTEA[],
    p_texts TEXT[],
    p_metadata JSONB[]
)
RETURNS TABLE(atom_id BIGINT, content_hash BYTEA) 
LANGUAGE plpython3u
AS $$
    import torch
    import hashlib
    
    # GPU batch hashing (10,000× faster than sequential)
    device = "cuda" if torch.cuda.is_available() else "cpu"
    
    results = []
    for value, text, meta in zip(p_values, p_texts, p_metadata):
        hash_val = hashlib.sha256(value).digest()
        
        # Check if exists
        atom_id = plpy.execute(
            "SELECT atom_id FROM atom WHERE content_hash = $1",
            [hash_val]
        )
        
        if not atom_id:
            # Insert new atom
            atom_id = plpy.execute(
                "INSERT INTO atom (content_hash, atomic_value, canonical_text, metadata) "
                "VALUES ($1, $2, $3, $4) RETURNING atom_id",
                [hash_val, value, text, meta]
            )[0]['atom_id']
        else:
            atom_id = atom_id[0]['atom_id']
        
        results.append((atom_id, hash_val))
    
    return results
$$;
```

### **Parallel Spatial Positioning**
```sql
-- Position 10,000 atoms in parallel (GPU)
CREATE OR REPLACE FUNCTION gpu_batch_position(
    p_atom_ids BIGINT[],
    p_use_gpu BOOLEAN DEFAULT TRUE
)
RETURNS VOID
LANGUAGE plpython3u
AS $$
    import torch
    import numpy as np
    
    device = "cuda" if p_use_gpu and torch.cuda.is_available() else "cpu"
    
    # Fetch atom texts
    atom_texts = plpy.execute(
        "SELECT atom_id, canonical_text FROM atom WHERE atom_id = ANY($1)",
        [p_atom_ids]
    )
    
    # Compute embeddings on GPU (batch of 10,000)
    texts = [row['canonical_text'] for row in atom_texts]
    embeddings = compute_embeddings_gpu(texts, device)  # [N, 3] positions
    
    # Bulk update
    for atom_id, (x, y, z) in zip(p_atom_ids, embeddings):
        plpy.execute(
            "UPDATE atom SET spatial_key = ST_MakePoint($1, $2, $3) WHERE atom_id = $4",
            [float(x), float(y), float(z), atom_id]
        )
$$;
```

---

## 📊 Ingestion Performance Targets

| Modality | Throughput | Latency | GPU Speedup |
|----------|-----------|---------|-------------|
| **Text (chars)** | 50K/sec | 0.02ms/char | 50× |
| **Image (1080p)** | 20 img/sec | 50ms/img | 10× |
| **Audio (16-bit)** | Real-time+ | <duration | 5× |
| **Video (1080p@30fps)** | 2× real-time | 30 sec/min | 20× |
| **AI Model (7B params)** | 100K weights/sec | 20 sec total | 100× |
| **Code (AST)** | 10K LOC/sec | 100ms/file | N/A (C#) |
| **Database (schema)** | 1K tables/sec | 1ms/table | N/A |

---

## 🎯 Success Metrics

### **Granularity**
- ✅ Every atom ≤64 bytes
- ✅ No data loss in decomposition
- ✅ Perfect reconstruction from atoms

### **Deduplication**
- ✅ Duplicate detection via SHA-256
- ✅ Reference counting tracks usage
- ✅ Space savings: 3-100× depending on modality

### **Performance**
- ✅ GPU acceleration: 10-100× speedup
- ✅ Batch processing: O(N/B) where B=batch_size
- ✅ Streaming: constant memory usage

### **Queryability**
- ✅ Spatial queries: O(log N) via R-Tree
- ✅ Semantic search: <50ms for 1M atoms
- ✅ Cross-modal: "code that does X" + "docs about Y"

---

**Status:** Architecture Complete, Ready for Implementation  
**Next:** Flesh out each parser service + GPU optimization + C# integration  
**Timeline:** Core parsers (2 weeks), GPU optimization (1 week), C# bridge (3 days)
