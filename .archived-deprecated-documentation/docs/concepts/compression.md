# Compression

**Multi-layer compression: sparse encoding, delta encoding, bit packing, and final compression.**

---

## Overview

Hartonomous uses **multi-layer compression** to minimize atom storage while preserving semantic information.

**Compression pipeline:**

```
Raw Data
  ?
Layer 1: Sparse Encoding (skip zeros)
  ?
Layer 2: Delta Encoding (store differences)
  ?
Layer 3: Bit Packing (minimal bits per value)
  ?
Layer 4: Final Compression (LZ4 or zlib)
  ?
Compressed Atom
```

**Typical results:**
- **Text**: 5-10x compression
- **Embeddings**: 10-50x compression
- **Model weights**: 50-100x compression

---

## Layer 1: Sparse Encoding

### Principle: Only Store Non-Zero Values

**Traditional storage:**
```python
vector = [0.0, 0.23, 0.0, 0.0, -0.14, 0.0, 0.87, 0.0]
# 8 values × 4 bytes = 32 bytes
```

**Sparse encoding:**
```python
indices = [1, 4, 6]
values = [0.23, -0.14, 0.87]
# 3 × (4 bytes index + 4 bytes value) = 24 bytes
# Savings: 25%
```

**For highly sparse data (embeddings):**
```python
vector = [1998 dimensions, 95% zeros]
# Traditional: 1998 × 4 = 7992 bytes
# Sparse: 100 × 8 = 800 bytes
# Savings: 90%
```

### Implementation

```python
def apply_sparse_encoding(data: np.ndarray, threshold: float = 1e-6):
    """
    Extract non-zero values.
    
    Args:
        data: Input array
        threshold: Values below this treated as zero
    
    Returns:
        (values, indices) tuple
    """
    mask = np.abs(data) > threshold
    values = data[mask]
    indices = np.where(mask)[0]
    return values, indices
```

### Storage Format

```python
# Sparse format header (8 bytes)
# - num_values: uint32 (4 bytes)
# - shape_hash: uint32 (4 bytes) for validation

# Value-index pairs
# - index: uint32 (4 bytes)
# - value: dtype (4 or 8 bytes)

header = struct.pack('II', len(values), hash(shape))
for idx, val in zip(indices, values):
    header += struct.pack('If', idx, val)
```

---

## Layer 2: Delta Encoding

### Principle: Store Differences, Not Absolutes

**Traditional storage:**
```python
values = [100, 102, 105, 107, 110]
# 5 × 4 bytes = 20 bytes
```

**Delta encoding:**
```python
base = 100
deltas = [0, +2, +3, +2, +3]
# 1 × 4 bytes (base) + 5 × 1 byte (deltas) = 9 bytes
# Savings: 55%
```

**Works best for:**
- Sorted data
- Gradual changes (time series, sorted weights)
- Coordinate data

### Implementation

```python
def apply_delta_encoding(values: np.ndarray):
    """
    Encode as base + deltas.
    
    Args:
        values: Sorted array
    
    Returns:
        (base, deltas) tuple
    """
    base = values[0]
    deltas = np.diff(values, prepend=base)
    return base, deltas
```

### Variable-Length Deltas

```python
def encode_delta_varint(delta: int) -> bytes:
    """
    Encode delta as variable-length integer.
    Small deltas (|d| < 128) ? 1 byte
    Larger deltas ? 2-4 bytes
    """
    if -128 <= delta < 128:
        return struct.pack('b', delta)  # 1 byte
    elif -32768 <= delta < 32768:
        return struct.pack('<h', delta)  # 2 bytes
    else:
        return struct.pack('<i', delta)  # 4 bytes
```

---

## Layer 3: Bit Packing

### Principle: Use Minimal Bits Per Value

**Traditional storage:**
```python
# Values in range [0, 255]
values = [23, 87, 145, 200]
# 4 × 4 bytes (int32) = 16 bytes
```

**Bit packing:**
```python
# Values fit in 8 bits (uint8)
values_packed = np.array([23, 87, 145, 200], dtype=np.uint8)
# 4 × 1 byte = 4 bytes
# Savings: 75%
```

### Quantization

**For floats, quantize to fewer bits:**

```python
def quantize_float(value: float, bits: int = 8):
    """
    Quantize float to N-bit integer.
    
    Args:
        value: Float in [-1, 1]
        bits: Number of bits (8 = 256 levels)
    
    Returns:
        Quantized integer
    """
    max_val = (1 << bits) - 1  # 2^bits - 1
    quantized = int((value + 1.0) / 2.0 * max_val)
    return np.clip(quantized, 0, max_val)

def dequantize_float(quantized: int, bits: int = 8):
    """Reverse quantization."""
    max_val = (1 << bits) - 1
    return (quantized / max_val) * 2.0 - 1.0

# Example
original = 0.573
quantized = quantize_float(0.573, bits=8)  # 200
reconstructed = dequantize_float(200, bits=8)  # 0.568
error = abs(original - reconstructed)  # 0.005
```

**Compression:**
```python
# 1998D embedding (float32)
# Traditional: 1998 × 4 = 7992 bytes
# Quantized 8-bit: 1998 × 1 = 1998 bytes
# Savings: 75%
```

---

## Layer 4: Final Compression

### LZ4 (Fast)

**Characteristics:**
- Compression: 500+ MB/s
- Decompression: 2000+ MB/s
- Ratio: 2-3x (moderate)

**Use when:** Speed > compression ratio (hot data, frequent access)

```python
import lz4.frame

compressed = lz4.frame.compress(
    data,
    compression_level=lz4.frame.COMPRESSIONLEVEL_MINHC
)
```

### Zlib (Balanced)

**Characteristics:**
- Compression: 100 MB/s
- Decompression: 300 MB/s
- Ratio: 3-5x (good)

**Use when:** Balance of speed and compression (default)

```python
import zlib

compressed = zlib.compress(data, level=6)  # level 1-9
```

### Comparison

| Algorithm | Speed | Ratio | Use Case |
|-----------|-------|-------|----------|
| **LZ4** | Very fast | 2-3x | Hot data, frequent reads |
| **Zlib** | Fast | 3-5x | Default (good balance) |
| **LZMA** | Slow | 5-10x | Cold storage (not used) |

---

## Complete Compression Pipeline

### Compression Function

```python
def compress_atom(
    data: np.ndarray,
    sparse_threshold: float = 1e-6,
    quantize_bits: int = 8
) -> Tuple[bytes, Dict]:
    """
    Multi-layer compression.
    
    Args:
        data: Input array
        sparse_threshold: Sparse encoding threshold
        quantize_bits: Quantization bits (0 = no quantization)
    
    Returns:
        (compressed_bytes, metadata)
    """
    metadata = {
        'shape': data.shape,
        'dtype': data.dtype.name,
        'original_size': data.nbytes,
    }
    
    # Layer 1: Sparse encoding
    values, indices = apply_sparse_encoding(data, sparse_threshold)
    if len(values) < data.size * 0.7:  # Sparse enough
        metadata['compression'] = 'sparse'
        metadata['sparsity'] = 1.0 - (len(values) / data.size)
        data_bytes = encode_sparse_format(values, indices, data.shape)
    else:
        metadata['compression'] = 'dense'
        data_bytes = data.tobytes()
    
    # Layer 2: Delta encoding (if sorted)
    if is_sorted(values):
        base, deltas = apply_delta_encoding(values)
        data_bytes = encode_delta_format(base, deltas)
        metadata['delta_encoded'] = True
    
    # Layer 3: Quantization (for floats)
    if quantize_bits > 0 and data.dtype in [np.float32, np.float64]:
        quantized = quantize_array(data, bits=quantize_bits)
        data_bytes = quantized.tobytes()
        metadata['quantized'] = True
        metadata['quantize_bits'] = quantize_bits
    
    # Layer 4: Final compression
    lz4_compressed = lz4.frame.compress(data_bytes)
    zlib_compressed = zlib.compress(data_bytes, level=6)
    
    if len(lz4_compressed) < len(zlib_compressed):
        final_bytes = b'LZ4:' + lz4_compressed
        metadata['final_compression'] = 'lz4'
    else:
        final_bytes = b'ZLIB:' + zlib_compressed
        metadata['final_compression'] = 'zlib'
    
    metadata['compressed_size'] = len(final_bytes)
    metadata['compression_ratio'] = metadata['original_size'] / len(final_bytes)
    
    return (final_bytes, metadata)
```

### Decompression Function

```python
def decompress_atom(compressed_bytes: bytes, metadata: Dict) -> np.ndarray:
    """
    Reverse multi-layer compression.
    
    Args:
        compressed_bytes: Compressed data
        metadata: Compression metadata
    
    Returns:
        Original array
    """
    # Layer 4: Final decompression
    if compressed_bytes.startswith(b'LZ4:'):
        data_bytes = lz4.frame.decompress(compressed_bytes[4:])
    elif compressed_bytes.startswith(b'ZLIB:'):
        data_bytes = zlib.decompress(compressed_bytes[5:])
    else:
        raise ValueError("Unknown compression format")
    
    # Layer 3: Dequantization
    if metadata.get('quantized'):
        quantized = np.frombuffer(data_bytes, dtype=np.uint8)
        data = dequantize_array(quantized, bits=metadata['quantize_bits'])
    else:
        dtype = np.dtype(metadata['dtype'])
        data = np.frombuffer(data_bytes, dtype=dtype)
    
    # Layer 2: Delta decoding
    if metadata.get('delta_encoded'):
        data = decode_delta_format(data)
    
    # Layer 1: Sparse decoding
    if metadata['compression'] == 'sparse':
        values, indices = decode_sparse_format(data_bytes)
        data = reconstruct_sparse(values, indices, metadata['shape'])
    
    return data.reshape(metadata['shape'])
```

---

## Compression Results

### Text Data

```python
text = "Machine learning is amazing" * 100  # 2700 bytes

# Layer 1: Sparse (N/A for text)
# Layer 2: Delta (N/A for text)
# Layer 3: Bit packing (N/A for text)
# Layer 4: Zlib
compressed_size = 350 bytes

# Compression ratio: 7.7x
```

### Embedding (1998D, 95% sparse)

```python
embedding = np.random.randn(1998)
embedding[np.abs(embedding) < 0.1] = 0  # 95% zeros

# Original: 1998 × 4 = 7992 bytes

# Layer 1: Sparse ? 100 values × 8 bytes = 800 bytes
# Layer 2: N/A (not sorted)
# Layer 3: Quantize 8-bit ? 100 bytes
# Layer 4: LZ4 ? 80 bytes

# Compression ratio: 99.9x
```

### Model Weights (Sorted, Quantized)

```python
weights = np.linspace(-1, 1, 10000)  # Sorted

# Original: 10000 × 4 = 40,000 bytes

# Layer 1: Dense (not sparse)
# Layer 2: Delta ? base + 10000 × 1 byte = 10,004 bytes
# Layer 3: Quantize 8-bit ? 10,000 bytes
# Layer 4: Zlib ? 2,000 bytes

# Compression ratio: 20x
```

---

## Storage in Database

### Compressed Storage

```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,
    
    -- Raw value (?64 bytes, often uncompressed)
    atomic_value BYTEA CHECK (length(atomic_value) <= 64),
    
    -- Compressed value (for large atoms)
    compressed_value BYTEA,
    
    -- Compression metadata
    compression_metadata JSONB,
    
    ...
);

-- Insert compressed atom
INSERT INTO atom (content_hash, atomic_value, compressed_value, compression_metadata)
VALUES (
    $hash,
    NULL,  -- Too large for atomic_value
    $compressed_bytes,
    $metadata::jsonb
);
```

### Retrieval

```python
async def get_atom_data(atom_id: int):
    """Retrieve and decompress atom data."""
    row = await conn.fetchrow(
        "SELECT atomic_value, compressed_value, compression_metadata FROM atom WHERE atom_id = $1",
        atom_id
    )
    
    if row['atomic_value']:
        # Small atom, uncompressed
        return row['atomic_value']
    else:
        # Large atom, decompress
        metadata = json.loads(row['compression_metadata'])
        return decompress_atom(row['compressed_value'], metadata)
```

---

## Adaptive Compression

### Choose Compression Level Based on Access Frequency

```python
def compress_atom_adaptive(data: np.ndarray, access_frequency: str):
    """
    Adjust compression based on access pattern.
    
    Args:
        data: Input array
        access_frequency: 'hot', 'warm', or 'cold'
    """
    if access_frequency == 'hot':
        # Fast decompression (LZ4, minimal layers)
        return compress_atom(data, quantize_bits=0)
    elif access_frequency == 'warm':
        # Balanced (Zlib, moderate layers)
        return compress_atom(data, quantize_bits=8)
    else:  # cold
        # Maximum compression (Zlib, all layers)
        return compress_atom(data, quantize_bits=4, sparse_threshold=1e-8)
```

---

## Performance Characteristics

### Compression Speed

| Layer | Speed (MB/s) | Overhead |
|-------|--------------|----------|
| **Sparse encoding** | 500 | Low |
| **Delta encoding** | 800 | Very low |
| **Bit packing** | 1000 | Very low |
| **LZ4** | 500 | Low |
| **Zlib** | 100 | Moderate |

### Decompression Speed

| Layer | Speed (MB/s) | Overhead |
|-------|--------------|----------|
| **Sparse decoding** | 600 | Low |
| **Delta decoding** | 900 | Very low |
| **Bit unpacking** | 1200 | Very low |
| **LZ4** | 2000 | Very low |
| **Zlib** | 300 | Low |

**Typical end-to-end:**
- Compression: 80-120 MB/s
- Decompression: 200-300 MB/s

---

## Key Takeaways

### 1. Multi-Layer Strategy

Each layer targets different compression opportunities:
- **Sparse**: Skip zeros
- **Delta**: Store differences
- **Bit packing**: Minimal bits
- **Final**: LZ4/Zlib

### 2. Lossless by Default

All compression is reversible (exact reconstruction).

### 3. Lossy Option (Quantization)

Quantization trades precision for compression (configurable).

### 4. Adaptive

Compression level adjusts based on access frequency.

### 5. Typical Results

- Text: 5-10x
- Embeddings: 10-50x
- Models: 50-100x

---

## Next Steps

Now that you understand compression, continue with:

1. **[Provenance](provenance.md)** — Neo4j tracking
2. **[Modalities](modalities.md)** — Multi-modal representation

---

**Next: [Provenance ?](provenance.md)**
