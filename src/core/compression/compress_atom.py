"""Compress atom function - FIXED dtype preservation."""

import numpy as np
import zlib
import lz4.frame
from typing import Optional, Tuple, Dict, Any

from .compression_magic import COMPRESSION_MAGIC
from .sparse_encoding import apply_sparse_encoding
from .run_length import apply_rle
from .encode_sparse_format import encode_sparse_format


def compress_atom(
    data: np.ndarray,
    dtype: Optional[np.dtype] = None,
    sparse_threshold: float = 1e-6,
    use_rle: bool = True,
    use_dict: bool = False,
) -> Tuple[bytes, Dict[str, Any]]:
    """Apply multi-layer compression to atom data."""
    # USE ACTUAL DATA DTYPE, not default to float64
    if dtype is None:
        actual_dtype = data.dtype
    elif hasattr(dtype, 'type'):  # np.dtype object
        actual_dtype = dtype
    else:  # dtype class like np.float32
        actual_dtype = np.dtype(dtype)

    metadata = {
        'shape': data.shape,
        'dtype': actual_dtype.name,  # Always use .name to get string like 'float32'
        'original_size': data.nbytes,
    }
    
    # Convert if needed
    if data.dtype != actual_dtype:
        data = data.astype(actual_dtype)
    
    # Sparse encoding
    sparse_data, sparse_indices = apply_sparse_encoding(data, sparse_threshold)
    if len(sparse_data) < data.size * 0.7:
        data_bytes = COMPRESSION_MAGIC['sparse']
        data_bytes += encode_sparse_format(sparse_data, sparse_indices, data.shape)
        metadata['compression'] = 'sparse'
        metadata['sparsity'] = 1.0 - (len(sparse_data) / data.size)
    else:
        data_bytes = data.tobytes()
        metadata['compression'] = 'dense'
    
    # RLE
    if use_rle:
        rle_result = apply_rle(data_bytes)
        if len(rle_result) < len(data_bytes) * 0.9:
            data_bytes = COMPRESSION_MAGIC['rle'] + rle_result
            metadata['rle_applied'] = True
    
    # Final compression (LZ4 or zlib)
    try:
        lz4_compressed = lz4.frame.compress(
            data_bytes,
            compression_level=lz4.frame.COMPRESSIONLEVEL_MINHC
        )
        if len(lz4_compressed) < len(data_bytes) * 0.95:
            data_bytes = COMPRESSION_MAGIC['lz4'] + lz4_compressed
            metadata['final_compression'] = 'lz4'
        else:
            zlib_compressed = zlib.compress(data_bytes, level=6)
            if len(zlib_compressed) < len(data_bytes):
                data_bytes = COMPRESSION_MAGIC['zlib'] + zlib_compressed
                metadata['final_compression'] = 'zlib'
            else:
                data_bytes = COMPRESSION_MAGIC['raw'] + data_bytes
                metadata['final_compression'] = 'raw'
    except Exception:
        zlib_compressed = zlib.compress(data_bytes, level=6)
        data_bytes = COMPRESSION_MAGIC['zlib'] + zlib_compressed
        metadata['final_compression'] = 'zlib'
    
    metadata['compressed_size'] = len(data_bytes)
    metadata['compression_ratio'] = metadata['original_size'] / len(data_bytes)
    
    return (data_bytes, metadata)
