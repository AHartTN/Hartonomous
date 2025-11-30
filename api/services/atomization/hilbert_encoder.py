"""
Hilbert Curve Encoding for 3D Spatial Indexing

Encodes integer coordinates (i, j, layer) to 1D Hilbert indices for:
1. RLE compression - consecutive indices = spatial runs
2. Cache-friendly access - locality preservation
3. Fast range queries - single integer comparison

Lossless roundtrip guaranteed for integer coordinates.
"""

import numpy as np
from typing import Tuple, Optional
import math


class HilbertEncoder:
    """
    Encode 3D integer coordinates to 1D Hilbert indices.
    
    Uses rotation-based algorithm from:
    "Efficient 3D Hilbert Curve Encoding and Decoding Algorithms"
    https://arxiv.org/pdf/2308.05673
    """
    
    @staticmethod
    def calculate_bits(rows: int, cols: int, layers: int) -> int:
        """
        Calculate minimum bits needed for lossless encoding.
        
        Args:
            rows, cols, layers: Maximum dimensions
            
        Returns:
            Bits needed per dimension (same for all 3 for simplicity)
        """
        max_dim = max(rows, cols, layers)
        return max(1, int(math.ceil(math.log2(max_dim))))
    
    def __init__(self, bits: int):
        """
        Initialize encoder with precision.
        
        Args:
            bits: Precision bits per dimension (use calculate_bits() for dynamic)
        """
        if bits < 1 or bits > 21:
            raise ValueError(f"Bits must be in range [1, 21], got {bits}")
        
        self.bits = bits
        self.max_coord = (1 << bits) - 1
    
    def encode_batch(self, positions: np.ndarray) -> np.ndarray:
        """
        Encode integer positions to Hilbert indices (VECTORIZED).
        
        Args:
            positions: int array shape (N, 3) with (i, j, layer) coordinates
            
        Returns:
            int64 array shape (N,) with Hilbert indices
        """
        if positions.ndim != 2 or positions.shape[1] != 3:
            raise ValueError(f"Expected (N, 3) array, got shape {positions.shape}")
        
        # Ensure integer type
        if not np.issubdtype(positions.dtype, np.integer):
            raise TypeError(f"Positions must be integers, got {positions.dtype}")
        
        return self._encode_vectorized(positions[:, 0], positions[:, 1], positions[:, 2])
    
    def encode_semantic(self, coords: np.ndarray) -> np.ndarray:
        """
        Encode semantic landmark coordinates (floats in [0, 1]) to Hilbert indices.
        
        Used for atom.spatial_key M coordinate from landmark projection.
        
        Args:
            coords: float array shape (N, 3) with (x, y, z) in [0, 1]
            
        Returns:
            int64 array shape (N,) with Hilbert indices
        """
        if coords.ndim != 2 or coords.shape[1] != 3:
            raise ValueError(f"Expected (N, 3) array, got shape {coords.shape}")
        
        # Normalize to integer grid
        coords_clipped = np.clip(coords, 0, 1)
        coords_scaled = coords_clipped * self.max_coord
        coords_int = np.round(coords_scaled).astype(np.int64)
        
        return self._encode_vectorized(coords_int[:, 0], coords_int[:, 1], coords_int[:, 2])
    
    def _encode_vectorized(self, xi: np.ndarray, yi: np.ndarray, zi: np.ndarray) -> np.ndarray:
        """
        Vectorized Hilbert encoding for integer coordinates.
        
        Args:
            xi, yi, zi: Integer coordinate arrays
            
        Returns:
            Hilbert indices as int64 array
        """
        # Clip to valid range
        xi = np.clip(xi, 0, self.max_coord)
        yi = np.clip(yi, 0, self.max_coord)
        zi = np.clip(zi, 0, self.max_coord)
        
        n = len(xi)
        hilbert = np.zeros(n, dtype=np.int64)
        rotation = np.zeros(n, dtype=np.int64)
        
        # Encode level by level (from most significant bit to least)
        for level in range(self.bits - 1, -1, -1):
            bit = 1 << level
            
            # Extract octant bits for this level
            quadrant = np.zeros(n, dtype=np.int64)
            quadrant |= ((xi & bit) != 0).astype(np.int64) << 2  # X bit -> bit 2
            quadrant |= ((yi & bit) != 0).astype(np.int64) << 1  # Y bit -> bit 1
            quadrant |= ((zi & bit) != 0).astype(np.int64)       # Z bit -> bit 0
            
            # Apply rotation (XOR with current rotation state)
            quadrant = quadrant ^ rotation
            
            # Update Hilbert index
            hilbert = (hilbert << 3) | quadrant
            
            # Calculate rotation for next level
            rotation = (rotation + (quadrant << 3)) & 7
        
        return hilbert
    
    def encode_single(
        self,
        i: int,
        j: int,
        layer_index: int,
        shape: Tuple[int, int],
        num_layers: int
    ) -> int:
        """
        Encode single tensor position to Hilbert index.
        
        Args:
            i, j: Position in tensor (row, col)
            layer_index: Current layer index (0-based)
            shape: (rows, cols) tensor shape
            num_layers: Total number of layers
            
        Returns:
            63-bit Hilbert index
        """
        positions = np.array([[i, j]])
        return int(self.encode_batch(positions, shape, layer_index, num_layers)[0])
    
    def encode_via_database(
        self,
        conn: psycopg.Connection,
        x: float,
        y: float,
        z: float
    ) -> int:
        """
        Encode using PostgreSQL hilbert_encode_3d() function.
        
        Useful for validation or when database connection is already available.
        
        Args:
            conn: psycopg3 connection
            x, y, z: Normalized coordinates [0.0, 1.0]
            
        Returns:
            63-bit Hilbert index
        """
        with conn.cursor() as cur:
            cur.execute(
                "SELECT hilbert_encode_3d(%s::float8, %s::float8, %s::float8, %s::int)",
                (x, y, z, self.bits)
            )
            result = cur.fetchone()
            return result[0] if result else 0


class HilbertDecoder:
    """
    Decode 1D Hilbert indices back to 3D integer positions.
    
    Lossless inverse of HilbertEncoder for integer coordinates.
    """
    
    def __init__(self, bits: int):
        """
        Initialize decoder with precision.
        
        Args:
            bits: Must match encoder precision
        """
        if bits < 1 or bits > 21:
            raise ValueError(f"Bits must be in range [1, 21], got {bits}")
        
        self.bits = bits
        self.max_coord = (1 << bits) - 1
    
    def decode_batch(self, hilbert_indices: np.ndarray) -> np.ndarray:
        """
        Decode Hilbert indices to integer positions.
        
        Args:
            hilbert_indices: int64 array shape (N,)
            
        Returns:
            int64 array shape (N, 3) with (i, j, layer) coordinates
        """
        n = len(hilbert_indices)
        x = np.zeros(n, dtype=np.int64)
        y = np.zeros(n, dtype=np.int64)
        z = np.zeros(n, dtype=np.int64)
        
        rotation = np.zeros(n, dtype=np.int64)
        
        # Decode level by level (from most significant bit to least)
        for level in range(self.bits - 1, -1, -1):
            # Extract 3-bit quadrant for this level
            shift = level * 3
            quadrant = (hilbert_indices >> shift) & 7
            
            # Reverse rotation (XOR, same as encoding)
            quadrant = quadrant ^ rotation
            
            # Extract coordinate bits using bitwise OR (not addition)
            bit_val = np.int64(1) << level
            x |= ((quadrant & 4) != 0).astype(np.int64) * bit_val
            y |= ((quadrant & 2) != 0).astype(np.int64) * bit_val
            z |= ((quadrant & 1) != 0).astype(np.int64) * bit_val
            
            # Update rotation for next level
            rotation = (rotation + (quadrant << 3)) & 7
        
        # Return integer coordinates directly (no denormalization)
        return np.column_stack([x, y, z])
    
    def decode_via_database(
        self,
        conn: psycopg.Connection,
        hilbert_index: int
    ) -> Tuple[float, float, float]:
        """
        Decode using PostgreSQL hilbert_decode_3d() function.
        
        Args:
            conn: psycopg3 connection
            hilbert_index: 63-bit Hilbert index
            
        Returns:
            (x, y, z) normalized coordinates [0.0, 1.0]
        """
        with conn.cursor() as cur:
            cur.execute(
                "SELECT hilbert_decode_3d(%s::bigint, %s::int)",
                (hilbert_index, self.bits)
            )
            result = cur.fetchone()
            if result and result[0]:
                # Result is JSONB: {"x": 0.5, "y": 0.3, "z": 0.1}
                coords = result[0]
                return coords.get('x', 0.0), coords.get('y', 0.0), coords.get('z', 0.0)
            return 0.0, 0.0, 0.0


def validate_roundtrip(bits: int = 21, num_samples: int = 1000):
    """
    Validate that encoding and decoding are inverse operations.
    
    Args:
        bits: Precision bits to test
        num_samples: Number of random positions to test
    """
    encoder = HilbertEncoder(bits=bits)
    decoder = HilbertDecoder(bits=bits)
    
    # Generate random positions
    np.random.seed(42)
    positions = np.random.randint(0, 768, size=(num_samples, 2))
    shape = (768, 768)
    layer_index = 5
    num_layers = 32
    
    # Encode
    hilbert_indices = encoder.encode_batch(positions, shape, layer_index, num_layers)
    
    # Decode
    i_decoded, j_decoded, layer_decoded = decoder.decode_batch(
        hilbert_indices, shape, num_layers
    )
    
    # Verify
    i_match = np.all(i_decoded == positions[:, 0])
    j_match = np.all(j_decoded == positions[:, 1])
    layer_match = np.all(layer_decoded == layer_index)
    
    print(f"Roundtrip validation (bits={bits}, samples={num_samples}):")
    print(f"  I positions match: {i_match}")
    print(f"  J positions match: {j_match}")
    print(f"  Layer indices match: {layer_match}")
    
    if i_match and j_match and layer_match:
        print("  ✓ Roundtrip validation PASSED")
    else:
        print("  ✗ Roundtrip validation FAILED")
        # Show first mismatch
        for idx in range(min(10, num_samples)):
            if (i_decoded[idx] != positions[idx, 0] or 
                j_decoded[idx] != positions[idx, 1] or 
                layer_decoded[idx] != layer_index):
                print(f"    Mismatch at {idx}: "
                      f"Original=({positions[idx, 0]}, {positions[idx, 1]}, {layer_index}), "
                      f"Decoded=({i_decoded[idx]}, {j_decoded[idx]}, {layer_decoded[idx]})")


if __name__ == "__main__":
    validate_roundtrip()
