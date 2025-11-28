"""Decompress atom function."""

import zlib
from typing import Any, Dict

import lz4.frame
import numpy as np

from .compression_magic import COMPRESSION_MAGIC
from .decode_sparse_format import decode_sparse_format
from .run_length import decode_rle


def decompress_atom(compressed: bytes, metadata: Dict[str, Any]) -> np.ndarray:
    """Decompress atom data through multi-layer pipeline."""
    data_bytes = compressed

    if data_bytes[:2] == COMPRESSION_MAGIC["lz4"][:2]:
        data_bytes = lz4.frame.decompress(data_bytes[4:])
    elif data_bytes[:2] == COMPRESSION_MAGIC["zlib"]:
        data_bytes = zlib.decompress(data_bytes[2:])
    elif data_bytes[:2] == COMPRESSION_MAGIC["raw"]:
        data_bytes = data_bytes[2:]

    if metadata.get("rle_applied"):
        if data_bytes[:2] == COMPRESSION_MAGIC["rle"]:
            data_bytes = decode_rle(data_bytes[2:])

    if metadata.get("compression") == "sparse":
        if data_bytes[:2] == COMPRESSION_MAGIC["sparse"]:
            data_bytes = data_bytes[2:]
            shape = metadata.get("shape") or metadata.get("original_shape")
            data = decode_sparse_format(data_bytes, shape)
    else:
        dtype = np.dtype(metadata["dtype"])
        shape = metadata.get("shape") or metadata.get("original_shape")
        data = np.frombuffer(data_bytes, dtype=dtype).reshape(shape)

    return data
