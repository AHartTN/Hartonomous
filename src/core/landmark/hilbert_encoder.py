"""
Hilbert curve encoder for 3D spatial indexing.

Encodes 3D positions as 1D Hilbert curve indices for O(log n) nearest-neighbor search.
"""

import numpy as np
from typing import Tuple


class HilbertEncoder:
    """
    Encode 3D positions as 1D Hilbert curve indices.
    
    This enables O(log n) nearest-neighbor search using PostgreSQL B-tree
    instead of O(n) vector similarity computation.
    """
    
    def __init__(self, order: int = 16):
        """
        Initialize Hilbert encoder.
        
        Args:
            order: Hilbert curve order (resolution = 2^order per dimension)
                   order=16 gives 65536 cubed = 281 trillion unique positions
        """
        self.order = order
        self.max_val = (1 << order) - 1
    
    def encode(self, x: float, y: float, z: float) -> int:
        """
        Encode normalized [0,1] cubed position to Hilbert index.
        
        Args:
            x, y, z: Coordinates in [0, 1] range
            
        Returns:
            Single 64-bit integer Hilbert index
        """
        x = np.clip(x, 0.0, 1.0)
        y = np.clip(y, 0.0, 1.0)
        z = np.clip(z, 0.0, 1.0)
        
        ix = int(x * self.max_val)
        iy = int(y * self.max_val)
        iz = int(z * self.max_val)
        
        return self._hilbert_encode_3d(ix, iy, iz)
    
    def decode(self, hilbert_index: int) -> Tuple[float, float, float]:
        """
        Decode Hilbert index back to 3D coordinates.
        
        Args:
            hilbert_index: Hilbert curve index
            
        Returns:
            (x, y, z) in [0, 1] range
        """
        ix, iy, iz = self._hilbert_decode_3d(hilbert_index)
        
        x = ix / self.max_val
        y = iy / self.max_val
        z = iz / self.max_val
        
        return (x, y, z)
    
    def _hilbert_encode_3d(self, x: int, y: int, z: int) -> int:
        """3D Hilbert encoding algorithm."""
        hilbert = 0
        
        for i in range(self.order - 1, -1, -1):
            xi = (x >> i) & 1
            yi = (y >> i) & 1
            zi = (z >> i) & 1
            
            index = (xi << 2) | (yi << 1) | zi
            hilbert = (hilbert << 3) | index
        
        return hilbert
    
    def _hilbert_decode_3d(self, hilbert: int) -> Tuple[int, int, int]:
        """3D Hilbert decoding algorithm."""
        x = y = z = 0
        
        for i in range(self.order):
            index = (hilbert >> (i * 3)) & 0x7
            
            xi = (index >> 2) & 1
            yi = (index >> 1) & 1
            zi = index & 1
            
            x |= (xi << i)
            y |= (yi << i)
            z |= (zi << i)
        
        return (x, y, z)
    
    def get_neighbors(self, hilbert_index: int, radius: int = 1000) -> Tuple[int, int]:
        """
        Get Hilbert index range for neighbors.
        
        Args:
            hilbert_index: Center point
            radius: Search radius in Hilbert space
            
        Returns:
            (min_index, max_index) for range query
        """
        min_idx = max(0, hilbert_index - radius)
        max_idx = hilbert_index + radius
        
        return (min_idx, max_idx)
