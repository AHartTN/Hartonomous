"""
Multi-layer compression pipeline.
Applies multiple compression techniques in sequence for maximum density.
"""

import numpy as np
import zlib
import lz4.frame
from typing import Optional, Tuple, Dict, Any
from .sparse_encoding import apply_sparse_encoding, decode_sparse
from .run_length import apply_rle, decode_rle


COMPRESSION_MAGIC = {
    'zlib': b'\x1f\x8b',
    'lz4': b'\x04\x22\x4d\x18',
    'sparse': b'\xaa\x55',
    'rle': b'\xbb\x66',
    'raw': b'\xff\xff',
}


def compress_atom(
    data: np.ndarray,
    dtype: np.dtype = np.float64,
    sparse_threshold: float = 1e-6,
    use_rle: bool = True,
    use_dict: bool = False,
) -> Tuple[bytes, Dict[str, Any]]:
    """
    Apply multi-layer compression to atom data.
    
    Pipeline:
    1. Sparse encoding (configurable threshold for "zero")
    2. Run-length encoding (for repeated values)
    3. LZ4 compression (fast, good ratio)
    4. Fallback to zlib if LZ4 doesn't help
    
    Args:
        data: Input numpy array (typically float64 weights/embeddings)
        dtype: Data type for storage
        sparse_threshold: Values below this treated as zero
        use_rle: Whether to apply run-length encoding
        use_dict: Whether to use dictionary compression
        
    Returns:
        Tuple of (compressed_bytes, metadata_dict)
    """
    metadata = {
        'shape': data.shape,
        'dtype': str(dtype),
        'original_size': data.nbytes,
    }
    
    # Ensure correct dtype
    if data.dtype != dtype:
        data = data.astype(dtype)
    
    # Layer 1: Sparse encoding (eliminate near-zeros)
    sparse_data, sparse_indices = apply_sparse_encoding(data, sparse_threshold)
    if len(sparse_data) < data.size * 0.7:  # >30% sparsity
        data_bytes = COMPRESSION_MAGIC['sparse']
        data_bytes += _encode_sparse_format(sparse_data, sparse_indices, data.shape)
        metadata['compression'] = 'sparse'
        metadata['sparsity'] = 1.0 - (len(sparse_data) / data.size)
    else:
        data_bytes = data.tobytes()
        metadata['compression'] = 'dense'
    
    # Layer 2: Run-length encoding (for repeated patterns)
    if use_rle:
        rle_result = apply_rle(data_bytes)
        if len(rle_result) < len(data_bytes) * 0.9:  # >10% reduction
            data_bytes = COMPRESSION_MAGIC['rle'] + rle_result
            metadata['rle_applied'] = True
    
    # Layer 3: LZ4 compression (fast with good ratio)
    try:
        lz4_compressed = lz4.frame.compress(
            data_bytes,
            compression_level=lz4.frame.COMPRESSIONLEVEL_MINHC
        )
        if len(lz4_compressed) < len(data_bytes) * 0.95:  # >5% reduction
            data_bytes = COMPRESSION_MAGIC['lz4'] + lz4_compressed
            metadata['final_compression'] = 'lz4'
        else:
            # Layer 4: Fallback to zlib (better ratio, slower)
            zlib_compressed = zlib.compress(data_bytes, level=6)
            if len(zlib_compressed) < len(data_bytes):
                data_bytes = COMPRESSION_MAGIC['zlib'] + zlib_compressed
                metadata['final_compression'] = 'zlib'
            else:
                data_bytes = COMPRESSION_MAGIC['raw'] + data_bytes
                metadata['final_compression'] = 'raw'
    except Exception:
        # Fallback to zlib if LZ4 fails
        zlib_compressed = zlib.compress(data_bytes, level=6)
        data_bytes = COMPRESSION_MAGIC['zlib'] + zlib_compressed
        metadata['final_compression'] = 'zlib'
    
    metadata['compressed_size'] = len(data_bytes)
    metadata['compression_ratio'] = metadata['original_size'] / len(data_bytes)
    
    return (data_bytes, metadata)


def decompress_atom(
    compressed: bytes,
    metadata: Dict[str, Any]
) -> np.ndarray:
    """
    Decompress atom data through multi-layer pipeline.
    
    Args:
        compressed: Compressed bytes
        metadata: Compression metadata from compress_atom
        
    Returns:
        Decompressed numpy array
    """
    data_bytes = compressed
    
    # Detect and apply final decompression
    if data_bytes[:2] == COMPRESSION_MAGIC['lz4'][:2]:
        data_bytes = lz4.frame.decompress(data_bytes[4:])
    elif data_bytes[:2] == COMPRESSION_MAGIC['zlib']:
        data_bytes = zlib.decompress(data_bytes[2:])
    elif data_bytes[:2] == COMPRESSION_MAGIC['raw']:
        data_bytes = data_bytes[2:]
    
    # Decode RLE if applied
    if metadata.get('rle_applied'):
        if data_bytes[:2] == COMPRESSION_MAGIC['rle']:
            data_bytes = decode_rle(data_bytes[2:])
    
    # Decode sparse if applied
    if metadata.get('compression') == 'sparse':
        if data_bytes[:2] == COMPRESSION_MAGIC['sparse']:
            data_bytes = data_bytes[2:]
            data = _decode_sparse_format(data_bytes, metadata['shape'])
    else:
        dtype = np.dtype(metadata['dtype'])
        data = np.frombuffer(data_bytes, dtype=dtype).reshape(metadata['shape'])
    
    return data


def _encode_sparse_format(
    values: np.ndarray,
    indices: np.ndarray,
    shape: Tuple[int, ...]
) -> bytes:
    """
    Encode sparse data in compact format.
    Format: shape_dims | nnz | indices | values
    """
    result = b''
    
    # Encode shape
    result += len(shape).to_bytes(1, 'little')
    for dim in shape:
        result += dim.to_bytes(4, 'little')
    
    # Encode number of non-zeros
    nnz = len(values)
    result += nnz.to_bytes(4, 'little')
    
    # Encode indices (uint32)
    result += indices.astype(np.uint32).tobytes()
    
    # Encode values (preserve dtype)
    result += values.tobytes()
    
    return result


def _decode_sparse_format(
    data: bytes,
    shape: Tuple[int, ...]
) -> np.ndarray:
    """
    Decode sparse format back to dense array.
    """
    offset = 0
    
    # Read shape dimensions
    ndims = data[offset]
    offset += 1
    
    decoded_shape = []
    for _ in range(ndims):
        dim = int.from_bytes(data[offset:offset+4], 'little')
        decoded_shape.append(dim)
        offset += 4
    
    # Read nnz
    nnz = int.from_bytes(data[offset:offset+4], 'little')
    offset += 4
    
    # Read indices
    indices_bytes = nnz * 4
    indices = np.frombuffer(
        data[offset:offset+indices_bytes],
        dtype=np.uint32
    )
    offset += indices_bytes
    
    # Read values
    values = np.frombuffer(data[offset:], dtype=np.float64)
    
    # Reconstruct dense array
    result = np.zeros(shape, dtype=np.float64)
    flat_result = result.ravel()
    flat_result[indices] = values
    
    return result
