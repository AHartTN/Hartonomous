"""
Hilbert Curve Encoding for 3D Tensor Position Mapping

Provides both database-backed and pure NumPy implementations for encoding
tensor positions (i,j,layer) into 1D Hilbert indices that preserve spatial locality.

This enables:
1. RLE compression - consecutive Hilbert indices = spatial runs
2. Cache-friendly access - nearby indices = nearby memory locations
3. Range queries - Hilbert range ≈ spatial bounding box
"""

import numpy as np
from typing import Tuple, Optional
import psycopg


class HilbertEncoder:
    """
    Encode 3D tensor positions to 1D Hilbert indices.
    
    Uses rotation-based algorithm from:
    "Efficient 3D Hilbert Curve Encoding and Decoding Algorithms"
    https://arxiv.org/pdf/2308.05673
    """
    
    def __init__(self, bits: int = 21):
        """
        Initialize encoder with precision.
        
        Args:
            bits: Precision bits per dimension (default 21 = 2^21 ≈ 2M resolution)
                  Total index space: 2^(3*bits) = 2^63 for bits=21
        """
        if bits < 1 or bits > 21:
            raise ValueError(f"Bits must be in range [1, 21], got {bits}")
        
        self.bits = bits
        self.max_coord = (1 << bits) - 1
    
    def encode_batch(
        self,
        positions: np.ndarray,
        shape: Tuple[int, int],
        layer_index: int,
        num_layers: int
    ) -> np.ndarray:
        """
        Encode batch of tensor positions to Hilbert indices.
        
        Args:
            positions: (N, 2) array of [i, j] positions in tensor
            shape: (rows, cols) tensor shape for normalization
            layer_index: Current layer index (0-based)
            num_layers: Total number of layers in model
            
        Returns:
            (N,) array of 63-bit Hilbert indices
            
        Example:
            # Encode positions in 768x768 tensor, layer 5 of 32
            positions = np.array([[0, 0], [0, 1], [1, 0]])
            hilbert_indices = encoder.encode_batch(
                positions, 
                shape=(768, 768),
                layer_index=5,
                num_layers=32
            )
        """
        if positions.shape[1] != 2:
            raise ValueError(f"Expected positions shape (N, 2), got {positions.shape}")
        
        rows, cols = shape
        
        # Normalize to [0, 1] - use max coordinate for normalization
        x = positions[:, 0].astype(np.float64) / max(rows - 1, 1)
        y = positions[:, 1].astype(np.float64) / max(cols - 1, 1)
        z = np.full(len(positions), layer_index / max(num_layers - 1, 1), dtype=np.float64)
        
        # Vectorized encoding
        return self._encode_vectorized(x, y, z)
    
    def _encode_vectorized(
        self, 
        x: np.ndarray, 
        y: np.ndarray, 
        z: np.ndarray
    ) -> np.ndarray:
        """
        Vectorized Hilbert encoding using NumPy.
        
        Args:
            x, y, z: Normalized coordinates [0.0, 1.0]
            
        Returns:
            Hilbert indices as int64 array
        """
        # Validate ranges
        if np.any((x < 0) | (x > 1) | (y < 0) | (y > 1) | (z < 0) | (z > 1)):
            raise ValueError("All coordinates must be in range [0.0, 1.0]")
        
        # Convert to integer coordinates
        xi = (x * self.max_coord).astype(np.int64)
        yi = (y * self.max_coord).astype(np.int64)
        zi = (z * self.max_coord).astype(np.int64)
        
        n = len(x)
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
    Decode 1D Hilbert indices back to 3D positions.
    
    Used for tensor reconstruction: Hilbert range -> position arrays.
    """
    
    def __init__(self, bits: int = 21):
        """
        Initialize decoder with precision.
        
        Args:
            bits: Must match encoder precision
        """
        if bits < 1 or bits > 21:
            raise ValueError(f"Bits must be in range [1, 21], got {bits}")
        
        self.bits = bits
        self.max_coord = (1 << bits) - 1
    
    def decode_batch(
        self,
        hilbert_indices: np.ndarray,
        shape: Tuple[int, int],
        num_layers: int
    ) -> Tuple[np.ndarray, np.ndarray, np.ndarray]:
        """
        Decode batch of Hilbert indices to tensor positions.
        
        Args:
            hilbert_indices: (N,) array of Hilbert indices
            shape: (rows, cols) tensor shape for denormalization
            num_layers: Total number of layers
            
        Returns:
            (i_positions, j_positions, layer_indices) as separate arrays
            
        Example:
            # Decode Hilbert range [1000, 1005] to positions
            hilbert_range = np.arange(1000, 1006)
            i_pos, j_pos, layers = decoder.decode_batch(
                hilbert_range,
                shape=(768, 768),
                num_layers=32
            )
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
        
        rows, cols = shape
        
        # Normalize to [0.0, 1.0] first
        x_norm = x.astype(np.float64) / self.max_coord
        y_norm = y.astype(np.float64) / self.max_coord
        z_norm = z.astype(np.float64) / self.max_coord
        
        # Denormalize to tensor coordinates (reverse of normalization in encode)
        # Use round() to handle precision loss from 21-bit encoding
        i_positions = np.round(x_norm * max(rows - 1, 1)).astype(np.int32)
        j_positions = np.round(y_norm * max(cols - 1, 1)).astype(np.int32)
        layer_indices = np.round(z_norm * max(num_layers - 1, 1)).astype(np.int32)
        
        # Clamp to valid ranges
        i_positions = np.clip(i_positions, 0, rows - 1)
        j_positions = np.clip(j_positions, 0, cols - 1)
        layer_indices = np.clip(layer_indices, 0, num_layers - 1)
        
        return i_positions, j_positions, layer_indices
    
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
