"""
Atom compressor wrapper - provides unified interface for atomization.
"""

import numpy as np

from .compression_result import CompressionResult
from .multi_layer import compress_atom, decompress_atom


class AtomCompressor:
    """
    Unified compressor for atoms.
    Wraps multi-layer compression system.
    """

    def __init__(self):
        pass

    def compress(
        self, data: np.ndarray, sparse_threshold: float = 1e-6
    ) -> CompressionResult:
        """Compress data array."""
        original_size = data.nbytes

        compressed_bytes, metadata = compress_atom(
            data,
            dtype=data.dtype,
            sparse_threshold=sparse_threshold,
            use_rle=True,
            use_dict=False,
        )

        compressed_size = len(compressed_bytes)

        compression_type = self._get_compression_type(metadata.get("method", "raw"))

        return CompressionResult(
            data=compressed_bytes,
            compression_type=compression_type,
            metadata=metadata,
            original_size=original_size,
            compressed_size=compressed_size,
        )

    def decompress(self, result: CompressionResult) -> np.ndarray:
        """Decompress data."""
        data = decompress_atom(result.data, result.metadata)
        return data

    def _get_compression_type(self, method: str) -> int:
        """Map compression method string to integer type."""
        type_map = {"raw": 0, "sparse": 1, "rle": 2, "lz4": 3, "zlib": 4, "multi": 5}
        return type_map.get(method, 0)
