"""
AtomLocator: Deterministic Semantic Coordinate Mapping

Maps atom values to fixed 3D semantic coordinates via SHA-256 hashing.
Ensures atoms always live at the SAME coordinate (deterministic).

Architecture:
1. Hash atom value with SHA-256
2. Extract first 24 bytes (8 bytes per dimension)
3. Map to normalized 3D coordinate in [-1, 1]³
4. Apply Gram-Schmidt orthonormalization (optional, for semantic alignment)

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import struct
from typing import Tuple


class AtomLocator:
    """
    Maps atom values to deterministic semantic coordinates.
    
    The same atom value always produces the same (x, y, z) coordinate.
    This is the foundation of content-addressable geometry.
    """
    
    def __init__(self, coordinate_range: float = 1e6):
        """
        Initialize AtomLocator.
        
        Args:
            coordinate_range: Max coordinate value (default 1M for PostGIS compatibility)
        """
        self.coordinate_range = coordinate_range
    
    def locate(self, value: bytes) -> Tuple[float, float, float]:
        """
        Compute deterministic semantic coordinate for atom value.
        
        Args:
            value: Atom value (raw bytes)
        
        Returns:
            (x, y, z) coordinate tuple
        """
        # Hash the value
        hash_bytes = hashlib.sha256(value).digest()
        
        # Extract 24 bytes (8 per dimension) and map to coordinates
        # Use first 24 bytes of SHA-256 (256 bits = 32 bytes)
        x_bytes = hash_bytes[0:8]
        y_bytes = hash_bytes[8:16]
        z_bytes = hash_bytes[16:24]
        
        # Convert to unsigned 64-bit integers
        x_int = struct.unpack('<Q', x_bytes)[0]
        y_int = struct.unpack('<Q', y_bytes)[0]
        z_int = struct.unpack('<Q', z_bytes)[0]
        
        # Normalize to [0, 1]
        max_uint64 = 2**64 - 1
        x_norm = x_int / max_uint64
        y_norm = y_int / max_uint64
        z_norm = z_int / max_uint64
        
        # Scale to coordinate range: [-coordinate_range, +coordinate_range]
        x = (x_norm * 2 - 1) * self.coordinate_range
        y = (y_norm * 2 - 1) * self.coordinate_range
        z = (z_norm * 2 - 1) * self.coordinate_range
        
        return (x, y, z)
    
    def locate_multiple(self, values: list[bytes]) -> list[Tuple[float, float, float]]:
        """
        Compute coordinates for multiple atoms (vectorized).
        
        Args:
            values: List of atom values
        
        Returns:
            List of (x, y, z) coordinate tuples
        """
        return [self.locate(value) for value in values]
    
    def compute_hilbert_index(self, x: float, y: float, z: float, bits: int = 21) -> int:
        """
        Compute Hilbert curve index for M coordinate.
        
        This enables spatial locality preservation:
        - Close atoms in semantic space → close M values
        - Enables B-tree approximate nearest neighbor
        
        Args:
            x, y, z: Semantic coordinates
            bits: Bits per dimension (default 21 = 63 bits total for BIGINT)
        
        Returns:
            Hilbert index (M coordinate)
        """
        # Normalize coordinates to [0, 2^bits - 1]
        max_val = 2**bits - 1
        
        # Map from [-coordinate_range, +coordinate_range] to [0, max_val]
        x_int = int((x / self.coordinate_range + 1) / 2 * max_val)
        y_int = int((y / self.coordinate_range + 1) / 2 * max_val)
        z_int = int((z / self.coordinate_range + 1) / 2 * max_val)
        
        # Clamp to valid range
        x_int = max(0, min(max_val, x_int))
        y_int = max(0, min(max_val, y_int))
        z_int = max(0, min(max_val, z_int))
        
        # Encode via Hilbert curve
        # (For now, use simple interleaving as placeholder)
        # TODO: Replace with actual Hilbert curve encoding from schema/core/functions/spatial/
        return self._morton_encode(x_int, y_int, z_int)
    
    def _morton_encode(self, x: int, y: int, z: int) -> int:
        """
        Morton (Z-order) encoding as Hilbert placeholder.
        
        Interleaves bits: z2y2x2 z1y1x1 z0y0x0
        
        Args:
            x, y, z: Integer coordinates
        
        Returns:
            Morton code
        """
        def split_by_3(value: int) -> int:
            """Spread bits to every 3rd position."""
            value &= 0x1fffff  # 21 bits
            value = (value | value << 32) & 0x1f00000000ffff
            value = (value | value << 16) & 0x1f0000ff0000ff
            value = (value | value << 8) & 0x100f00f00f00f00f
            value = (value | value << 4) & 0x10c30c30c30c30c3
            value = (value | value << 2) & 0x1249249249249249
            return value
        
        return split_by_3(x) | (split_by_3(y) << 1) | (split_by_3(z) << 2)
