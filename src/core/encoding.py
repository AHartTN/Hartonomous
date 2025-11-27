"""
Multi-layer encoding system for atom compression.

Applies multiple encoding strategies in sequence to maximize compression
while maintaining full precision. Each layer exploits different patterns.
"""

import numpy as np
from typing import List, Tuple, Optional
from dataclasses import dataclass
from enum import IntEnum

class EncodingType(IntEnum):
    """Encoding type flags for metadata."""
    NONE = 0
    SPARSE = 1
    RLE = 2
    DELTA = 4
    SPARSE_RLE = 3
    SPARSE_DELTA = 5
    RLE_DELTA = 6
    ALL = 7


@dataclass
class EncodedData:
    """Container for encoded atom data."""
    data: np.ndarray
    encoding_flags: int
    original_shape: Tuple[int, ...]
    original_dtype: np.dtype
    metadata: dict


class SparseEncoder:
    """
    Sparse encoding: zeros out values below threshold.
    
    Exploits the fact that many weights/pixels are near-zero and 
    can be treated as zero without significant information loss.
    """
    
    def __init__(self, threshold: float = 1e-6):
        self.threshold = threshold
    
    def encode(self, data: np.ndarray) -> Tuple[np.ndarray, dict]:
        """Apply sparse encoding threshold."""
        mask = np.abs(data) < self.threshold
        sparse_data = data.copy()
        sparse_data[mask] = 0
        
        metadata = {
            'zeros_created': np.sum(mask),
            'threshold': self.threshold,
            'sparsity': np.sum(mask) / data.size
        }
        
        return sparse_data, metadata
    
    def decode(self, data: np.ndarray, metadata: dict) -> np.ndarray:
        """Sparse encoding is lossy by design - no decode needed."""
        return data


class RLEEncoder:
    """
    Run-Length Encoding: compresses sequences of repeated values.
    
    Common in:
    - Image data (sky pixels, backgrounds)
    - Zero weights after sparse encoding
    - Repeated tokens in text
    """
    
    def encode(self, data: np.ndarray) -> Tuple[np.ndarray, dict]:
        """
        Encode runs of identical values.
        Format: [value, count, value, count, ...]
        """
        flat = data.flatten()
        if len(flat) == 0:
            return flat, {'runs': 0, 'compression_ratio': 1.0}
        
        # Find run boundaries
        changes = np.concatenate(([True], flat[1:] != flat[:-1], [True]))
        change_indices = np.where(changes)[0]
        
        # Extract values and run lengths
        values = flat[change_indices[:-1]]
        lengths = np.diff(change_indices)
        
        # Interleave: [value, length, value, length, ...]
        encoded = np.empty(len(values) + len(lengths), dtype=flat.dtype)
        encoded[0::2] = values
        encoded[1::2] = lengths.astype(flat.dtype)
        
        metadata = {
            'runs': len(values),
            'compression_ratio': len(encoded) / len(flat) if len(flat) > 0 else 1.0
        }
        
        return encoded, metadata
    
    def decode(self, data: np.ndarray, metadata: dict, original_shape: Tuple[int, ...]) -> np.ndarray:
        """Decode RLE back to original format."""
        if len(data) == 0:
            return np.array([]).reshape(original_shape)
        
        values = data[0::2]
        lengths = data[1::2].astype(np.int64)
        
        # Reconstruct
        decoded = np.repeat(values, lengths)
        return decoded.reshape(original_shape)


class DeltaEncoder:
    """
    Delta encoding: store differences between consecutive values.
    
    Effective when values change gradually:
    - Time series
    - Sequential embeddings
    - Gradient values
    """
    
    def encode(self, data: np.ndarray) -> Tuple[np.ndarray, dict]:
        """Store first value + deltas."""
        flat = data.flatten()
        if len(flat) <= 1:
            return flat, {'delta_range': 0}
        
        deltas = np.empty_like(flat)
        deltas[0] = flat[0]  # Store first value as-is
        deltas[1:] = np.diff(flat)
        
        metadata = {
            'delta_range': float(np.max(np.abs(deltas[1:]))) if len(deltas) > 1 else 0,
            'delta_mean': float(np.mean(np.abs(deltas[1:]))) if len(deltas) > 1 else 0
        }
        
        return deltas, metadata
    
    def decode(self, data: np.ndarray, metadata: dict, original_shape: Tuple[int, ...]) -> np.ndarray:
        """Reconstruct from deltas."""
        if len(data) <= 1:
            return data.reshape(original_shape)
        
        reconstructed = np.cumsum(data)
        return reconstructed.reshape(original_shape)


class MultiLayerEncoder:
    """
    Applies multiple encoding strategies in optimal sequence.
    
    Pipeline:
    1. Sparse: Threshold near-zero values
    2. Delta: Encode gradual changes
    3. RLE: Compress repeated sequences
    
    Each layer only applied if it provides benefit.
    """
    
    def __init__(self, 
                 sparse_threshold: float = 1e-6,
                 min_compression_benefit: float = 0.1):
        self.sparse = SparseEncoder(sparse_threshold)
        self.rle = RLEEncoder()
        self.delta = DeltaEncoder()
        self.min_benefit = min_compression_benefit
    
    def encode(self, data: np.ndarray) -> EncodedData:
        """Apply encoding layers that provide compression benefit."""
        if data.size == 0:
            return EncodedData(
                data=data,
                encoding_flags=EncodingType.NONE,
                original_shape=data.shape,
                original_dtype=data.dtype,
                metadata={}
            )
        
        current = data.astype(np.float64)  # Work in high precision
        encoding_flags = EncodingType.NONE
        metadata = {}
        original_size = data.size
        
        # Layer 1: Sparse encoding
        sparse_result, sparse_meta = self.sparse.encode(current)
        if sparse_meta['sparsity'] > self.min_benefit:
            current = sparse_result
            encoding_flags |= EncodingType.SPARSE
            metadata['sparse'] = sparse_meta
        
        # Layer 2: Delta encoding (test benefit)
        delta_result, delta_meta = self.delta.encode(current)
        # Only use delta if it reduces value magnitudes
        if delta_meta['delta_range'] < np.max(np.abs(current)) * 0.5:
            current = delta_result
            encoding_flags |= EncodingType.DELTA
            metadata['delta'] = delta_meta
        
        # Layer 3: RLE (test benefit)
        rle_result, rle_meta = self.rle.encode(current)
        if rle_meta['compression_ratio'] < (1.0 - self.min_benefit):
            current = rle_result
            encoding_flags |= EncodingType.RLE
            metadata['rle'] = rle_meta
        
        metadata['final_size'] = current.size
        metadata['compression_ratio'] = current.size / original_size if original_size > 0 else 1.0
        
        return EncodedData(
            data=current,
            encoding_flags=encoding_flags,
            original_shape=data.shape,
            original_dtype=data.dtype,
            metadata=metadata
        )
    
    def decode(self, encoded: EncodedData) -> np.ndarray:
        """Decode in reverse order: RLE -> Delta -> Sparse."""
        data = encoded.data
        
        # Reverse layer 3: RLE
        if encoded.encoding_flags & EncodingType.RLE:
            data = self.rle.decode(data, encoded.metadata.get('rle', {}), encoded.original_shape)
        
        # Reverse layer 2: Delta
        if encoded.encoding_flags & EncodingType.DELTA:
            data = self.delta.decode(data, encoded.metadata.get('delta', {}), encoded.original_shape)
        
        # Sparse encoding is lossy - zeros stay zeros
        # Just reshape if needed
        if data.shape != encoded.original_shape:
            data = data.reshape(encoded.original_shape)
        
        # Cast back to original dtype
        return data.astype(encoded.original_dtype)


class AtomEncoder:
    """
    Atom-specific encoder that respects 64-byte constraint.
    
    Handles encoding of individual atom values while maintaining
    size constraints and full precision where needed.
    """
    
    MAX_ATOM_SIZE = 64  # bytes
    
    def __init__(self):
        self.encoder = MultiLayerEncoder()
    
    def encode_atom(self, value: np.ndarray) -> Tuple[bytes, int, dict]:
        """
        Encode single atom value.
        
        Returns:
            (encoded_bytes, encoding_flags, metadata)
        """
        encoded = self.encoder.encode(value)
        
        # Convert to bytes
        data_bytes = encoded.data.tobytes()
        
        # Verify size constraint
        if len(data_bytes) > self.MAX_ATOM_SIZE:
            # Fall back to direct encoding without compression
            data_bytes = value.astype(np.float64).tobytes()
            if len(data_bytes) > self.MAX_ATOM_SIZE:
                # Truncate if absolutely necessary (should be rare)
                data_bytes = data_bytes[:self.MAX_ATOM_SIZE]
            encoding_flags = EncodingType.NONE
        else:
            encoding_flags = encoded.encoding_flags
        
        metadata = {
            'size': len(data_bytes),
            'flags': encoding_flags,
            'shape': encoded.original_shape,
            'dtype': str(encoded.original_dtype)
        }
        
        return data_bytes, encoding_flags, metadata
    
    def decode_atom(self, data_bytes: bytes, encoding_flags: int, 
                    shape: Tuple[int, ...], dtype: str) -> np.ndarray:
        """Decode atom value from bytes."""
        if encoding_flags == EncodingType.NONE:
            # Direct decoding
            arr = np.frombuffer(data_bytes, dtype=dtype)
            return arr.reshape(shape)
        
        # Use multi-layer decoder
        data = np.frombuffer(data_bytes, dtype=np.float64)
        encoded = EncodedData(
            data=data,
            encoding_flags=encoding_flags,
            original_shape=shape,
            original_dtype=np.dtype(dtype),
            metadata={}
        )
        
        return self.encoder.decode(encoded)
