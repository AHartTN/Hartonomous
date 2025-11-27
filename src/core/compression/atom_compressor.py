"""
Atom compressor wrapper - provides unified interface for atomization.
"""

import numpy as np
from typing import Optional
from dataclasses import dataclass

from .multi_layer import compress_atom, decompress_atom


@dataclass
class CompressionResult:
    """Result of compression operation."""
    data: bytes
    compression_type: int
    metadata: dict
    original_size: int
    compressed_size: int
    
    @property
    def ratio(self) -> float:
        """Compression ratio."""
        if self.compressed_size == 0:
            return 0.0
        return self.original_size / self.compressed_size


class AtomCompressor:
    """
    Unified compressor for atoms.
    Wraps multi-layer compression system.
    """
    
    def __init__(self):
        pass
    
    def compress(
        self,
        data: np.ndarray,
        sparse_threshold: float = 1e-6
    ) -> CompressionResult:
        """
        Compress data array.
        
        Args:
            data: NumPy array to compress
            sparse_threshold: Threshold for sparse encoding
        
        Returns:
            CompressionResult with compressed data and metadata
        """
        original_size = data.nbytes
        
        # Use multi-layer compression
        compressed_bytes, metadata = compress_atom(
            data,
            dtype=data.dtype,
            sparse_threshold=sparse_threshold,
            use_rle=True,
            use_dict=False
        )
        
        compressed_size = len(compressed_bytes)
        
        # Determine compression type from metadata
        compression_type = self._get_compression_type(metadata.get('method', 'raw'))
        
        return CompressionResult(
            data=compressed_bytes,
            compression_type=compression_type,
            metadata=metadata,
            original_size=original_size,
            compressed_size=compressed_size
        )
    
    def decompress(self, result: CompressionResult) -> np.ndarray:
        """
        Decompress data.
        
        Args:
            result: CompressionResult to decompress
        
        Returns:
            Decompressed NumPy array
        """
        # Use multi-layer decompression
        data = decompress_atom(result.data, result.metadata)
        return data
    
    def _get_compression_type(self, method: str) -> int:
        """Map compression method string to integer type."""
        type_map = {
            'raw': 0,
            'sparse': 1,
            'rle': 2,
            'lz4': 3,
            'zlib': 4,
            'multi': 5
        }
        return type_map.get(method, 0)
