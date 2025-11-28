"""Decode sparse format function."""

import numpy as np
from typing import Tuple


def decode_sparse_format(
    data: bytes,
    shape: Tuple[int, ...] = None
) -> np.ndarray:
    """
    Decode sparse format back to dense array.

    Args:
        data: Sparse-encoded bytes containing shape, dtype, indices, and values
        shape: Optional shape override (if None, uses shape from encoded data)

    Returns:
        Reconstructed dense numpy array
    """
    offset = 0

    # Read encoded shape
    ndims = data[offset]
    offset += 1

    decoded_shape = []
    for _ in range(ndims):
        dim = int.from_bytes(data[offset:offset+4], 'little')
        decoded_shape.append(dim)
        offset += 4

    # Use decoded shape from data unless override provided
    output_shape = shape if shape is not None else tuple(decoded_shape)

    # Read number of non-zero values
    nnz = int.from_bytes(data[offset:offset+4], 'little')
    offset += 4

    # Read dtype
    dtype_len = data[offset]
    offset += 1
    dtype_str = data[offset:offset+dtype_len].decode('utf-8')
    offset += dtype_len
    dtype = np.dtype(dtype_str)

    # Read indices
    indices_bytes = nnz * 4
    indices = np.frombuffer(
        data[offset:offset+indices_bytes],
        dtype=np.uint32
    )
    offset += indices_bytes

    # Read values
    values = np.frombuffer(data[offset:], dtype=dtype)

    # Reconstruct dense array
    result = np.zeros(output_shape, dtype=dtype)
    flat_result = result.ravel()
    flat_result[indices] = values

    return result
