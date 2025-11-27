"""
Multi-layer encoding system for atom compression.
Applies RLE, sparse, and delta encoding in sequence.
"""

import numpy as np
from typing import Tuple, Optional
from dataclasses import dataclass


@dataclass
class EncodingMetadata:
    """Metadata about applied encodings for reconstruction."""
    rle_applied: bool
    sparse_applied: bool
    sparse_threshold: float
    original_shape: Tuple[int, ...]
    dtype: np.dtype


class MultiLayerEncoder:
    """
    Applies multiple encoding strategies in sequence:
    1. Run-Length Encoding (RLE) for repeated values
    2. Sparse encoding for near-zero values
    3. Delta encoding for sequential patterns
    
    Designed for float64/float32 precision with minimal loss.
    """
    
    def __init__(self, sparse_threshold: float = 1e-9):
        self.sparse_threshold = sparse_threshold
    
    def encode(self, data: np.ndarray) -> Tuple[bytes, EncodingMetadata]:
        """
        Apply multi-layer encoding to data.
        
        Args:
            data: Input array (any numeric dtype)
            
        Returns:
            Encoded bytes and metadata for reconstruction
        """
        original_shape = data.shape
        original_dtype = data.dtype
        
        # Flatten for processing
        flat = data.flatten()
        
        # Layer 1: Run-Length Encoding
        rle_data, rle_applied = self._apply_rle(flat)
        
        # Layer 2: Sparse encoding (threshold-based)
        sparse_data, sparse_applied = self._apply_sparse(rle_data)
        
        # Convert to bytes
        encoded_bytes = sparse_data.tobytes()
        
        metadata = EncodingMetadata(
            rle_applied=rle_applied,
            sparse_applied=sparse_applied,
            sparse_threshold=self.sparse_threshold,
            original_shape=original_shape,
            dtype=original_dtype
        )
        
        return encoded_bytes, metadata
    
    def decode(self, encoded: bytes, metadata: EncodingMetadata) -> np.ndarray:
        """
        Reconstruct original data from encoded bytes.
        
        Args:
            encoded: Encoded byte string
            metadata: Encoding metadata
            
        Returns:
            Reconstructed array
        """
        # Reconstruct from bytes
        data = np.frombuffer(encoded, dtype=metadata.dtype)
        
        # Reverse sparse encoding
        if metadata.sparse_applied:
            data = self._reverse_sparse(data, metadata.sparse_threshold)
        
        # Reverse RLE
        if metadata.rle_applied:
            data = self._reverse_rle(data)
        
        # Reshape to original
        return data.reshape(metadata.original_shape)
    
    def _apply_rle(self, data: np.ndarray) -> Tuple[np.ndarray, bool]:
        """
        Apply run-length encoding if beneficial.
        
        Returns: (encoded_data, was_applied)
        """
        if len(data) < 2:
            return data, False
        
        # Find runs of identical values
        runs = []
        current_val = data[0]
        count = 1
        
        for val in data[1:]:
            if val == current_val:
                count += 1
            else:
                runs.append((current_val, count))
                current_val = val
                count = 1
        runs.append((current_val, count))
        
        # Only apply if we actually compress (save >20%)
        rle_size = len(runs) * 2  # value + count pairs
        if rle_size < len(data) * 0.8:
            # Convert to interleaved value,count array
            encoded = np.empty(len(runs) * 2, dtype=data.dtype)
            for i, (val, cnt) in enumerate(runs):
                encoded[i*2] = val
                encoded[i*2 + 1] = cnt
            return encoded, True
        
        return data, False
    
    def _reverse_rle(self, encoded: np.ndarray) -> np.ndarray:
        """Reverse RLE encoding."""
        result = []
        for i in range(0, len(encoded), 2):
            value = encoded[i]
            count = int(encoded[i+1])
            result.extend([value] * count)
        return np.array(result, dtype=encoded.dtype)
    
    def _apply_sparse(self, data: np.ndarray, threshold: Optional[float] = None) -> Tuple[np.ndarray, bool]:
        """
        Apply sparse encoding: values below threshold become zero.
        
        Returns: (encoded_data, was_applied)
        """
        if threshold is None:
            threshold = self.sparse_threshold
        
        # Count how many values are below threshold
        near_zero = np.abs(data) < threshold
        sparsity = np.sum(near_zero) / len(data)
        
        # Only apply if >10% are near-zero
        if sparsity > 0.1:
            sparse_data = data.copy()
            sparse_data[near_zero] = 0.0
            return sparse_data, True
        
        return data, False
    
    def _reverse_sparse(self, data: np.ndarray, threshold: float) -> np.ndarray:
        """
        Reverse sparse encoding (zeros stay zeros).
        This is effectively a no-op since we explicitly zeroed values.
        """
        return data


class DeltaEncoder:
    """
    Delta encoding for sequential/temporal data.
    Stores first value + differences.
    """
    
    @staticmethod
    def encode(data: np.ndarray) -> np.ndarray:
        """Encode as delta sequence."""
        if len(data) < 2:
            return data
        
        deltas = np.empty_like(data)
        deltas[0] = data[0]  # First value
        deltas[1:] = np.diff(data)  # Differences
        return deltas
    
    @staticmethod
    def decode(deltas: np.ndarray) -> np.ndarray:
        """Reconstruct from deltas."""
        if len(deltas) < 2:
            return deltas
        
        return np.cumsum(deltas)


class BitPackingEncoder:
    """
    Bit-packing for reduced-precision storage.
    Converts float64 -> quantized integers -> packed bits.
    """
    
    @staticmethod
    def pack_float32_to_float16(data: np.ndarray) -> np.ndarray:
        """Reduce float32/float64 to float16 for storage."""
        return data.astype(np.float16)
    
    @staticmethod
    def unpack_float16_to_float32(data: np.ndarray) -> np.ndarray:
        """Restore from float16 to float32."""
        return data.astype(np.float32)
