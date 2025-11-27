"""
Hilbert curve encoding/decoding for 3D spatial indexing.

The Hilbert curve maps 3D coordinates to 1D indices while preserving spatial locality.
This enables efficient range queries and spatial clustering in the M dimension of POINTZM.
"""

import numpy as np
from typing import Tuple


def encode_hilbert_3d(x: float, y: float, z: float, order: int = 21) -> int:
    """
    Encode 3D coordinates [0,1]³ to Hilbert curve index.
    
    Args:
        x: X coordinate in [0, 1]
        y: Y coordinate in [0, 1]
        z: Z coordinate in [0, 1]
        order: Hilbert curve order (resolution = 2^order per dimension)
               Default 21 gives ~2M³ resolution
    
    Returns:
        Hilbert index (integer in range [0, 2^(3*order)))
    """
    # Convert to integer coordinates
    max_coord = (1 << order) - 1  # 2^order - 1
    ix = int(np.clip(x * max_coord, 0, max_coord))
    iy = int(np.clip(y * max_coord, 0, max_coord))
    iz = int(np.clip(z * max_coord, 0, max_coord))
    
    # Use optimized C implementation via PostgreSQL for production
    # This is a pure Python fallback for testing
    return _hilbert_3d_encode(ix, iy, iz, order)


def decode_hilbert_3d(index: int, order: int = 21) -> Tuple[float, float, float]:
    """
    Decode Hilbert curve index to 3D coordinates.
    
    Args:
        index: Hilbert index
        order: Hilbert curve order used for encoding
    
    Returns:
        Tuple of (x, y, z) coordinates in [0, 1]³
    """
    max_coord = (1 << order) - 1
    ix, iy, iz = _hilbert_3d_decode(index, order)
    
    return (
        ix / max_coord,
        iy / max_coord,
        iz / max_coord
    )


def _hilbert_3d_encode(x: int, y: int, z: int, order: int) -> int:
    """
    Pure Python implementation of 3D Hilbert curve encoding.
    Based on compact Hilbert indices algorithm.
    """
    index = 0
    
    for i in range(order - 1, -1, -1):
        # Extract bits at current level
        xi = (x >> i) & 1
        yi = (y >> i) & 1
        zi = (z >> i) & 1
        
        # Combine into 3-bit index for this level
        bits = (xi << 2) | (yi << 1) | zi
        
        # Gray code transformation
        bits = _gray_encode_3bit(bits)
        
        # Add to result
        index = (index << 3) | bits
    
    return index


def _hilbert_3d_decode(index: int, order: int) -> Tuple[int, int, int]:
    """
    Pure Python implementation of 3D Hilbert curve decoding.
    """
    x, y, z = 0, 0, 0
    
    for i in range(order):
        # Extract 3 bits at this level
        shift = 3 * (order - 1 - i)
        bits = (index >> shift) & 0x7
        
        # Gray code inverse transformation
        bits = _gray_decode_3bit(bits)
        
        # Extract individual coordinate bits
        xi = (bits >> 2) & 1
        yi = (bits >> 1) & 1
        zi = bits & 1
        
        # Add to coordinates
        x = (x << 1) | xi
        y = (y << 1) | yi
        z = (z << 1) | zi
    
    return (x, y, z)


def _gray_encode_3bit(n: int) -> int:
    """Gray code encoding for 3-bit value."""
    return n ^ (n >> 1)


def _gray_decode_3bit(n: int) -> int:
    """Gray code decoding for 3-bit value."""
    n ^= (n >> 2)
    n ^= (n >> 1)
    return n & 0x7


def hilbert_box_query(
    center_index: int,
    radius: int,
    order: int = 21
) -> Tuple[int, int]:
    """
    Compute Hilbert index range for approximate spatial box query.
    
    Args:
        center_index: Center Hilbert index
        radius: Radius in Hilbert space (approximate)
        order: Hilbert curve order
    
    Returns:
        Tuple of (start_index, end_index) for range query
    """
    max_index = (1 << (3 * order)) - 1
    
    start = max(0, center_index - radius)
    end = min(max_index, center_index + radius)
    
    return (start, end)


def compute_hilbert_distance(
    index1: int,
    index2: int
) -> int:
    """
    Compute distance between two Hilbert indices.
    This is an approximation of spatial distance.
    
    Args:
        index1: First Hilbert index
        index2: Second Hilbert index
    
    Returns:
        Integer distance in Hilbert space
    """
    return abs(index1 - index2)
