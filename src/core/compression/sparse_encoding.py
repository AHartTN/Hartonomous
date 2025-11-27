"""
Sparse encoding for near-zero elimination.
Configurable threshold allows aggressive pruning of insignificant values.
"""

import numpy as np
from typing import Tuple


def apply_sparse_encoding(
    data: np.ndarray,
    threshold: float = 1e-6
) -> Tuple[np.ndarray, np.ndarray]:
    """
    Apply sparse encoding to eliminate near-zero values.
    
    Args:
        data: Input array
        threshold: Values with abs() below this are treated as zero
        
    Returns:
        Tuple of (non_zero_values, flat_indices)
    """
    flat_data = data.ravel()
    mask = np.abs(flat_data) >= threshold
    
    non_zero_values = flat_data[mask]
    indices = np.where(mask)[0]
    
    return (non_zero_values, indices)


def decode_sparse(
    values: np.ndarray,
    indices: np.ndarray,
    shape: Tuple[int, ...]
) -> np.ndarray:
    """
    Decode sparse representation back to dense array.
    
    Args:
        values: Non-zero values
        indices: Flat indices of non-zero values
        shape: Original array shape
        
    Returns:
        Reconstructed dense array
    """
    result = np.zeros(shape, dtype=values.dtype)
    flat_result = result.ravel()
    flat_result[indices] = values
    
    return result
