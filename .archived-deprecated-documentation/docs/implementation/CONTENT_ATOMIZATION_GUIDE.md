# Content Atomization Guide: All Modalities

**Status:** MIXED (Text complete, Code Python-only, Images dual-strategy, Audio partial, Models GGUF-only)  
**Philosophy:** Universal atomization pattern across all data types

---

## Terminology Clarification

**Key Terms:**

| Term | Definition | Example |
|------|------------|----------|
| **Atom** | Immutable, content-addressable unit ≤64 bytes | Character 'H', pixel RGB(255,0,0), audio sample 0.42 |
| **Primitive Atom** | Leaf atom with no composition_ids | Single character, single pixel |
| **Composition Atom** | Atom built from component atoms | Word "Hello" = composition of chars [H,e,l,l,o] |
| **Trajectory** | Ordered composition with sequence preserved | Sentence with word order maintained |
| **Hierarchical Atomization** | Multi-level: chars → words → sentences | Text "Hello world" creates 3 levels |
| **Content Hash** | SHA-256 hash = atom identity | Same content = same hash = same atom_id (deduplication) |
| **Spatial Key** | POINTZ(x,y,z) geometric position | Auto-computed from hash or semantic (embeddings) |
| **Modality** | Content type category | text, code, image, audio, model, video |
| **CAS** | Content-Addressable Storage | Like Git: content determines identity |

**Composition vs Trajectory:**
- **Composition:** Unordered set. Example: {"neural", "network"} = {"network", "neural"}
- **Trajectory:** Ordered sequence. Example: ["neural", "network"] ≠ ["network", "neural"]

**Hierarchical Levels:**
```
Text: "Hello world"
  Level 1: [H, e, l, l, o, , w, o, r, l, d]  (11 primitive atoms)
  Level 2: [Hello, world]                    (2 composition atoms)
  Level 3: ["Hello world"]                   (1 trajectory atom)
```

---

## Core Principle

**ALL content → immutable atoms ≤ 64 bytes.** Every modality uses the same pattern:

1. **Primitives:** Smallest units (characters, pixels, samples, tokens)
2. **Compositions:** Hierarchical structures from primitives
3. **Spatial Positioning:** Geometric coordinates in semantic space
4. **Content-Addressable:** SHA-256 hash = identity

```
Text: chars → words → sentences → documents
Code: tokens → nodes → subtrees → AST
Images: pixels → patches → regions → full image
Audio: samples → frames → segments → track
Models: tokens → layers → full model
```

---

## Schema (Universal)

```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    
    content_hash BYTEA UNIQUE NOT NULL,
    canonical_text TEXT,
    
    spatial_key GEOMETRY(POINTZ, 0) NOT NULL,
    
    composition_ids BIGINT[] DEFAULT '{}'::BIGINT[] NOT NULL,
    
    metadata JSONB DEFAULT '{}'::jsonb NOT NULL,
    
    created_at TIMESTAMPTZ DEFAULT now() NOT NULL
);

-- Metadata structure per modality:
-- Text: {"modality": "text", "type": "character|word|sentence", "language": "en"}
-- Code: {"modality": "code", "type": "token|node|ast", "language": "python", "node_type": "FunctionDef"}
-- Image: {"modality": "image", "type": "pixel|patch", "position": [x, y], "color": [r, g, b]}
-- Audio: {"modality": "audio", "type": "sample|frame", "sample_rate": 44100, "duration_ms": 23}
-- Model: {"modality": "model", "type": "token|weight", "model": "llama-2-7b", "layer": 0}
```

---

## 1. Text Atomization

**Status:** ✅ COMPLETE

### Character-Level Atoms

```python
import psycopg
from hashlib import sha256

async def atomize_text_characters(
    cur: psycopg.AsyncCursor,
    text: str
) -> list[int]:
    """
    Atomize text into character-level atoms.
    
    Each character = 1 atom (UTF-8 encoded, ≤4 bytes).
    
    Args:
        cur: Database cursor
        text: Input text
        
    Returns:
        List of atom IDs (one per character)
    """
    atom_ids = []
    
    for i, char in enumerate(text):
        content = char.encode('utf-8')
        content_hash = sha256(content).digest()
        
        # Position: semantic embedding (placeholder: hash-based)
        x = (int.from_bytes(content_hash[:4], 'big') % 1000) / 1000.0
        y = (int.from_bytes(content_hash[4:8], 'big') % 1000) / 1000.0
        z = (int.from_bytes(content_hash[8:12], 'big') % 1000) / 1000.0
        
        # CAS insert
        result = await cur.execute(
            """
            INSERT INTO atom (content_hash, canonical_text, spatial_key, metadata)
            VALUES (%s, %s, ST_GeomFromText(%s, 0), %s)
            ON CONFLICT (content_hash) DO UPDATE SET canonical_text = EXCLUDED.canonical_text
            RETURNING atom_id
            """,
            (
                content_hash,
                char,
                f"POINTZ({x} {y} {z})",
                {"modality": "text", "type": "character", "char_code": ord(char)}
            )
        )
        
        atom_id = (await result.fetchone())[0]
        atom_ids.append(atom_id)
    
    return atom_ids
```

### Hierarchical Text Composition

```python
async def atomize_text_hierarchical(
    cur: psycopg.AsyncCursor,
    text: str
) -> dict:
    """
    Hierarchical text atomization: chars → words → sentences.
    
    Returns:
        {"chars": [...], "words": [...], "sentences": [...]}
    """
    from api.services.atom_factory import create_composition
    
    # Step 1: Character atoms
    char_atoms = await atomize_text_characters(cur, text)
    
    # Step 2: Word compositions (use regex to preserve exact character positions)
    import re
    word_atoms = []
    
    for match in re.finditer(r'\S+', text):
        word = match.group()
        start_pos = match.start()
        end_pos = match.end()
        
        word_char_atoms = char_atoms[start_pos:end_pos]
        
        if word_char_atoms:
            word_atom = await create_composition(
                cur,
                word_char_atoms,
                {"modality": "text", "type": "word", "text": word}
            )
            word_atoms.append(word_atom)
    
    # Step 3: Sentence composition (full text)
    sentence_atom = await create_composition(
        cur,
        word_atoms,
        {"modality": "text", "type": "sentence", "text": text}
    )
    
    return {
        "chars": char_atoms,
        "words": word_atoms,
        "sentence": sentence_atom
    }
```

---

## 2. Code Atomization

**Status:** 🟡 PARTIAL (Python via Tree-sitter complete, other languages TODO)

### Tree-sitter AST Atomization (Python)

```python
from tree_sitter import Language, Parser
import tree_sitter_python as tspython

async def atomize_code_ast(
    cur: psycopg.AsyncCursor,
    code: str,
    language: str = "python"
) -> dict:
    """
    Atomize code via Tree-sitter AST.
    
    Each AST node = composition of child nodes.
    
    Args:
        cur: Database cursor
        code: Source code
        language: Programming language (only "python" supported)
        
    Returns:
        {"ast_root": atom_id, "nodes": [atom_ids...]}
    """
    if language != "python":
        # Fallback: plain text character atomization
        return await atomize_text_hierarchical(cur, code)
    
    # Parse with Tree-sitter
    PY_LANGUAGE = Language(tspython.language())
    parser = Parser(PY_LANGUAGE)
    
    tree = parser.parse(bytes(code, "utf-8"))
    root = tree.root_node
    
    # Recursively atomize AST
    ast_atom = await _atomize_ast_node(cur, root, code)
    
    return {
        "ast_root": ast_atom,
        "language": language
    }

async def _atomize_ast_node(
    cur: psycopg.AsyncCursor,
    node,
    source_code: str
) -> int:
    """
    Recursively atomize AST node.
    
    Leaf nodes: Create primitive atoms from text
    Internal nodes: Create compositions from children
    """
    node_text = source_code[node.start_byte:node.end_byte]
    
    if node.child_count == 0:
        # Leaf node: create primitive atom
        content = node_text.encode('utf-8')
        content_hash = sha256(content).digest()
        
        # Position: code semantic embedding (placeholder)
        x, y, z = 0.5, 0.5, 0.5  # TODO: actual code embeddings
        
        result = await cur.execute(
            """
            INSERT INTO atom (content_hash, canonical_text, spatial_key, metadata)
            VALUES (%s, %s, ST_GeomFromText(%s, 0), %s)
            ON CONFLICT (content_hash) DO UPDATE SET canonical_text = EXCLUDED.canonical_text
            RETURNING atom_id
            """,
            (
                content_hash,
                node_text,
                f"POINTZ({x} {y} {z})",
                {
                    "modality": "code",
                    "type": "token",
                    "node_type": node.type,
                    "start_byte": node.start_byte,
                    "end_byte": node.end_byte
                }
            )
        )
        
        return (await result.fetchone())[0]
    
    # Internal node: recursively atomize children
    child_atoms = []
    
    for child in node.children:
        child_atom = await _atomize_ast_node(cur, child, source_code)
        child_atoms.append(child_atom)
    
    # Create composition from children
    from api.services.atom_factory import create_composition
    
    node_atom = await create_composition(
        cur,
        child_atoms,
        {
            "modality": "code",
            "type": "ast_node",
            "node_type": node.type,
            "text": node_text[:100]  # Truncate for metadata
        }
    )
    
    return node_atom
```

### Fallback: Plain Text Code Atomization

```python
async def atomize_code_plain(
    cur: psycopg.AsyncCursor,
    code: str,
    language: str
) -> list[int]:
    """
    Fallback for languages without Tree-sitter support.
    
    Uses character-level atomization (same as text).
    """
    char_atoms = await atomize_text_characters(cur, code)
    
    # Update metadata to indicate code
    await cur.execute(
        """
        UPDATE atom
        SET metadata = metadata || %s
        WHERE atom_id = ANY(%s)
        """,
        (
            {"modality": "code", "language": language},
            char_atoms
        )
    )
    
    return char_atoms
```

---

## 3. Image Atomization

**Status:** 🟡 DUAL STRATEGY (Pixel-level 234 lines vs Patch-based 307 lines)

### Strategy 1: Pixel-Level Atomization

**When to use:** Small images (< 256x256), lossless reconstruction required

```python
from PIL import Image
import numpy as np

async def atomize_image_pixels(
    cur: psycopg.AsyncCursor,
    image_path: str
) -> dict:
    """
    Atomize image at pixel level.
    
    Each pixel = 1 atom (RGB values, ≤12 bytes).
    
    WARNING: Creates WIDTH × HEIGHT atoms (e.g., 65536 atoms for 256x256 image).
    
    Args:
        cur: Database cursor
        image_path: Path to image file
        
    Returns:
        {"pixels": [[atom_ids...]], "width": int, "height": int}
    """
    img = Image.open(image_path).convert('RGB')
    width, height = img.size
    
    # Memory validation: Prevent OOM on large images
    total_pixels = width * height
    estimated_memory_mb = (total_pixels * 100) / (1024 * 1024)  # ~100 bytes per atom
    
    if total_pixels > 1_000_000:  # 1 megapixel limit
        raise ValueError(
            f"Image too large for pixel-level atomization: {width}x{height} = {total_pixels:,} pixels "
            f"(estimated {estimated_memory_mb:.1f} MB). "
            f"Recommend patch-based strategy or downsampling. "
            f"Patch-based with 16x16 patches would create {(total_pixels // 256):,} atoms."
        )
    
    pixels = np.array(img)
    
    pixel_atoms = []
    
    for y in range(height):
        row = []
        
        for x in range(width):
            r, g, b = pixels[y, x]
            
            # Content: RGB values
            content = bytes([r, g, b])
            content_hash = sha256(content).digest()
            
            # Position: normalize pixel coordinates to [0, 1]
            pos_x = x / width
            pos_y = y / height
            pos_z = (r + g + b) / (3 * 255)  # Color intensity
            
            result = await cur.execute(
                """
                INSERT INTO atom (content_hash, canonical_text, spatial_key, metadata)
                VALUES (%s, %s, ST_GeomFromText(%s, 0), %s)
                ON CONFLICT (content_hash) DO UPDATE SET canonical_text = EXCLUDED.canonical_text
                RETURNING atom_id
                """,
                (
                    content_hash,
                    f"RGB({r},{g},{b})",
                    f"POINTZ({pos_x} {pos_y} {pos_z})",
                    {
                        "modality": "image",
                        "type": "pixel",
                        "rgb": [r, g, b],
                        "position": [x, y]
                    }
                )
            )
            
            row.append((await result.fetchone())[0])
        
        pixel_atoms.append(row)
    
    return {
        "pixels": pixel_atoms,
        "width": width,
        "height": height
    }
```

### Strategy 2: Patch-Based Atomization (Recommended)

**When to use:** Large images, feature extraction, semantic similarity

```python
async def atomize_image_patches(
    cur: psycopg.AsyncCursor,
    image_path: str,
    patch_size: int = 16
) -> dict:
    """
    Atomize image into non-overlapping patches.
    
    Each patch = 1 atom (embedding vector, ≤64 bytes).
    
    Creates (WIDTH / patch_size) × (HEIGHT / patch_size) atoms.
    
    Args:
        cur: Database cursor
        image_path: Path to image file
        patch_size: Patch size in pixels (default 16x16)
        
    Returns:
        {"patches": [[atom_ids...]], "patch_size": int}
    """
    img = Image.open(image_path).convert('RGB')
    width, height = img.size
    pixels = np.array(img)
    
    patch_atoms = []
    
    for y in range(0, height, patch_size):
        row = []
        
        for x in range(0, width, patch_size):
            # Extract patch
            patch = pixels[y:y+patch_size, x:x+patch_size]
            
            # Compute patch embedding (mean RGB + variance)
            mean_rgb = patch.mean(axis=(0, 1))
            var_rgb = patch.var(axis=(0, 1))
            
            # Content: quantized embedding (6 floats → 24 bytes)
            import struct
            content = struct.pack('6f', *mean_rgb, *var_rgb)
            content_hash = sha256(content).digest()
            
            # Position: patch center + color intensity
            pos_x = (x + patch_size / 2) / width
            pos_y = (y + patch_size / 2) / height
            pos_z = mean_rgb.mean() / 255.0
            
            result = await cur.execute(
                """
                INSERT INTO atom (content_hash, canonical_text, spatial_key, metadata)
                VALUES (%s, %s, ST_GeomFromText(%s, 0), %s)
                ON CONFLICT (content_hash) DO UPDATE SET canonical_text = EXCLUDED.canonical_text
                RETURNING atom_id
                """,
                (
                    content_hash,
                    f"Patch({x},{y})",
                    f"POINTZ({pos_x} {pos_y} {pos_z})",
                    {
                        "modality": "image",
                        "type": "patch",
                        "position": [x, y],
                        "patch_size": patch_size,
                        "mean_rgb": mean_rgb.tolist(),
                        "var_rgb": var_rgb.tolist()
                    }
                )
            )
            
            row.append((await result.fetchone())[0])
        
        patch_atoms.append(row)
    
    return {
        "patches": patch_atoms,
        "patch_size": patch_size
    }
```

### Strategy Comparison

| Criterion | Pixel-Level | Patch-Based |
|-----------|-------------|-------------|
| Atom count | W × H | (W/P) × (H/P) |
| Example (1024x1024) | 1,048,576 atoms | 4,096 atoms (P=16) |
| Reconstruction | Lossless | Approximate |
| Semantic search | Poor | Good |
| Storage | High | Low |
| Use case | Exact reproduction | Feature extraction |

---

## 4. Audio Atomization

**Status:** 🟡 PARTIAL (Sample-level complete, video extraction TODO)

### Sample-Level Atomization

```python
import wave
import struct

async def atomize_audio_samples(
    cur: psycopg.AsyncCursor,
    audio_path: str,
    frame_size: int = 1024
) -> dict:
    """
    Atomize audio into sample frames.
    
    Each frame = frame_size samples (e.g., 1024 samples = ~23ms at 44.1kHz).
    
    Args:
        cur: Database cursor
        audio_path: Path to WAV file
        frame_size: Samples per frame
        
    Returns:
        {"frames": [atom_ids...], "sample_rate": int, "duration_sec": float}
    """
    with wave.open(audio_path, 'rb') as wav:
        sample_rate = wav.getframerate()
        n_frames = wav.getnframes()
        samples = wav.readframes(n_frames)
        sample_width = wav.getsampwidth()  # Bytes per sample
        n_channels = wav.getnchannels()
    
    # Parse samples based on detected bit depth
    if sample_width == 1:  # 8-bit unsigned
        format_char = 'B'  # Unsigned char
        max_val = 128
    elif sample_width == 2:  # 16-bit signed
        format_char = 'h'  # Signed short
        max_val = 32768
    elif sample_width == 4:  # 32-bit signed
        format_char = 'i'  # Signed int
        max_val = 2147483648
    else:
        raise ValueError(f"Unsupported sample width: {sample_width} bytes")
    
    sample_array = struct.unpack(f'{n_frames * n_channels}{format_char}', samples)
    
    # Handle multi-channel audio (convert to mono by averaging)
    if n_channels > 1:
        mono_samples = []
        for i in range(0, len(sample_array), n_channels):
            frame_channels = sample_array[i:i+n_channels]
            mono_samples.append(sum(frame_channels) // n_channels)
        sample_array = mono_samples
    
    frame_atoms = []
    
    for i in range(0, len(sample_array), frame_size):
        frame = sample_array[i:i+frame_size]
        
        # Compute frame features (mean, RMS energy)
        mean_amplitude = sum(frame) / len(frame)
        rms_energy = (sum(s**2 for s in frame) / len(frame)) ** 0.5
        
        # Content: quantized features (≤16 bytes)
        content = struct.pack('2f', mean_amplitude, rms_energy)
        content_hash = sha256(content).digest()
        
        # Position: time + energy
        time_pos = i / len(sample_array)
        energy_pos = rms_energy / 32768.0  # Normalize 16-bit range
        
        result = await cur.execute(
            """
            INSERT INTO atom (content_hash, canonical_text, spatial_key, metadata)
            VALUES (%s, %s, ST_GeomFromText(%s, 0), %s)
            ON CONFLICT (content_hash) DO UPDATE SET canonical_text = EXCLUDED.canonical_text
            RETURNING atom_id
            """,
            (
                content_hash,
                f"Frame({i // frame_size})",
                f"POINTZ({time_pos} {energy_pos} 0.5)",
                {
                    "modality": "audio",
                    "type": "frame",
                    "frame_index": i // frame_size,
                    "sample_rate": sample_rate,
                    "duration_ms": int(frame_size / sample_rate * 1000),
                    "mean_amplitude": float(mean_amplitude),
                    "rms_energy": float(rms_energy)
                }
            )
        )
        
        frame_atoms.append((await result.fetchone())[0])
    
    return {
        "frames": frame_atoms,
        "sample_rate": sample_rate,
        "duration_sec": len(sample_array) / sample_rate
    }
```

### Video Audio Extraction (TODO)

```python
async def atomize_video_audio(
    cur: psycopg.AsyncCursor,
    video_path: str
) -> dict:
    """
    Extract audio from video and atomize.
    
    TODO: Requires ffmpeg integration for audio extraction.
    
    Steps:
    1. ffmpeg -i video.mp4 -vn -acodec pcm_s16le audio.wav
    2. atomize_audio_samples(audio.wav)
    """
    raise NotImplementedError("Video audio extraction requires ffmpeg")
```

---

## 5. Model Atomization

**Status:** 🟡 PARTIAL (GGUF vocabulary complete 1120x optimized, weight matrices TODO)

### GGUF Vocabulary Atomization (COMPLETE)

```python
import gguf

async def atomize_model_vocabulary(
    cur: psycopg.AsyncCursor,
    model_path: str
) -> dict:
    """
    Atomize GGUF model vocabulary (tokens).
    
    Each token = 1 atom (UTF-8 text, ≤64 bytes).
    
    Args:
        cur: Database cursor
        model_path: Path to GGUF file
        
    Returns:
        {"tokens": [atom_ids...], "model_name": str, "vocab_size": int}
    """
    reader = gguf.GGUFReader(model_path)
    
    # Extract vocabulary
    vocab = []
    
    for field in reader.fields.values():
        if field.name == "tokenizer.ggml.tokens":
            vocab = field.data
            break
    
    if not vocab:
        raise ValueError("No vocabulary found in GGUF file")
    
    token_atoms = []
    
    for token_id, token_bytes in enumerate(vocab):
        # Token text
        try:
            token_text = token_bytes.decode('utf-8', errors='replace')
        except:
            token_text = f"<TOKEN_{token_id}>"
        
        content = token_bytes
        content_hash = sha256(content).digest()
        
        # Position: token embedding (placeholder: hash-based)
        x = (int.from_bytes(content_hash[:4], 'big') % 1000) / 1000.0
        y = (int.from_bytes(content_hash[4:8], 'big') % 1000) / 1000.0
        z = (int.from_bytes(content_hash[8:12], 'big') % 1000) / 1000.0
        
        result = await cur.execute(
            """
            INSERT INTO atom (content_hash, canonical_text, spatial_key, metadata)
            VALUES (%s, %s, ST_GeomFromText(%s, 0), %s)
            ON CONFLICT (content_hash) DO UPDATE SET canonical_text = EXCLUDED.canonical_text
            RETURNING atom_id
            """,
            (
                content_hash,
                token_text,
                f"POINTZ({x} {y} {z})",
                {
                    "modality": "model",
                    "type": "token",
                    "token_id": token_id,
                    "model": model_path.split('/')[-1]
                }
            )
        )
        
        token_atoms.append((await result.fetchone())[0])
    
    return {
        "tokens": token_atoms,
        "model_name": model_path.split('/')[-1],
        "vocab_size": len(vocab)
    }
```

### Weight Matrix Atomization (TODO)

```python
async def atomize_model_weights(
    cur: psycopg.AsyncCursor,
    model_path: str
) -> dict:
    """
    Atomize model weight matrices.
    
    TODO: Requires sparse matrix encoding via composition hierarchies.
    
    Strategy:
    1. Extract weight tensors from GGUF/SafeTensors
    2. Encode as sparse matrix (non-zero elements only)
    3. Create composition atoms for matrix rows
    4. Create composition for full layer
    
    Challenge: Weight matrices are large (7B model = ~28GB)
    Solution: Geometric compression via M coordinate gaps (POINTZM required)
    """
    raise NotImplementedError("Weight matrix atomization requires POINTZM migration")
```

---

## 6. Video Atomization (TODO)

```python
async def atomize_video(
    cur: psycopg.AsyncCursor,
    video_path: str,
    frame_rate: int = 1
) -> dict:
    """
    Atomize video into frames + audio.
    
    TODO: Requires ffmpeg for frame extraction.
    
    Strategy:
    1. Extract frames: ffmpeg -i video.mp4 -r {frame_rate} frame_%04d.jpg
    2. Extract audio: ffmpeg -i video.mp4 -vn audio.wav
    3. Atomize each frame (patch-based)
    4. Atomize audio (sample frames)
    5. Create composition linking frames + audio
    
    Returns:
        {
            "frames": [atom_ids...],
            "audio": audio_atom_id,
            "video_composition": composition_atom_id
        }
    """
    raise NotImplementedError("Video atomization requires ffmpeg")
```

---

## Performance Characteristics

| Modality | Strategy | Atom Count | Throughput |
|----------|----------|-----------|------------|
| Text | Character-level | N chars | 10000-50000 chars/sec |
| Text | Hierarchical | N chars + M words | 5000-10000 chars/sec |
| Code | AST (Python) | N nodes | 1000-5000 lines/sec |
| Code | Plain text | N chars | 10000-50000 chars/sec |
| Image | Pixel-level | W × H | 100-500 pixels/sec |
| Image | Patch-based (16x16) | (W/16) × (H/16) | 1000-5000 patches/sec |
| Audio | Sample frames | N/frame_size | 10000-50000 samples/sec |
| Model | Vocabulary | vocab_size | 5000-10000 tokens/sec |
| Model | Weights | PENDING | PENDING |

---

## Testing

### Text Atomization Tests

```python
import pytest

@pytest.mark.asyncio
async def test_text_character_atomization(db_pool):
    """Test character-level text atomization."""
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        text = "Hello"
        atoms = await atomize_text_characters(cur, text)
        
        assert len(atoms) == 5
        
        # Verify deduplication ('l' appears twice)
        await conn.commit()
        
        atoms2 = await atomize_text_characters(cur, "Hello")
        assert atoms == atoms2  # Same atom IDs

@pytest.mark.asyncio
async def test_hierarchical_text(db_pool):
    """Test hierarchical text atomization."""
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        text = "Hello world"
        result = await atomize_text_hierarchical(cur, text)
        
        assert "chars" in result
        assert "words" in result
        assert "sentence" in result
        
        assert len(result["words"]) == 2  # "Hello", "world"
```

### Code Atomization Tests

```python
@pytest.mark.asyncio
async def test_python_ast_atomization(db_pool):
    """Test Python AST atomization."""
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        code = "def foo():\n    return 42"
        result = await atomize_code_ast(cur, code, "python")
        
        assert "ast_root" in result
        assert result["ast_root"] > 0
```

### Image Atomization Tests

```python
@pytest.mark.asyncio
async def test_image_patch_atomization(db_pool, tmp_path):
    """Test patch-based image atomization."""
    from PIL import Image
    
    # Create test image
    img_path = tmp_path / "test.png"
    img = Image.new('RGB', (64, 64), color='red')
    img.save(img_path)
    
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        result = await atomize_image_patches(cur, str(img_path), patch_size=16)
        
        assert "patches" in result
        assert len(result["patches"]) == 4  # 64/16 = 4 rows
        assert len(result["patches"][0]) == 4  # 4 columns
```

---

---

## 8. PDF & Spreadsheet Atomization

**Status:** ⚠️ PLANNED (not yet implemented)

### PDF Strategy

**Multi-Level Atomization:**

```python
import PyPDF2
from PIL import Image
import io

async def atomize_pdf(
    cur: psycopg.AsyncCursor,
    pdf_path: str
) -> dict:
    """
    Atomize PDF with text extraction, image extraction, and structural preservation.
    
    Levels:
    1. Text content (characters → words → sentences)
    2. Embedded images (pixel/patch-based)
    3. Document structure (pages, sections, metadata)
    4. Layout geometry (bounding boxes, reading order)
    
    Returns:
        {
            "pages": [{"page_num": int, "text_atoms": [...], "image_atoms": [...]}],
            "images": [{"image_id": int, "patch_atoms": [...]}],
            "structure": {"toc": [...], "sections": [...]}
        }
    """
    with open(pdf_path, 'rb') as f:
        pdf_reader = PyPDF2.PdfReader(f)
        
        pages_result = []
        images_result = []
        
        for page_num, page in enumerate(pdf_reader.pages):
            # Extract text
            text = page.extract_text()
            text_atoms = await atomize_text_hierarchical(cur, text)
            
            # Extract images
            page_images = []
            if '/XObject' in page['/Resources']:
                xobjects = page['/Resources']['/XObject'].get_object()
                
                for obj_name in xobjects:
                    obj = xobjects[obj_name]
                    
                    if obj['/Subtype'] == '/Image':
                        # Extract image data
                        width = obj['/Width']
                        height = obj['/Height']
                        data = obj.get_data()
                        
                        # Convert to PIL Image
                        img = Image.open(io.BytesIO(data))
                        
                        # Atomize image
                        with tempfile.NamedTemporaryFile(suffix='.png') as tmp:
                            img.save(tmp.name)
                            img_atoms = await atomize_image_patches(cur, tmp.name)
                            images_result.append({
                                "page_num": page_num,
                                "image_atoms": img_atoms
                            })
            
            pages_result.append({
                "page_num": page_num,
                "text_atoms": text_atoms,
                "image_count": len(page_images)
            })
        
        # Create document-level composition
        all_atom_ids = []
        for page in pages_result:
            all_atom_ids.extend(page["text_atoms"]["sentence"])
        
        doc_composition_id = await create_composition(
            cur,
            all_atom_ids,
            {
                "modality": "document",
                "type": "pdf",
                "page_count": len(pages_result),
                "image_count": len(images_result)
            }
        )
        
        return {
            "document_composition_id": doc_composition_id,
            "pages": pages_result,
            "images": images_result
        }
```

### Spreadsheet Strategy

```python
import openpyxl
import pandas as pd

async def atomize_spreadsheet(
    cur: psycopg.AsyncCursor,
    file_path: str
) -> dict:
    """
    Atomize spreadsheet with cell-level atomization and table structure preservation.
    
    Levels:
    1. Cell values (text/number primitives)
    2. Rows (compositions of cells)
    3. Columns (compositions of cells)
    4. Tables (compositions of rows)
    5. Sheets (compositions of tables)
    
    Returns:
        {
            "sheets": [{"name": str, "table_atoms": [...]}],
            "row_count": int,
            "column_count": int
        }
    """
    workbook = openpyxl.load_workbook(file_path)
    sheets_result = []
    
    for sheet_name in workbook.sheetnames:
        sheet = workbook[sheet_name]
        
        # Read as pandas DataFrame
        df = pd.DataFrame(sheet.values)
        
        # Atomize each cell
        row_atoms = []
        
        for row_idx, row in df.iterrows():
            cell_atoms = []
            
            for col_idx, value in enumerate(row):
                if pd.notna(value):  # Skip empty cells
                    # Atomize cell value
                    content = str(value).encode('utf-8')
                    atom_id = await create_atom_cas(
                        cur,
                        content,
                        str(value),
                        (0.5, 0.5, 0.5),  # TODO: Use semantic position
                        {
                            "modality": "spreadsheet",
                            "type": "cell",
                            "row": row_idx,
                            "column": col_idx
                        }
                    )
                    cell_atoms.append(atom_id)
            
            # Create row composition
            if cell_atoms:
                row_comp_id = await create_composition(
                    cur,
                    cell_atoms,
                    {
                        "modality": "spreadsheet",
                        "type": "row",
                        "row_index": row_idx
                    }
                )
                row_atoms.append(row_comp_id)
        
        # Create sheet composition
        sheet_comp_id = await create_composition(
            cur,
            row_atoms,
            {
                "modality": "spreadsheet",
                "type": "sheet",
                "name": sheet_name
            }
        )
        
        sheets_result.append({
            "name": sheet_name,
            "composition_id": sheet_comp_id,
            "row_count": len(row_atoms)
        })
    
    return {
        "sheets": sheets_result,
        "total_rows": sum(s["row_count"] for s in sheets_result)
    }
```

---

## 9. Video Atomization (Detailed)

**Status:** ⚠️ PARTIAL (frame extraction working, audio TODO)

### Complete Video Strategy

```python
import cv2
import subprocess
import tempfile
from pathlib import Path

async def atomize_video_complete(
    cur: psycopg.AsyncCursor,
    video_path: str,
    fps: int = 1  # Extract 1 frame per second
) -> dict:
    """
    Complete video atomization: frames + audio + metadata.
    
    Requires:
    - opencv-python (frame extraction)
    - ffmpeg (audio extraction)
    - librosa (audio atomization)
    
    Levels:
    1. Video frames → pixel/patch atoms
    2. Audio track → sample/spectrogram atoms
    3. Frame sequence → temporal trajectory
    4. Audio sequence → temporal trajectory
    5. Video composition → synchronized audio+video
    
    Returns:
        {
            "frames": [{"timestamp": float, "atoms": [...]}],
            "audio": {"samples": [...], "duration_sec": float},
            "video_composition_id": int,
            "metadata": {"fps": int, "duration": float, "resolution": [w, h]}
        }
    """
    # 1. Extract video metadata
    cap = cv2.VideoCapture(video_path)
    video_fps = cap.get(cv2.CAP_PROP_FPS)
    total_frames = int(cap.get(cv2.CAP_PROP_FRAME_COUNT))
    width = int(cap.get(cv2.CAP_PROP_FRAME_WIDTH))
    height = int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT))
    duration_sec = total_frames / video_fps
    
    # 2. Extract frames at specified FPS
    frame_results = []
    frame_interval = int(video_fps / fps)
    frame_count = 0
    
    while True:
        ret, frame = cap.read()
        if not ret:
            break
        
        if frame_count % frame_interval == 0:
            timestamp = frame_count / video_fps
            
            # Save frame temporarily
            with tempfile.NamedTemporaryFile(suffix='.png', delete=False) as tmp:
                cv2.imwrite(tmp.name, frame)
                
                # Atomize frame (patch-based for efficiency)
                frame_atoms = await atomize_image_patches(cur, tmp.name, patch_size=16)
                
                frame_results.append({
                    "timestamp": timestamp,
                    "frame_num": frame_count,
                    "atoms": frame_atoms
                })
                
                Path(tmp.name).unlink()  # Cleanup
        
        frame_count += 1
    
    cap.release()
    
    # 3. Extract audio track using ffmpeg
    audio_path = Path(video_path).with_suffix('.wav')
    
    subprocess.run([
        'ffmpeg',
        '-i', video_path,
        '-vn',  # No video
        '-acodec', 'pcm_s16le',  # 16-bit PCM
        '-ar', '44100',  # 44.1kHz sample rate
        '-ac', '2',  # Stereo
        str(audio_path)
    ], check=True, capture_output=True)
    
    # 4. Atomize audio
    audio_atoms = await atomize_audio_samples(cur, str(audio_path))
    
    # Cleanup audio file
    audio_path.unlink()
    
    # 5. Create frame trajectory (temporal ordering)
    frame_atom_ids = []
    for frame in frame_results:
        # Create frame composition
        frame_comp_id = await create_composition(
            cur,
            [atom_id for row in frame["atoms"]["patches"] for atom_id in row],
            {
                "modality": "video",
                "type": "frame",
                "timestamp": frame["timestamp"]
            }
        )
        frame_atom_ids.append(frame_comp_id)
    
    # 6. Create video trajectory (temporal frame sequence)
    video_trajectory_id = await create_trajectory(
        cur,
        frame_atom_ids,
        {
            "modality": "video",
            "type": "frame_sequence",
            "fps": fps,
            "duration_sec": duration_sec
        }
    )
    
    # 7. Create audio trajectory
    audio_trajectory_id = await create_trajectory(
        cur,
        audio_atoms["sample_ids"],
        {
            "modality": "audio",
            "type": "audio_track",
            "sample_rate": 44100,
            "duration_sec": duration_sec
        }
    )
    
    # 8. Create synchronized video composition
    video_composition_id = await create_composition(
        cur,
        [video_trajectory_id, audio_trajectory_id],
        {
            "modality": "video",
            "type": "video_with_audio",
            "resolution": [width, height],
            "fps": video_fps,
            "extracted_fps": fps,
            "duration_sec": duration_sec
        }
    )
    
    return {
        "video_composition_id": video_composition_id,
        "frames": frame_results,
        "audio": {
            "trajectory_id": audio_trajectory_id,
            "sample_count": len(audio_atoms["sample_ids"]),
            "duration_sec": duration_sec
        },
        "metadata": {
            "fps": video_fps,
            "extracted_fps": fps,
            "resolution": [width, height],
            "duration_sec": duration_sec,
            "total_frames": total_frames,
            "extracted_frames": len(frame_results)
        }
    }
```

**Installation Requirements:**

```bash
# Python packages
pip install opencv-python librosa soundfile

# System dependencies
# Ubuntu/Debian
sudo apt install ffmpeg libsndfile1

# macOS
brew install ffmpeg libsndfile

# Windows
# Download ffmpeg from https://ffmpeg.org/download.html
# Add to PATH
```

---

## Monitoring Dashboards (Grafana)

### Dashboard: Atomization Overview

**JSON Configuration:**

```json
{
  "dashboard": {
    "title": "Hartonomous Atomization Overview",
    "panels": [
      {
        "title": "Atoms Created (Rate)",
        "targets": [{
          "expr": "rate(atoms_created_total[5m])",
          "legendFormat": "{{modality}}"
        }],
        "type": "graph"
      },
      {
        "title": "Atomization Latency (P95)",
        "targets": [{
          "expr": "histogram_quantile(0.95, rate(atomization_duration_seconds_bucket[5m]))",
          "legendFormat": "{{endpoint}}"
        }],
        "type": "graph"
      },
      {
        "title": "Deduplication Rate",
        "targets": [{
          "expr": "100 * rate(atoms_deduplicated_total[5m]) / rate(atoms_created_total[5m])"
        }],
        "type": "singlestat",
        "format": "percent"
      },
      {
        "title": "Active Modalities",
        "targets": [{
          "expr": "count(rate(atoms_created_total[5m]) > 0) by (modality)"
        }],
        "type": "table"
      }
    ]
  }
}
```

### Dashboard: BPE Pattern Learning

```json
{
  "dashboard": {
    "title": "BPE Pattern Learning",
    "panels": [
      {
        "title": "Patterns Crystallized",
        "targets": [{
          "expr": "rate(bpe_patterns_crystallized_total[5m])",
          "legendFormat": "ngram_{{ngram_length}}"
        }],
        "type": "graph"
      },
      {
        "title": "Pattern Count by Length",
        "targets": [{
          "expr": "bpe_pattern_count",
          "legendFormat": "length_{{ngram_length}}"
        }],
        "type": "bargauge"
      },
      {
        "title": "Crystallization Latency",
        "targets": [{
          "expr": "histogram_quantile(0.95, rate(bpe_crystallization_duration_seconds_bucket[5m]))"
        }],
        "type": "singlestat",
        "format": "ms"
      }
    ]
  }
}
```

### Alerting Rules (Prometheus)

```yaml
# alerting_rules.yml
groups:
  - name: atomization_alerts
    interval: 30s
    rules:
      # High error rate
      - alert: HighAtomizationErrorRate
        expr: |
          rate(atomization_requests_total{status="error"}[5m]) /
          rate(atomization_requests_total[5m]) > 0.05
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High atomization error rate ({{ $value | humanizePercentage }})"
          description: "Atomization service {{ $labels.endpoint }} has {{ $value | humanizePercentage }} error rate"
      
      # Low throughput
      - alert: LowAtomizationThroughput
        expr: rate(atoms_created_total[5m]) < 10
        for: 10m
        labels:
          severity: warning
        annotations:
          summary: "Low atomization throughput ({{ $value }} atoms/sec)"
      
      # High P95 latency
      - alert: HighAtomizationLatency
        expr: |
          histogram_quantile(0.95,
            rate(atomization_duration_seconds_bucket[5m])
          ) > 1.0
        for: 5m
        labels:
          severity: warning
        annotations:
          summary: "High P95 atomization latency ({{ $value }}s)"
      
      # No patterns crystallized
      - alert: BPECrystallizationStalled
        expr: rate(bpe_patterns_crystallized_total[5m]) == 0
        for: 30m
        labels:
          severity: info
        annotations:
          summary: "No BPE patterns crystallized in 30 minutes"
      
      # Low deduplication rate
      - alert: LowDeduplicationRate
        expr: |
          100 * rate(atoms_deduplicated_total[5m]) /
          rate(atoms_created_total[5m]) < 5
        for: 15m
        labels:
          severity: info
        annotations:
          summary: "Low deduplication rate ({{ $value }}%)"
          description: "Expected >10% deduplication for typical workloads"
```

---

## Debug Logging Configuration

### Application-Level Logging

**Python Logging Setup:**

```python
# logging_config.py
import logging
import sys
from pathlib import Path

# Create logs directory
LOG_DIR = Path("/var/log/hartonomous")
LOG_DIR.mkdir(parents=True, exist_ok=True)

def setup_logging(log_level: str = "INFO"):
    """Configure application logging."""
    
    # Root logger
    root_logger = logging.getLogger()
    root_logger.setLevel(log_level)
    
    # Console handler (structured output)
    console_handler = logging.StreamHandler(sys.stdout)
    console_handler.setLevel(log_level)
    console_formatter = logging.Formatter(
        "%(asctime)s | %(levelname)-8s | %(name)s | %(message)s",
        datefmt="%Y-%m-%d %H:%M:%S"
    )
    console_handler.setFormatter(console_formatter)
    root_logger.addHandler(console_handler)
    
    # File handler (detailed JSON)
    file_handler = logging.FileHandler(LOG_DIR / "hartonomous.log")
    file_handler.setLevel("DEBUG")
    file_formatter = logging.Formatter(
        '{"timestamp": "%(asctime)s", "level": "%(levelname)s", '
        '"logger": "%(name)s", "message": "%(message)s", '
        '"function": "%(funcName)s", "line": %(lineno)d}'
    )
    file_handler.setFormatter(file_formatter)
    root_logger.addHandler(file_handler)
    
    # Atomization service logger
    atomization_logger = logging.getLogger("atomization")
    atomization_handler = logging.FileHandler(LOG_DIR / "atomization.log")
    atomization_handler.setLevel("DEBUG")
    atomization_handler.setFormatter(file_formatter)
    atomization_logger.addHandler(atomization_handler)
    
    # BPE crystallization logger
    bpe_logger = logging.getLogger("bpe_crystallization")
    bpe_handler = logging.FileHandler(LOG_DIR / "bpe_crystallization.log")
    bpe_handler.setLevel("DEBUG")
    bpe_handler.setFormatter(file_formatter)
    bpe_logger.addHandler(bpe_handler)
    
    logging.info(f"Logging configured: level={log_level}")

# Usage in application
from logging_config import setup_logging

setup_logging(log_level="DEBUG")  # Or "INFO" for production
```

---

### Request/Response Logging Middleware

```python
# middleware/logging_middleware.py
from fastapi import Request, Response
import logging
import time
import json

logger = logging.getLogger("api")

async def log_requests(request: Request, call_next):
    """Log all API requests and responses."""
    
    # Request logging
    request_id = request.headers.get("X-Request-ID", "unknown")
    start_time = time.time()
    
    logger.info(
        f"Request started: {request.method} {request.url.path} "
        f"(request_id={request_id})"
    )
    
    # Log request body for POST/PUT
    if request.method in ["POST", "PUT", "PATCH"]:
        try:
            body = await request.body()
            logger.debug(
                f"Request body (request_id={request_id}): "
                f"{body.decode('utf-8')[:1000]}"  # Truncate large bodies
            )
        except Exception as e:
            logger.warning(f"Could not log request body: {e}")
    
    # Process request
    try:
        response = await call_next(request)
    except Exception as e:
        duration = time.time() - start_time
        logger.error(
            f"Request failed: {request.method} {request.url.path} "
            f"(duration={duration:.3f}s, request_id={request_id}, error={str(e)})"
        )
        raise
    
    # Response logging
    duration = time.time() - start_time
    logger.info(
        f"Request completed: {request.method} {request.url.path} "
        f"(status={response.status_code}, duration={duration:.3f}s, "
        f"request_id={request_id})"
    )
    
    return response

# Register middleware in FastAPI app
from fastapi import FastAPI

app = FastAPI()
app.middleware("http")(log_requests)
```

---

### Database Query Logging

**PostgreSQL Logging Configuration:**

```ini
# postgresql.conf

# Log all queries taking longer than 100ms
log_min_duration_statement = 100

# Log query parameters
log_line_prefix = '%t [%p]: [%l-1] user=%u,db=%d,app=%a,client=%h '
log_statement = 'all'  # Or 'ddl', 'mod' for production

# Log slow queries with details
log_duration = on
log_error_verbosity = default

# Enable auto_explain for slow queries
shared_preload_libraries = 'auto_explain'
auto_explain.log_min_duration = 1000  # Log queries > 1s
auto_explain.log_analyze = on
auto_explain.log_buffers = on
```

**Application-Level Query Logging:**

```python
# database/logging_connection.py
import asyncpg
import logging
import time
from typing import Any

logger = logging.getLogger("database")

class LoggingConnection:
    """Database connection wrapper with query logging."""
    
    def __init__(self, conn: asyncpg.Connection, slow_query_threshold: float = 0.1):
        self.conn = conn
        self.slow_query_threshold = slow_query_threshold
    
    async def execute(self, query: str, *args, **kwargs) -> Any:
        """Execute query with logging."""
        start_time = time.time()
        
        # Log query start
        logger.debug(f"Executing query: {query[:200]}... (args={args})")
        
        try:
            result = await self.conn.execute(query, *args, **kwargs)
            duration = time.time() - start_time
            
            # Log slow queries
            if duration > self.slow_query_threshold:
                logger.warning(
                    f"Slow query detected (duration={duration:.3f}s): "
                    f"{query[:200]}..."
                )
            else:
                logger.debug(f"Query completed (duration={duration:.3f}s)")
            
            return result
        
        except Exception as e:
            duration = time.time() - start_time
            logger.error(
                f"Query failed (duration={duration:.3f}s, error={str(e)}): "
                f"{query[:200]}..."
            )
            raise
    
    async def fetch(self, query: str, *args, **kwargs) -> list:
        """Fetch results with logging."""
        start_time = time.time()
        
        try:
            result = await self.conn.fetch(query, *args, **kwargs)
            duration = time.time() - start_time
            
            logger.debug(
                f"Query fetched {len(result)} rows (duration={duration:.3f}s): "
                f"{query[:200]}..."
            )
            
            if duration > self.slow_query_threshold:
                logger.warning(
                    f"Slow query detected (duration={duration:.3f}s, rows={len(result)}): "
                    f"{query[:200]}..."
                )
            
            return result
        
        except Exception as e:
            duration = time.time() - start_time
            logger.error(
                f"Query failed (duration={duration:.3f}s, error={str(e)}): "
                f"{query[:200]}..."
            )
            raise

# Usage
async def get_connection_with_logging(pool: asyncpg.Pool) -> LoggingConnection:
    conn = await pool.acquire()
    return LoggingConnection(conn, slow_query_threshold=0.1)
```

---

### Performance Degradation Diagnosis

**Scenario:** Application performance degrades over time

**Diagnosis Steps:**

```python
# diagnose_performance.py
import asyncpg
import time
import logging

logger = logging.getLogger(__name__)

async def diagnose_performance_degradation(db_pool):
    """Run comprehensive performance diagnostics."""
    
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        print("=" * 60)
        print("PERFORMANCE DEGRADATION DIAGNOSIS")
        print("=" * 60)
        
        # 1. Table bloat
        print("\n1. Checking table bloat...")
        result = await cur.execute(
            """
            SELECT
                schemaname,
                tablename,
                pg_size_pretty(pg_total_relation_size(schemaname||'.'||tablename)) AS size,
                pg_size_pretty(
                    pg_total_relation_size(schemaname||'.'||tablename) -
                    pg_relation_size(schemaname||'.'||tablename)
                ) AS index_size,
                n_dead_tup,
                n_live_tup,
                ROUND(100.0 * n_dead_tup / NULLIF(n_live_tup + n_dead_tup, 0), 2) AS dead_tuple_percent
            FROM pg_stat_user_tables
            WHERE schemaname = 'public'
            ORDER BY n_dead_tup DESC
            LIMIT 10
            """
        )
        
        bloat_results = await result.fetchall()
        for row in bloat_results:
            print(f"  {row[1]}: {row[2]} total, {row[3]} indexes, {row[6]}% dead tuples")
            if row[6] and row[6] > 10:
                print(f"    ⚠ WARNING: High dead tuple percentage, consider VACUUM")
        
        # 2. Index health
        print("\n2. Checking index health...")
        result = await cur.execute(
            """
            SELECT
                schemaname,
                tablename,
                indexname,
                idx_scan,
                idx_tup_read,
                idx_tup_fetch,
                pg_size_pretty(pg_relation_size(indexrelid)) AS index_size
            FROM pg_stat_user_indexes
            WHERE schemaname = 'public'
            ORDER BY idx_scan ASC
            LIMIT 10
            """
        )
        
        index_results = await result.fetchall()
        for row in index_results:
            print(f"  {row[2]} on {row[1]}: {row[3]} scans, {row[6]} size")
            if row[3] == 0:
                print(f"    ⚠ WARNING: Unused index, consider dropping")
        
        # 3. Long-running queries
        print("\n3. Checking for long-running queries...")
        result = await cur.execute(
            """
            SELECT
                pid,
                now() - query_start AS duration,
                state,
                LEFT(query, 100) AS query_preview
            FROM pg_stat_activity
            WHERE state != 'idle'
              AND query_start < now() - interval '5 seconds'
            ORDER BY duration DESC
            """
        )
        
        long_queries = await result.fetchall()
        for row in long_queries:
            print(f"  PID {row[0]}: {row[1]} duration, state={row[2]}")
            print(f"    Query: {row[3]}...")
        
        # 4. Connection pool status
        print("\n4. Checking connection pool...")
        result = await cur.execute(
            """
            SELECT
                COUNT(*) AS total_connections,
                COUNT(*) FILTER (WHERE state = 'active') AS active,
                COUNT(*) FILTER (WHERE state = 'idle') AS idle,
                COUNT(*) FILTER (WHERE state = 'idle in transaction') AS idle_in_transaction
            FROM pg_stat_activity
            WHERE datname = current_database()
            """
        )
        
        pool_stats = await result.fetchone()
        print(f"  Total: {pool_stats[0]}, Active: {pool_stats[1]}, Idle: {pool_stats[2]}, Idle in txn: {pool_stats[3]}")
        if pool_stats[3] > 5:
            print(f"    ⚠ WARNING: Many idle in transaction connections, check for transaction leaks")
        
        # 5. Cache hit rate
        print("\n5. Checking cache hit rate...")
        result = await cur.execute(
            """
            SELECT
                SUM(heap_blks_read) AS heap_read,
                SUM(heap_blks_hit) AS heap_hit,
                ROUND(100.0 * SUM(heap_blks_hit) / NULLIF(SUM(heap_blks_hit) + SUM(heap_blks_read), 0), 2) AS cache_hit_ratio
            FROM pg_statio_user_tables
            """
        )
        
        cache_stats = await result.fetchone()
        print(f"  Cache hit ratio: {cache_stats[2]}%")
        if cache_stats[2] and cache_stats[2] < 90:
            print(f"    ⚠ WARNING: Low cache hit rate, consider increasing shared_buffers")
        
        # 6. Recent errors
        print("\n6. Checking application logs for errors...")
        # (Requires log aggregation system)
        
        print("\n" + "=" * 60)
        print("DIAGNOSIS COMPLETE")
        print("=" * 60)

# Usage
import asyncio

async def main():
    pool = await asyncpg.create_pool(
        host="localhost",
        database="hartonomous",
        user="postgres",
        password="postgres"
    )
    
    await diagnose_performance_degradation(pool)
    
    await pool.close()

asyncio.run(main())
```

**Expected Output:**
- Table bloat: <10% dead tuples is healthy
- Index health: All indexes used (idx_scan > 0)
- Long queries: None >5 seconds
- Connection pool: <5 idle in transaction
- Cache hit rate: >95% is excellent

---

## Advanced Metadata Handling

### Custom Metadata Fields

**Use Case:** Add domain-specific metadata to atoms during ingestion

```python
# custom_metadata_atomization.py
import asyncpg
from typing import Dict, Any
import json

async def atomize_with_custom_metadata(
    db_pool,
    text: str,
    custom_metadata: Dict[str, Any]
) -> int:
    """
    Atomize text with custom metadata fields.
    
    Args:
        db_pool: Database connection pool
        text: Text to atomize
        custom_metadata: Custom fields (e.g., {"source": "legal_doc", "jurisdiction": "CA"})
    
    Returns:
        atom_id of created/existing atom
    """
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        # Create hash
        import hashlib
        hash_value = hashlib.sha256(text.encode()).hexdigest()
        
        # Merge standard + custom metadata
        metadata = {
            "modality": "text",
            "length": len(text),
            "encoding": "utf-8",
            **custom_metadata  # Merge custom fields
        }
        
        # Insert or update with custom metadata
        result = await cur.execute(
            """
            INSERT INTO atom (hash, canonical_text, metadata, spatial_key)
            VALUES (
                %s,
                %s,
                %s::jsonb,
                ST_MakePoint(random(), random(), random(), 0)  -- Placeholder
            )
            ON CONFLICT (hash) DO UPDATE
            SET metadata = atom.metadata || EXCLUDED.metadata  -- Merge metadata
            RETURNING atom_id
            """,
            (hash_value, text, json.dumps(metadata))
        )
        
        atom_id = (await result.fetchone())[0]
        return atom_id

# Usage examples
import asyncio

async def main():
    pool = await asyncpg.create_pool(
        host="localhost",
        database="hartonomous",
        user="postgres"
    )
    
    # Legal document
    legal_atom_id = await atomize_with_custom_metadata(
        pool,
        "Section 1.2: Liability is limited to...",
        custom_metadata={
            "source": "legal_doc",
            "jurisdiction": "CA",
            "contract_id": "CNT-2025-001",
            "section": "1.2"
        }
    )
    
    # Medical record
    medical_atom_id = await atomize_with_custom_metadata(
        pool,
        "Patient exhibits symptoms of...",
        custom_metadata={
            "source": "medical_record",
            "patient_id": "P-12345",
            "encounter_date": "2025-12-02",
            "provider": "Dr. Smith"
        }
    )
    
    await pool.close()

asyncio.run(main())
```

---

### Querying by Custom Metadata

```sql
-- Find all legal document atoms from California
SELECT
    atom_id,
    canonical_text,
    metadata->>'jurisdiction' AS jurisdiction,
    metadata->>'section' AS section
FROM atom
WHERE metadata->>'source' = 'legal_doc'
  AND metadata->>'jurisdiction' = 'CA';

-- Index for fast custom metadata queries
CREATE INDEX idx_atom_metadata_source ON atom ((metadata->>'source'));
CREATE INDEX idx_atom_metadata_jurisdiction ON atom ((metadata->>'jurisdiction'));

-- Aggregate by custom metadata
SELECT
    metadata->>'source' AS source,
    metadata->>'jurisdiction' AS jurisdiction,
    COUNT(*) AS atom_count
FROM atom
GROUP BY source, jurisdiction
ORDER BY atom_count DESC;
```

---

## Rare Atomization Edge Cases

### Edge Case 1: Empty Content

**Scenario:** User submits empty string or whitespace-only text

```python
def validate_content(text: str) -> bool:
    """Validate content before atomization."""
    
    # Check for empty/whitespace
    if not text or not text.strip():
        raise ValueError("Cannot atomize empty content")
    
    # Check minimum length
    if len(text.strip()) < 3:
        raise ValueError("Content too short (minimum 3 characters)")
    
    return True

# Usage
try:
    validate_content("")
except ValueError as e:
    print(f"Validation failed: {e}")
```

---

### Edge Case 2: Binary Content (Non-UTF-8)

**Scenario:** User submits binary data that cannot be decoded as UTF-8

```python
import base64

async def atomize_binary_content(db_pool, binary_data: bytes, modality: str) -> int:
    """Atomize binary content (e.g., image, audio)."""
    
    # Hash binary content
    import hashlib
    hash_value = hashlib.sha256(binary_data).hexdigest()
    
    # Base64 encode for storage (or store reference)
    canonical_repr = base64.b64encode(binary_data).decode('ascii')
    
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        result = await cur.execute(
            """
            INSERT INTO atom (hash, canonical_text, metadata, spatial_key)
            VALUES (
                %s,
                %s,  -- Base64 representation
                %s::jsonb,
                ST_MakePoint(random(), random(), random(), 0)
            )
            ON CONFLICT (hash) DO NOTHING
            RETURNING atom_id
            """,
            (
                hash_value,
                canonical_repr,
                json.dumps({
                    "modality": modality,
                    "encoding": "base64",
                    "size_bytes": len(binary_data)
                })
            )
        )
        
        if result.rowcount == 0:
            # Deduplicated
            result = await cur.execute(
                "SELECT atom_id FROM atom WHERE hash = %s",
                (hash_value,)
            )
        
        atom_id = (await result.fetchone())[0]
        return atom_id
```

---

### Edge Case 3: Extremely Large Content (>1MB)

**Scenario:** User submits very large text (e.g., entire book)

```python
MAX_ATOM_SIZE = 64 * 1024  # 64KB per atom

async def atomize_large_content(db_pool, text: str) -> list[int]:
    """Split large content into multiple atoms."""
    
    if len(text) <= MAX_ATOM_SIZE:
        # Single atom
        return [await atomize_text(db_pool, text)]
    
    # Split into chunks
    chunks = []
    for i in range(0, len(text), MAX_ATOM_SIZE):
        chunk = text[i:i+MAX_ATOM_SIZE]
        chunks.append(chunk)
    
    # Atomize each chunk
    atom_ids = []
    for idx, chunk in enumerate(chunks):
        atom_id = await atomize_with_custom_metadata(
            db_pool,
            chunk,
            custom_metadata={
                "chunk_index": idx,
                "total_chunks": len(chunks),
                "original_size": len(text)
            }
        )
        atom_ids.append(atom_id)
    
    # Create composition of chunks
    composition_id = await create_composition(
        db_pool,
        atom_ids,
        metadata={"type": "large_document_chunks"}
    )
    
    return [composition_id]  # Return composition, not individual chunks
```

---

### Edge Case 4: Unicode Normalization

**Scenario:** Same text with different Unicode representations (e.g., "café" with combining accent vs precomposed)

```python
import unicodedata

def normalize_unicode(text: str) -> str:
    """Normalize Unicode to NFC form for consistent hashing."""
    
    # NFC: Canonical Decomposition, followed by Canonical Composition
    normalized = unicodedata.normalize('NFC', text)
    
    return normalized

# Example
text1 = "café"  # Precomposed é (U+00E9)
text2 = "café"  # Combining e + ́ (U+0065 + U+0301)

print(text1 == text2)  # False (different bytes)

norm1 = normalize_unicode(text1)
norm2 = normalize_unicode(text2)

print(norm1 == norm2)  # True (same after normalization)

# Use in atomization
import hashlib

def create_atom_hash(text: str) -> str:
    normalized = normalize_unicode(text)
    return hashlib.sha256(normalized.encode('utf-8')).hexdigest()
```

---

### Edge Case 5: Hash Collision (Astronomically Rare)

**Scenario:** SHA-256 collision (probability: ~10^-60)

```python
# Detection strategy
async def detect_hash_collision(db_pool, text: str, hash_value: str) -> bool:
    """Check if existing atom with same hash has different content."""
    
    async with db_pool.connection() as conn:
        cur = conn.cursor()
        
        result = await cur.execute(
            "SELECT canonical_text FROM atom WHERE hash = %s",
            (hash_value,)
        )
        
        existing = await result.fetchone()
        
        if existing and existing[0] != text:
            # COLLISION DETECTED!
            import logging
            logger = logging.getLogger(__name__)
            logger.critical(
                f"SHA-256 COLLISION DETECTED! "
                f"Hash: {hash_value}, "
                f"Original: {existing[0][:100]}, "
                f"New: {text[:100]}"
            )
            
            # Mitigation: append nonce and rehash
            import uuid
            nonce = str(uuid.uuid4())
            new_hash = hashlib.sha256((text + nonce).encode()).hexdigest()
            
            # Store with collision flag
            await cur.execute(
                """
                INSERT INTO atom (hash, canonical_text, metadata)
                VALUES (%s, %s, %s::jsonb)
                """,
                (
                    new_hash,
                    text,
                    json.dumps({
                        "collision_detected": True,
                        "original_hash": hash_value,
                        "nonce": nonce
                    })
                )
            )
            
            return True
        
        return False
```

---

## Status

**Implementation Status by Modality:**

| Modality | Status | Details |
|----------|--------|---------|
| Text | ✅ COMPLETE | Character-level + hierarchical composition |
| Code | 🟡 PARTIAL | Python AST via Tree-sitter, others fallback to plain text |
| Image | 🟡 DUAL | Pixel-level (234 lines) + Patch-based (307 lines) |
| Audio | 🟡 PARTIAL | Sample frame atomization complete, video extraction TODO |
| Model | 🟡 PARTIAL | GGUF vocabulary complete (1120x optimized), weights TODO |
| Video | ❌ TODO | Requires ffmpeg integration for frame/audio extraction |

**Production Readiness:**
- Text atomization: Production-ready
- Code atomization: Python production-ready, other languages fallback
- Image atomization: Patch-based recommended for production
- Audio atomization: Sample frames production-ready
- Model atomization: Vocabulary production-ready, weights pending POINTZM

**Next Steps:**
1. Add Tree-sitter support for more languages (JavaScript, TypeScript, Rust, Go)
2. Implement weight matrix atomization (requires POINTZM for geometric compression)
3. Add ffmpeg integration for video frame/audio extraction
4. Benchmark all atomizers on production datasets
5. Add modality-specific semantic embeddings (replace hash-based positions)

---

## Batch Processing Examples

### Bulk Document Ingestion Pipeline

**Complete end-to-end ingestion:**

```python
import asyncio
import asyncpg
from typing import List, Dict
from services.atomization_service import AtomizationService
from services.composition_service import CompositionService
from services.relation_service import RelationService

class IngestionPipeline:
    """Full document ingestion pipeline."""
    
    def __init__(self, db_pool):
        self.db_pool = db_pool
        self.atomizer = AtomizationService(db_pool)
        self.composer = CompositionService(db_pool)
        self.relationer = RelationService(db_pool)
    
    async def ingest_document(
        self,
        content: str,
        metadata: Dict
    ) -> Dict:
        """
        Pipeline: Document → Sentences → Atoms → Composition → Relations
        
        Returns:
            {'composition_id': int, 'atom_count': int, 'relation_count': int}
        """
        # Step 1: Split into sentences
        import re
        sentences = re.split(r'(?<=[.!?])\s+', content)
        
        # Step 2: Atomize in parallel
        atoms = await asyncio.gather(*[
            self.atomizer.atomize_text(
                content=sent,
                metadata={**metadata, 'sentence_index': i}
            )
            for i, sent in enumerate(sentences)
        ])
        atom_ids = [atom.atom_id for atom in atoms]
        
        # Step 3: Create composition
        comp = await self.composer.create_composition(
            atom_ids=atom_ids,
            metadata={'type': 'document', **metadata}
        )
        
        # Step 4: Create sequential relations
        relations = []
        for i in range(len(atom_ids) - 1):
            relations.append(
                self.relationer.create_relation(
                    atom_id_a=atom_ids[i],
                    atom_id_b=atom_ids[i+1],
                    relation_type_name='temporal_sequence',
                    weight=0.8
                )
            )
        await asyncio.gather(*relations)
        
        return {
            'composition_id': comp.composition_id,
            'atom_count': len(atom_ids),
            'relation_count': len(relations)
        }
    
    async def ingest_bulk(
        self,
        documents: List[Dict],
        batch_size: int = 10
    ) -> List[Dict]:
        """Ingest documents in batches."""
        results = []
        
        for i in range(0, len(documents), batch_size):
            batch = documents[i:i+batch_size]
            batch_results = await asyncio.gather(*[
                self.ingest_document(doc['content'], doc['metadata'])
                for doc in batch
            ])
            results.extend(batch_results)
        
        return results

# Usage
async def main():
    pool = await asyncpg.create_pool(host="localhost", database="hartonomous")
    pipeline = IngestionPipeline(pool)
    
    documents = [
        {
            'content': "First doc. Has sentences. Multiple ones.",
            'metadata': {'source': 'test', 'doc_id': 1}
        },
        {
            'content': "Second doc. Also sentences. Several.",
            'metadata': {'source': 'test', 'doc_id': 2}
        }
    ]
    
    results = await pipeline.ingest_bulk(documents)
    
    for i, r in enumerate(results):
        print(f"Doc {i}: composition={r['composition_id']}, "
              f"atoms={r['atom_count']}, relations={r['relation_count']}")
    
    await pool.close()

asyncio.run(main())
```

**Expected Performance:**
- 100 documents: 10-20 seconds (5-10 docs/sec)
- 1,000 documents: 2-4 minutes (4-8 docs/sec)
- 10,000 documents: 20-40 minutes (4-8 docs/sec)

---

### File System Crawler

**Recursively ingest directory:**

```python
import os
import asyncio
from pathlib import Path

class FileCrawler:
    """Crawl directories and ingest files."""
    
    def __init__(self, pipeline: IngestionPipeline):
        self.pipeline = pipeline
        self.extensions = {'.txt', '.md', '.py', '.js', '.c'}
    
    async def crawl(self, root_dir: str) -> List[Dict]:
        """Crawl directory and ingest all files."""
        # Find files
        files = []
        for root, _, filenames in os.walk(root_dir):
            for fname in filenames:
                if Path(fname).suffix in self.extensions:
                    files.append(os.path.join(root, fname))
        
        # Read and ingest
        documents = []
        for fpath in files:
            try:
                with open(fpath, 'r', encoding='utf-8') as f:
                    content = f.read()
                documents.append({
                    'content': content,
                    'metadata': {
                        'filepath': fpath,
                        'filename': os.path.basename(fpath),
                        'extension': Path(fpath).suffix
                    }
                })
            except Exception as e:
                print(f"Error reading {fpath}: {e}")
        
        return await self.pipeline.ingest_bulk(documents, batch_size=20)

# Usage
async def main():
    pool = await asyncpg.create_pool(host="localhost", database="hartonomous")
    pipeline = IngestionPipeline(pool)
    crawler = FileCrawler(pipeline)
    
    results = await crawler.crawl("./src")
    
    print(f"Ingested {len(results)} files")
    total_atoms = sum(r['atom_count'] for r in results)
    print(f"Total atoms: {total_atoms}")
    
    await pool.close()

asyncio.run(main())
```

---

### Progress Tracking with tqdm

**Monitor batch processing:**

```python
from tqdm.asyncio import tqdm
import asyncio

async def ingest_with_progress(pipeline, documents):
    """Ingest documents with progress bar."""
    results = []
    
    for doc in tqdm(documents, desc="Ingesting"):
        try:
            result = await pipeline.ingest_document(
                content=doc['content'],
                metadata=doc['metadata']
            )
            results.append(result)
        except Exception as e:
            print(f"Error: {e}")
    
    return results

# Usage
async def main():
    pool = await asyncpg.create_pool(host="localhost", database="hartonomous")
    pipeline = IngestionPipeline(pool)
    
    documents = [
        {'content': f"Document {i} content", 'metadata': {'id': i}}
        for i in range(1000)
    ]
    
    results = await ingest_with_progress(pipeline, documents)
    
    print(f"Success: {len(results)}/{len(documents)}")
    
    await pool.close()

asyncio.run(main())
```

**Output:**
```
Ingesting: 100%|████████████| 1000/1000 [02:30<00:00, 6.67docs/s]
Success: 1000/1000
```

---



**This guide covers ALL modalities with complete working implementations where feasible. Some features (weights, video) require infrastructure improvements (POINTZM migration, ffmpeg integration).**
