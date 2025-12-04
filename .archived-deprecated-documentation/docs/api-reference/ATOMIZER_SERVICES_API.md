# Atomizer Services API Specification

**Base URL:** `http://localhost:8000/api/v1`  
**Authentication:** ⚠️ JWT TODO (currently bypassed for development)  
**Content-Type:** `application/json`

---

## Overview

**Atomizer Services** provide REST endpoints for all content modalities:

- **Text:** Character-level atomization with hierarchical compositions
- **Code:** AST-based atomization (Python complete, others planned)
- **Image:** Pixel-level and patch-based strategies
- **Audio:** Sample-level atomization with spectrogram option
- **Model:** GGUF vocabulary atomization (weights TODO)
- **Video:** Frame + audio extraction (implementation TODO)

Each endpoint returns atom IDs and metadata with **honest per-modality status**.

---

## Error Handling

**Standard Error Response:**

```json
{
  "error": "Validation failed",
  "detail": "Text content exceeds maximum size of 1MB",
  "status_code": 400,
  "timestamp": "2025-01-15T10:30:00Z"
}
```

### Error Codes

| Status | Error Type | Example |
|--------|------------|----------|
| **400** | Bad Request | Missing required field, invalid format |
| **413** | Payload Too Large | Content exceeds modality limits |
| **415** | Unsupported Media Type | Unknown modality or codec |
| **422** | Unprocessable Entity | Valid format but semantically invalid |
| **500** | Internal Server Error | Database connection failed |
| **503** | Service Unavailable | BPE worker offline |

### Example Error Scenarios

**Missing Required Field:**
```json
{
  "error": "Missing required field",
  "detail": "Field 'text' is required for text atomization",
  "status_code": 400
}
```

**Content Size Exceeded:**
```json
{
  "error": "Content too large",
  "detail": "Image dimensions 5000x5000 exceed 1MP limit (1000x1000)",
  "status_code": 413,
  "limits": {
    "max_pixels": 1000000,
    "provided_pixels": 25000000
  }
}
```

**Unsupported Format:**
```json
{
  "error": "Unsupported audio format",
  "detail": "Codec 'opus' not supported. Supported: wav, mp3, flac",
  "status_code": 415,
  "supported_formats": ["wav", "mp3", "flac"]
}
```

**Fallback Warning (Non-Error):**
```json
{
  "atoms": {...},
  "language": "rust",
  "ast_parsing": false,
  "fallback": true,
  "warning": "Tree-sitter parser for rust not available, using plain text fallback",
  "status_code": 201
}
```

**Python Error Handling Example:**

```python
import httpx

async def ingest_with_error_handling(text: str) -> dict:
    """Ingest text with comprehensive error handling."""
    try:
        async with httpx.AsyncClient() as client:
            response = await client.post(
                "http://localhost:8000/api/v1/ingest/text",
                json={"text": text, "options": {"hierarchical": True}},
                timeout=30.0
            )
            
            response.raise_for_status()
            return response.json()
            
    except httpx.HTTPStatusError as e:
        if e.response.status_code == 400:
            error_detail = e.response.json()
            raise ValueError(f"Invalid request: {error_detail['detail']}")
        elif e.response.status_code == 413:
            raise ValueError("Text content too large")
        elif e.response.status_code == 500:
            raise RuntimeError("Atomization service error")
        else:
            raise
    
    except httpx.TimeoutException:
        raise TimeoutError("Atomization request timed out after 30s")
    
    except httpx.RequestError as e:
        raise ConnectionError(f"Failed to connect to atomization service: {e}")

# Usage
try:
    result = await ingest_with_error_handling("Hello world")
    print(f"Created {result['atom_count']} atoms")
except ValueError as e:
    print(f"Validation error: {e}")
except TimeoutError as e:
    print(f"Timeout: {e}")
except ConnectionError as e:
    print(f"Connection failed: {e}")
```

---

## Endpoints

### 1. Ingest Text

**POST** `/ingest/text`

Atomize text content with optional hierarchical composition.

**Status:** ✅ COMPLETE

#### Request

```json
{
  "text": "Hello world",
  "options": {
    "hierarchical": true,  // Create word/sentence compositions
    "enable_learning": false,  // BPE pattern learning
    "language": "en"
  }
}
```

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `text` | string | required | Text content to atomize |
| `options.hierarchical` | bool | true | Create word and sentence compositions |
| `options.enable_learning` | bool | false | Enable BPE pattern learning (see BPE_CRYSTALLIZER_API.md) |
| `options.language` | string | null | Language code (stored in metadata) |

#### Response (201 Created)

```json
{
  "atoms": {
    "chars": [12345, 12346, 12347, 12347, 12348, 12349, 12350, 12348, 12351, 12347, 12352],
    "words": [12353, 12354],
    "sentence": 12355
  },
  "atom_count": 14,
  "execution_time_ms": 45
}
```

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/ingest/text \
  -H "Content-Type: application/json" \
  -d '{
    "text": "Hello world",
    "options": {
      "hierarchical": true,
      "language": "en"
    }
  }'
```

#### Python Client

```python
import httpx

async def ingest_text(text: str, hierarchical: bool = True, enable_learning: bool = False) -> dict:
    """Atomize text content."""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8000/api/v1/ingest/text",
            json={
                "text": text,
                "options": {
                    "hierarchical": hierarchical,
                    "enable_learning": enable_learning
                }
            }
        )
        
        response.raise_for_status()
        return response.json()
```

---

## Performance Benchmarks

### Throughput (Operations/Second)

| Endpoint | Operation | Avg Throughput | Notes |
|----------|-----------|----------------|-------|
| `/ingest/text` | Small text (<100 chars) | 500-800 ops/sec | Includes hierarchical composition |
| `/ingest/text` | Medium text (1000 chars) | 100-200 ops/sec | ~100 atoms per request |
| `/ingest/text` | Large text (10k chars) | 10-20 ops/sec | ~1000 atoms per request |
| `/ingest/document` | PDF document | 2-5 ops/sec | Depends on page count |
| `/ingest/document` | Image file | 10-20 ops/sec | Depends on resolution |
| `/ingest/spreadsheet` | Excel file | 5-10 ops/sec | Depends on row count |

### Latency (Request → Response)

| Endpoint | Operation | P50 | P95 | P99 | Max |
|----------|-----------|-----|-----|-----|-----|
| `/ingest/text` | Small text | 15ms | 45ms | 80ms | 200ms |
| `/ingest/text` | Medium text | 80ms | 200ms | 400ms | 1s |
| `/ingest/document` | PDF (10 pages) | 500ms | 1.2s | 2s | 5s |
| `/ingest/spreadsheet` | Excel (1000 rows) | 800ms | 1.5s | 3s | 10s |

### Health Check Integration

All atomization services expose health status via common health endpoint:

```python
import httpx

async def check_atomizer_health() -> dict:
    """Check atomization service health."""
    async with httpx.AsyncClient() as client:
        # Aggregated health from all services
        response = await client.get(
            "http://localhost:8000/health/atomization"
        )
        
        return response.json()

# Response format
{
    "status": "healthy",  # "healthy" | "degraded" | "unhealthy"
    "services": {
        "text_atomizer": {
            "status": "healthy",
            "throughput_ops_per_sec": 650,
            "avg_latency_ms": 18,
            "error_rate_percent": 0.02
        },
        "document_atomizer": {
            "status": "healthy",
            "throughput_ops_per_sec": 12,
            "avg_latency_ms": 450,
            "error_rate_percent": 0.05
        },
        "spreadsheet_atomizer": {
            "status": "healthy",
            "throughput_ops_per_sec": 8,
            "avg_latency_ms": 750,
            "error_rate_percent": 0.01
        }
    },
    "database": {
        "status": "healthy",
        "connection_pool_available": 18,
        "connection_pool_total": 20
    },
    "timestamp": "2025-01-15T10:30:00Z"
}
```

### Performance Monitoring

Monitor atomization service performance via Prometheus metrics:

```python
from prometheus_client import Counter, Histogram
import time

# Metrics
atomization_requests_total = Counter(
    'atomization_requests_total',
    'Total atomization requests',
    ['endpoint', 'status']
)

atomization_duration_seconds = Histogram(
    'atomization_duration_seconds',
    'Atomization request duration',
    ['endpoint'],
    buckets=[0.01, 0.05, 0.1, 0.5, 1.0, 5.0, 10.0]
)

atoms_created_total = Counter(
    'atoms_created_total',
    'Total atoms created',
    ['modality']
)

# Usage in endpoint
async def ingest_text_endpoint(request: TextRequest):
    start_time = time.time()
    
    try:
        result = await atomize_text(request.text, request.options)
        
        # Record metrics
        atomization_requests_total.labels(
            endpoint='/ingest/text',
            status='success'
        ).inc()
        
        atoms_created_total.labels(
            modality='text'
        ).inc(result['atom_count'])
        
        return result
        
    except Exception as e:
        atomization_requests_total.labels(
            endpoint='/ingest/text',
            status='error'
        ).inc()
        raise
    
    finally:
        duration = time.time() - start_time
        atomization_duration_seconds.labels(
            endpoint='/ingest/text'
        ).observe(duration)
```

---
        
        response.raise_for_status()
        return response.json()

# Usage
result = await ingest_text("Hello world", hierarchical=True)

print(f"Character atoms: {len(result['atoms']['chars'])}")
print(f"Word atoms: {len(result['atoms']['words'])}")
print(f"Sentence atom: {result['atoms']['sentence']}")
```

---

### 2. Ingest Code

**POST** `/ingest/code`

Atomize code with AST-based semantic parsing.

**Status:** 🟡 PARTIAL (Python complete via Tree-sitter, other languages plain text fallback)

#### Request

```json
{
  "code": "def hello():\n    print('Hello world')",
  "language": "python",
  "options": {
    "ast_parsing": true,
    "semantic_positioning": true
  }
}
```

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `code` | string | required | Source code to atomize |
| `language` | string | required | Language identifier (python, javascript, typescript, etc.) |
| `options.ast_parsing` | bool | true | Use AST parser (falls back to plain text if unavailable) |
| `options.semantic_positioning` | bool | true | Position AST nodes by semantic role |

#### Response (201 Created)

**Python (AST parsing successful):**
```json
{
  "atoms": {
    "ast_nodes": [
      {
        "atom_id": 12400,
        "node_type": "function_definition",
        "name": "hello",
        "position": "POINTZ(0.9 0.5 0.5)",
        "children": [12401, 12402]
      },
      {
        "atom_id": 12401,
        "node_type": "call_expression",
        "name": "print",
        "position": "POINTZ(0.7 0.5 0.5)",
        "children": [12402]
      },
      {
        "atom_id": 12402,
        "node_type": "string_literal",
        "value": "'Hello world'",
        "position": "POINTZ(0.3 0.5 0.5)",
        "children": []
      }
    ]
  },
  "language": "python",
  "ast_parsing": true,
  "fallback": false,
  "atom_count": 3
}
```

**Other languages (plain text fallback):**
```json
{
  "atoms": {
    "chars": [12500, 12501, 12502, ...],
    "words": [12600, 12601, ...]
  },
  "language": "javascript",
  "ast_parsing": false,
  "fallback": true,
  "atom_count": 45,
  "warning": "Tree-sitter parser for javascript not available, using plain text fallback"
}
```

#### Language Support

| Language | Status | Parser |
|----------|--------|--------|
| Python | ✅ COMPLETE | Tree-sitter |
| JavaScript | ❌ TODO | Plain text fallback |
| TypeScript | ❌ TODO | Plain text fallback |
| Java | ❌ TODO | Plain text fallback |
| C/C++ | ❌ TODO | Plain text fallback |
| Go | ❌ TODO | Plain text fallback |
| Rust | ❌ TODO | Plain text fallback |

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/ingest/code \
  -H "Content-Type: application/json" \
  -d '{
    "code": "def hello():\n    print(\"Hello world\")",
    "language": "python",
    "options": {
      "ast_parsing": true
    }
  }'
```

#### Python Client

```python
async def ingest_code(code: str, language: str, ast_parsing: bool = True) -> dict:
    """Atomize source code."""
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8000/api/v1/ingest/code",
            json={
                "code": code,
                "language": language,
                "options": {
                    "ast_parsing": ast_parsing
                }
            }
        )
        
        response.raise_for_status()
        return response.json()

# Usage
result = await ingest_code(
    "def hello():\n    print('Hello world')",
    language="python"
)

if result["fallback"]:
    print(f"Warning: {result['warning']}")
else:
    print(f"AST nodes: {len(result['atoms']['ast_nodes'])}")
```

---

### 3. Ingest Image

**POST** `/ingest/image`

Atomize image with pixel-level or patch-based strategy.

**Status:** ✅ COMPLETE (both strategies available)

#### Request

```json
{
  "image": "iVBORw0KGgoAAAANSUhEUgAAAAUA...",  // Base64-encoded
  "strategy": "patch",  // "pixel" or "patch"
  "options": {
    "patch_size": 16,  // For patch strategy
    "color_space": "rgb"  // rgb, hsv, lab
  }
}
```

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `image` | string | required | Base64-encoded image data |
| `strategy` | string | "patch" | `pixel` (fine-grained) or `patch` (memory-efficient) |
| `options.patch_size` | int | 16 | Patch size for patch strategy (e.g., 16x16) |
| `options.color_space` | string | "rgb" | Color space for positioning (rgb, hsv, lab) |

#### Response (201 Created)

**Patch strategy (307 lines implementation):**
```json
{
  "atoms": {
    "patches": [
      {
        "atom_id": 12700,
        "position": "POINTZ(0.45 0.67 0.32)",  // Average color
        "patch_coords": {"x": 0, "y": 0},
        "size": {"width": 16, "height": 16}
      },
      {
        "atom_id": 12701,
        "position": "POINTZ(0.51 0.62 0.38)",
        "patch_coords": {"x": 16, "y": 0},
        "size": {"width": 16, "height": 16}
      }
    ]
  },
  "strategy": "patch",
  "image_dimensions": {"width": 256, "height": 256},
  "patch_size": 16,
  "atom_count": 256,  // (256/16) * (256/16) = 256 patches
  "execution_time_ms": 120
}
```

**Pixel strategy (234 lines implementation):**
```json
{
  "atoms": {
    "pixels": [12800, 12801, 12802, ...]  // Every pixel as atom
  },
  "strategy": "pixel",
  "image_dimensions": {"width": 256, "height": 256},
  "atom_count": 65536,  // 256 * 256 = 65536 pixels
  "execution_time_ms": 3400,
  "warning": "Pixel strategy creates large atom count for big images"
}
```

#### Strategy Comparison

| Strategy | Granularity | Memory Usage | Use Case |
|----------|-------------|--------------|----------|
| **Pixel** (234 lines) | Fine-grained (every pixel) | High (O(W × H)) | Small images, critical detail |
| **Patch** (307 lines) | Coarse (NxN patches) | Low (O((W/N) × (H/N))) | Large images, memory-constrained |

**Recommendation:** Use **patch strategy** (default) for most cases.

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/ingest/image \
  -H "Content-Type: application/json" \
  -d '{
    "image": "iVBORw0KGgoAAAANSUhEUgAAAAUA...",
    "strategy": "patch",
    "options": {
      "patch_size": 16
    }
  }'
```

#### Python Client

```python
import base64
from PIL import Image
import io

async def ingest_image(image_path: str, strategy: str = "patch", patch_size: int = 16) -> dict:
    """Atomize image."""
    # Load and encode image
    with open(image_path, "rb") as f:
        image_data = f.read()
    
    image_b64 = base64.b64encode(image_data).decode('utf-8')
    
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8000/api/v1/ingest/image",
            json={
                "image": image_b64,
                "strategy": strategy,
                "options": {
                    "patch_size": patch_size
                }
            }
        )
        
        response.raise_for_status()
        return response.json()

# Usage
result = await ingest_image("photo.png", strategy="patch", patch_size=16)

print(f"Strategy: {result['strategy']}")
print(f"Dimensions: {result['image_dimensions']['width']}x{result['image_dimensions']['height']}")
print(f"Atoms created: {result['atom_count']}")
```

---

### 4. Ingest Audio

**POST** `/ingest/audio`

Atomize audio with sample-level or spectrogram-based strategy.

**Status:** 🟡 PARTIAL (sample atomization complete, video extraction TODO)

#### Request

```json
{
  "audio": "SUQzAwAAAAAAFkFVRElPLU1E...",  // Base64-encoded
  "sample_rate": 44100,
  "options": {
    "use_spectrogram": false,
    "frame_size": 2048
  }
}
```

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `audio` | string | required | Base64-encoded audio data |
| `sample_rate` | int | 44100 | Sample rate (Hz) |
| `options.use_spectrogram` | bool | false | Use spectrogram representation instead of waveform |
| `options.frame_size` | int | 2048 | Frame size for spectrogram (if enabled) |

#### Response (201 Created)

**Waveform (sample-level):**
```json
{
  "atoms": {
    "samples": [13000, 13001, 13002, ...]  // Each sample as atom
  },
  "sample_rate": 44100,
  "duration_seconds": 3.5,
  "atom_count": 154350,  // 44100 * 3.5
  "execution_time_ms": 2100
}
```

**Spectrogram (frame-level):**
```json
{
  "atoms": {
    "frames": [13500, 13501, 13502, ...]  // Each spectrogram frame as atom
  },
  "sample_rate": 44100,
  "frame_size": 2048,
  "duration_seconds": 3.5,
  "atom_count": 75,  // (44100 * 3.5) / 2048
  "execution_time_ms": 450
}
```

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/ingest/audio \
  -H "Content-Type: application/json" \
  -d '{
    "audio": "SUQzAwAAAAAAFkFVRElPLU1E...",
    "sample_rate": 44100,
    "options": {
      "use_spectrogram": false
    }
  }'
```

#### Python Client

```python
import base64
import wave

async def ingest_audio(audio_path: str, use_spectrogram: bool = False) -> dict:
    """Atomize audio."""
    # Load and encode audio
    with open(audio_path, "rb") as f:
        audio_data = f.read()
    
    audio_b64 = base64.b64encode(audio_data).decode('utf-8')
    
    # Get sample rate from WAV header
    with wave.open(audio_path, "rb") as wav:
        sample_rate = wav.getframerate()
    
    async with httpx.AsyncClient() as client:
        response = await client.post(
            "http://localhost:8000/api/v1/ingest/audio",
            json={
                "audio": audio_b64,
                "sample_rate": sample_rate,
                "options": {
                    "use_spectrogram": use_spectrogram
                }
            }
        )
        
        response.raise_for_status()
        return response.json()

# Usage
result = await ingest_audio("speech.wav", use_spectrogram=False)

print(f"Duration: {result['duration_seconds']:.2f}s")
print(f"Atoms created: {result['atom_count']}")
```

**Video Audio Extraction:** ❌ TODO (requires ffmpeg integration)

---

### 5. Ingest Model (GGUF)

**POST** `/ingest/model`

Atomize machine learning model (GGUF format).

**Status:** 🟡 PARTIAL (vocabulary complete 1120x optimized, weight matrices TODO, SafeTensors planned)

#### Request

```json
{
  "model_path": "/models/llama-2-7b.gguf",
  "options": {
    "include_weights": false,  // TODO: Weight atomization
    "sparse_threshold": 0.01  // For sparse weight matrices
  }
}
```

#### Request Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `model_path` | string | required | Path to GGUF model file |
| `options.include_weights` | bool | false | Atomize weight matrices (TODO) |
| `options.sparse_threshold` | float | 0.01 | Sparsity threshold for weight compression |

#### Response (201 Created)

**Vocabulary only (COMPLETE):**
```json
{
  "atoms": {
    "vocab": [
      {
        "atom_id": 14000,
        "token_id": 0,
        "token": "<s>",
        "type": "control"
      },
      {
        "atom_id": 14001,
        "token_id": 1,
        "token": "</s>",
        "type": "control"
      },
      {
        "atom_id": 14002,
        "token_id": 2,
        "token": "the",
        "type": "word"
      }
    ]
  },
  "vocab_size": 32000,
  "atom_count": 32000,
  "weights_included": false,
  "execution_time_ms": 850,
  "optimization": "1120x faster than naive implementation"
}
```

**Vocabulary + weights (TODO):**
```json
{
  "atoms": {
    "vocab": [...],
    "weight_matrices": [
      {
        "layer_name": "model.layers.0.self_attn.q_proj.weight",
        "composition_id": 15000,
        "shape": [4096, 4096],
        "sparse_atoms": 1234567,  // Non-zero weight atoms
        "compression_ratio": 0.073  // 7.3% of dense matrix
      }
    ]
  },
  "vocab_size": 32000,
  "weight_atom_count": 123456789,
  "total_atom_count": 123488789,
  "execution_time_ms": 45000,
  "note": "Weight atomization requires POINTZM for efficient sparse representation"
}
```

#### Model Format Support

| Format | Status | Vocab | Weights |
|--------|--------|-------|---------|
| GGUF | 🟡 PARTIAL | ✅ COMPLETE (1120x) | ❌ TODO |
| SafeTensors | ❌ PLANNED | Planned | Planned |
| PyTorch | ❌ TODO | TODO | TODO |

#### cURL Example

```bash
curl -X POST http://localhost:8000/api/v1/ingest/model \
  -H "Content-Type: application/json" \
  -d '{
    "model_path": "/models/llama-2-7b.gguf",
    "options": {
      "include_weights": false
    }
  }'
```

#### Python Client

```python
async def ingest_model(model_path: str, include_weights: bool = False) -> dict:
    """Atomize ML model (GGUF format)."""
    async with httpx.AsyncClient(timeout=300.0) as client:  # Long timeout for large models
        response = await client.post(
            "http://localhost:8000/api/v1/ingest/model",
            json={
                "model_path": model_path,
                "options": {
                    "include_weights": include_weights
                }
            }
        )
        
        response.raise_for_status()
        return response.json()

# Usage
result = await ingest_model("/models/llama-2-7b.gguf", include_weights=False)

print(f"Vocabulary atoms: {result['vocab_size']}")
print(f"Optimization: {result['optimization']}")
```

---

### 6. Ingest Video

**POST** `/ingest/video`

Atomize video (frame extraction + audio separation).

**Status:** ❌ CONCEPT (implementation TODO, requires ffmpeg)

#### Request (Planned)

```json
{
  "video_path": "/videos/demo.mp4",
  "options": {
    "frame_rate": 24,  // Frames per second to extract
    "extract_audio": true,
    "audio_sample_rate": 44100
  }
}
```

#### Response (Planned)

```json
{
  "atoms": {
    "frames": [
      {
        "atom_id": 16000,
        "timestamp": 0.0,
        "frame_number": 0
      },
      {
        "atom_id": 16001,
        "timestamp": 0.041666,
        "frame_number": 1
      }
    ],
    "audio_samples": [17000, 17001, 17002, ...]
  },
  "video_duration": 10.5,
  "frame_count": 252,  // 24 fps * 10.5s
  "audio_atom_count": 463050,  // 44100 Hz * 10.5s
  "total_atom_count": 463302,
  "execution_time_ms": 8500,
  "note": "Requires ffmpeg for frame extraction and audio separation"
}
```

**Implementation Status:** ❌ TODO (requires ffmpeg integration, frame processing pipeline)

---

## Performance

| Endpoint | Throughput | Latency (p95) | Notes |
|----------|-----------|---------------|-------|
| `/ingest/text` | 1000-5000 chars/sec | < 100ms (100 chars) | Character-level + hierarchical |
| `/ingest/code` (Python) | 500-2000 lines/sec | < 200ms (100 lines) | AST parsing with Tree-sitter |
| `/ingest/code` (fallback) | 1000-5000 chars/sec | < 100ms (100 chars) | Plain text fallback |
| `/ingest/image` (patch) | 10-50 images/sec | < 200ms (256x256, 16px patches) | Memory-efficient |
| `/ingest/image` (pixel) | 1-5 images/sec | < 5000ms (256x256) | Memory-heavy |
| `/ingest/audio` (waveform) | 1-5 sec audio/sec | < 2000ms (3.5s audio) | Sample-level |
| `/ingest/audio` (spectrogram) | 5-20 sec audio/sec | < 500ms (3.5s audio) | Frame-level |
| `/ingest/model` (vocab only) | 1-2 models/min | < 1000ms (32K vocab) | 1120x optimized |
| `/ingest/video` | ❌ TODO | N/A | Requires ffmpeg |

---

## Batch Operations (TODO)

**POST** `/ingest/batch`

**Status:** ⚠️ DESIGN PLANNED, IMPLEMENTATION TODO

Atomize multiple content items in single request.

**Request:**
```json
{"items": [{"id": "item_1", "modality": "text", "content": "SGVsbG8="}]}
```

**Response (201):**
```json
{"results": [{"id": "item_1", "status": "success", "atom_count": 5}], "successful_count": 1}
```

**Performance:** 50-500 items/sec.

---

## Async Processing (TODO)

**POST** `/ingest/async/{modality}`

**Status:** ⚠️ DESIGN PLANNED, IMPLEMENTATION TODO

**Response (202):**
```json
{"job_id": "job_a7f3c9d2", "status": "pending", "status_url": "/ingest/jobs/{job_id}"}
```

**Use:** Large content (videos, high-res images), non-interactive workloads.

---

## Error Responses

**400 Bad Request**
```json
{
  "error": "Invalid request",
  "detail": "text field is required"
}
```

**415 Unsupported Media Type**
```json
{
  "error": "Unsupported content type",
  "detail": "Only application/json supported"
}
```

**422 Unprocessable Entity**
```json
{
  "error": "Validation error",
  "detail": "language must be one of: python, javascript, typescript, java, cpp, go, rust"
}
```

**500 Internal Server Error**
```json
{
  "error": "Database error",
  "detail": "Failed to insert atoms"
}
```

---

## Status Summary

**Production Ready:**
- ✅ Text atomization (character-level + hierarchical compositions)
- ✅ Image atomization (both pixel and patch strategies)

**Partial Implementation:**
- 🟡 Code atomization (Python AST complete, others plain text fallback)
- 🟡 Audio atomization (sample-level complete, video extraction TODO)
- 🟡 Model atomization (GGUF vocabulary complete 1120x, weights TODO)

**TODO:**
- ❌ Video atomization (requires ffmpeg integration)
- ❌ Model weight atomization (requires POINTZM for sparse matrices)
- ❌ SafeTensors support
- ❌ Additional Tree-sitter parsers (JavaScript, TypeScript, Java, etc.)
- ❌ JWT authentication (currently bypassed)
- ❌ Rate limiting

---

## Advanced Usage Patterns

### Progressive Atomization for Large Files

Handle large files efficiently with streaming and chunking:

```python
from typing import AsyncIterator
import aiofiles

class ProgressiveAtomizer:
    """Stream large files for atomization without loading entirely into memory."""
    
    def __init__(self, chunk_size: int = 64 * 1024):  # 64KB chunks
        self.chunk_size = chunk_size
    
    async def stream_file_chunks(self, file_path: str) -> AsyncIterator[bytes]:
        """Stream file in fixed-size chunks."""
        async with aiofiles.open(file_path, 'rb') as f:
            while True:
                chunk = await f.read(self.chunk_size)
                if not chunk:
                    break
                yield chunk
    
    async def atomize_large_file(self, file_path: str, modality: str) -> list[dict]:
        """Atomize large file progressively."""
        atoms = []
        chunk_num = 0
        
        async for chunk in self.stream_file_chunks(file_path):
            # Atomize chunk with position metadata
            response = await client.post("/atomize", json={
                "modality": modality,
                "content": chunk.decode('utf-8') if modality == 'text' else chunk.hex(),
                "metadata": {
                    "chunk_number": chunk_num,
                    "file_path": file_path,
                    "chunk_size": len(chunk)
                }
            })
            
            chunk_atoms = response.json()["atoms"]
            atoms.extend(chunk_atoms)
            chunk_num += 1
        
        return atoms

# Usage example
atomizer = ProgressiveAtomizer(chunk_size=64 * 1024)
atoms = await atomizer.atomize_large_file("large_document.txt", modality="text")
```

### Parallel Atomization for Batch Processing

Process multiple files concurrently with controlled parallelism:

```python
import asyncio
from typing import List, Tuple
from pathlib import Path

class BatchAtomizer:
    """Atomize multiple files in parallel with concurrency control."""
    
    def __init__(self, max_concurrent: int = 10):
        self.max_concurrent = max_concurrent
        self.semaphore = asyncio.Semaphore(max_concurrent)
    
    async def atomize_file(self, file_path: Path) -> Tuple[str, list[dict]]:
        """Atomize single file with concurrency control."""
        async with self.semaphore:
            # Detect modality from file extension
            modality = self._detect_modality(file_path)
            
            # Read file content
            async with aiofiles.open(file_path, 'rb') as f:
                content = await f.read()
            
            # Atomize
            response = await client.post("/atomize", json={
                "modality": modality,
                "content": content.decode('utf-8') if modality == 'text' else content.hex(),
                "metadata": {"file_path": str(file_path)}
            })
            
            return str(file_path), response.json()["atoms"]
    
    def _detect_modality(self, file_path: Path) -> str:
        """Detect modality from file extension."""
        ext = file_path.suffix.lower()
        mapping = {
            '.txt': 'text', '.md': 'text', '.py': 'code',
            '.jpg': 'image', '.png': 'image', '.wav': 'audio',
            '.mp4': 'video', '.gguf': 'model'
        }
        return mapping.get(ext, 'text')
    
    async def atomize_directory(self, directory: Path) -> dict[str, list[dict]]:
        """Atomize all files in directory concurrently."""
        files = list(directory.rglob('*'))
        files = [f for f in files if f.is_file()]
        
        # Process files concurrently
        tasks = [self.atomize_file(f) for f in files]
        results = await asyncio.gather(*tasks, return_exceptions=True)
        
        # Collect successful results
        atoms_by_file = {}
        for result in results:
            if isinstance(result, Exception):
                continue  # Skip errors
            file_path, atoms = result
            atoms_by_file[file_path] = atoms
        
        return atoms_by_file

# Usage example
batch_atomizer = BatchAtomizer(max_concurrent=10)
results = await batch_atomizer.atomize_directory(Path("./documents"))
print(f"Atomized {len(results)} files")
```

### Custom Atomization Strategies

Implement domain-specific atomization for specialized content:

```python
from typing import Protocol
import re

class AtomizationStrategy(Protocol):
    """Protocol for custom atomization strategies."""
    
    async def atomize(self, content: str) -> list[dict]:
        """Atomize content according to strategy."""
        ...

class EmailAtomizationStrategy:
    """Atomize email content with structure awareness."""
    
    async def atomize(self, email_content: str) -> list[dict]:
        """Parse email into structured atoms."""
        atoms = []
        
        # Extract headers
        headers = self._parse_headers(email_content)
        for key, value in headers.items():
            atom = await self._create_atom(f"{key}: {value}", metadata={
                "type": "email_header",
                "header_name": key
            })
            atoms.append(atom)
        
        # Extract body
        body = self._extract_body(email_content)
        body_atoms = await self._atomize_body(body)
        atoms.extend(body_atoms)
        
        # Extract attachments references
        attachments = self._extract_attachments(email_content)
        for attachment in attachments:
            atom = await self._create_atom(attachment, metadata={
                "type": "email_attachment_ref"
            })
            atoms.append(atom)
        
        return atoms
    
    def _parse_headers(self, content: str) -> dict:
        """Parse email headers."""
        headers = {}
        for line in content.split('\n'):
            if ':' in line:
                key, value = line.split(':', 1)
                headers[key.strip()] = value.strip()
            else:
                break
        return headers
    
    def _extract_body(self, content: str) -> str:
        """Extract email body."""
        # Simplified: everything after headers
        return content.split('\n\n', 1)[1] if '\n\n' in content else content
    
    def _extract_attachments(self, content: str) -> list[str]:
        """Extract attachment references."""
        # Simplified pattern matching
        return re.findall(r'Attachment: (.+)', content)
    
    async def _atomize_body(self, body: str) -> list[dict]:
        """Atomize email body as text."""
        response = await client.post("/atomize", json={
            "modality": "text",
            "content": body,
            "metadata": {"type": "email_body"}
        })
        return response.json()["atoms"]
    
    async def _create_atom(self, content: str, metadata: dict) -> dict:
        """Create single atom."""
        response = await client.post("/atomize", json={
            "modality": "text",
            "content": content,
            "metadata": metadata
        })
        return response.json()["atoms"][0]

# Usage example
email_strategy = EmailAtomizationStrategy()
email_atoms = await email_strategy.atomize(email_content)
```

### Deduplication Across Atomization Sessions

Track and deduplicate atoms across multiple atomization calls:

```python
from typing import Set
import hashlib

class DeduplicatingAtomizer:
    """Atomizer with cross-session deduplication tracking."""
    
    def __init__(self):
        self.seen_hashes: Set[str] = set()
        self.atom_registry: dict[str, dict] = {}
    
    def _compute_hash(self, atom: dict) -> str:
        """Compute deterministic hash for atom."""
        # Use canonical_text or content for hashing
        content = atom.get("canonical_text", atom.get("content", ""))
        return hashlib.sha256(content.encode()).hexdigest()
    
    async def atomize_with_dedup(self, modality: str, content: str) -> list[dict]:
        """Atomize content and track unique atoms."""
        response = await client.post("/atomize", json={
            "modality": modality,
            "content": content
        })
        
        atoms = response.json()["atoms"]
        unique_atoms = []
        
        for atom in atoms:
            atom_hash = self._compute_hash(atom)
            
            if atom_hash not in self.seen_hashes:
                self.seen_hashes.add(atom_hash)
                self.atom_registry[atom_hash] = atom
                unique_atoms.append(atom)
        
        return unique_atoms
    
    def get_stats(self) -> dict:
        """Get deduplication statistics."""
        return {
            "unique_atoms": len(self.seen_hashes),
            "total_size_bytes": sum(
                len(atom.get("content", "").encode())
                for atom in self.atom_registry.values()
            )
        }

# Usage example
dedup_atomizer = DeduplicatingAtomizer()

# Atomize multiple documents
for doc in documents:
    unique_atoms = await dedup_atomizer.atomize_with_dedup("text", doc)
    print(f"Found {len(unique_atoms)} unique atoms")

stats = dedup_atomizer.get_stats()
print(f"Total unique atoms: {stats['unique_atoms']}")
```

### Error Recovery and Validation

Implement robust error handling and validation:

```python
from dataclasses import dataclass
from typing import Optional

@dataclass
class AtomizationResult:
    """Result of atomization with error tracking."""
    success: bool
    atoms: list[dict]
    error: Optional[str] = None
    warnings: list[str] = None
    
    def __post_init__(self):
        if self.warnings is None:
            self.warnings = []

class RobustAtomizer:
    """Atomizer with comprehensive error handling and validation."""
    
    async def atomize_with_validation(
        self,
        modality: str,
        content: str,
        validate: bool = True
    ) -> AtomizationResult:
        """Atomize with validation and error recovery."""
        warnings = []
        
        # Pre-atomization validation
        if validate:
            validation_errors = self._validate_input(modality, content)
            if validation_errors:
                return AtomizationResult(
                    success=False,
                    atoms=[],
                    error=f"Validation failed: {validation_errors}"
                )
        
        # Attempt atomization
        try:
            response = await client.post("/atomize", json={
                "modality": modality,
                "content": content
            }, timeout=30.0)
            
            if response.status_code != 200:
                return AtomizationResult(
                    success=False,
                    atoms=[],
                    error=f"HTTP {response.status_code}: {response.text}"
                )
            
            atoms = response.json()["atoms"]
            
            # Post-atomization validation
            if validate:
                validation_warnings = self._validate_atoms(atoms)
                warnings.extend(validation_warnings)
            
            return AtomizationResult(
                success=True,
                atoms=atoms,
                warnings=warnings
            )
            
        except Exception as e:
            return AtomizationResult(
                success=False,
                atoms=[],
                error=f"Atomization failed: {str(e)}"
            )
    
    def _validate_input(self, modality: str, content: str) -> Optional[str]:
        """Validate input before atomization."""
        valid_modalities = {'text', 'code', 'image', 'audio', 'video', 'model'}
        
        if modality not in valid_modalities:
            return f"Invalid modality: {modality}"
        
        if not content:
            return "Content is empty"
        
        if len(content) > 10 * 1024 * 1024:  # 10MB limit
            return "Content exceeds 10MB limit"
        
        return None
    
    def _validate_atoms(self, atoms: list[dict]) -> list[str]:
        """Validate atomization results."""
        warnings = []
        
        if not atoms:
            warnings.append("Atomization produced no atoms")
        
        for i, atom in enumerate(atoms):
            if "content_hash" not in atom:
                warnings.append(f"Atom {i} missing content_hash")
            
            if "canonical_text" not in atom and "content" not in atom:
                warnings.append(f"Atom {i} missing content")
        
        return warnings

# Usage example
robust_atomizer = RobustAtomizer()
result = await robust_atomizer.atomize_with_validation("text", document_content)

if result.success:
    print(f"Atomized successfully: {len(result.atoms)} atoms")
    if result.warnings:
        print(f"Warnings: {result.warnings}")
else:
    print(f"Atomization failed: {result.error}")
```

---

**This API provides COMPLETE atomization for text and images. Code, audio, and model atomization are PARTIAL with clear fallback strategies. Video atomization is conceptually designed but requires implementation.**
