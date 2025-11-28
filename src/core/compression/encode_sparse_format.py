"""Encode sparse format function."""

from typing import Tuple

import numpy as np


def encode_sparse_format(
    values: np.ndarray, indices: np.ndarray, shape: Tuple[int, ...]
) -> bytes:
    """Encode sparse data in compact format."""
    result = b""

    result += len(shape).to_bytes(1, "little")
    for dim in shape:
        result += dim.to_bytes(4, "little")

    nnz = len(values)
    result += nnz.to_bytes(4, "little")

    # Store dtype info
    dtype_str = str(values.dtype)
    dtype_bytes = dtype_str.encode("utf-8")
    result += len(dtype_bytes).to_bytes(1, "little")
    result += dtype_bytes

    result += indices.astype(np.uint32).tobytes()
    result += values.tobytes()

    return result
