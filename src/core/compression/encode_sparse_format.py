"""Encode sparse format function."""

import numpy as np
from typing import Tuple


def encode_sparse_format(
    values: np.ndarray,
    indices: np.ndarray,
    shape: Tuple[int, ...]
) -> bytes:
    """Encode sparse data in compact format."""
    result = b''
    
    result += len(shape).to_bytes(1, 'little')
    for dim in shape:
        result += dim.to_bytes(4, 'little')
    
    nnz = len(values)
    result += nnz.to_bytes(4, 'little')
    
    result += indices.astype(np.uint32).tobytes()
    result += values.tobytes()
    
    return result
