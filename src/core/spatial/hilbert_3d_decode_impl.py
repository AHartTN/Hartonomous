"""Hilbert 3D decode - inverse rotation-based algorithm matching SQL implementation."""

from typing import Tuple


def hilbert_3d_decode(index: int, order: int) -> Tuple[int, int, int]:
    """
    Decode Hilbert index to 3D coordinates - inverse of encoding.

    Based on "Efficient 3D Hilbert Curve Encoding and Decoding Algorithms"
    Reference: https://arxiv.org/pdf/2308.05673
    
    This matches the SQL implementation in schema/functions/hilbert_encoding.sql
    
    Args:
        index: Hilbert index (3 * order bits)
        order: Number of bits per dimension (precision)
    
    Returns:
        (x, y, z) integer coordinates in range [0, 2^order - 1]
    """
    if order <= 0:
        return (0, 0, 0)

    x = y = z = 0
    rotation = 0
    
    # Process from most significant to least significant bits
    for level in range(order - 1, -1, -1):
        shift = level * 3
        
        # Extract 3-bit quadrant from index
        quadrant = (index >> shift) & 7
        
        # Apply inverse rotation (XOR with rotation state)
        quadrant = quadrant ^ rotation
        
        bit = 1 << level
        
        # Extract coordinate bits from quadrant
        if (quadrant & 4) != 0:
            x |= bit
        if (quadrant & 2) != 0:
            y |= bit
        if (quadrant & 1) != 0:
            z |= bit
        
        # Update rotation state for next level
        rotation = (rotation + (quadrant << 3)) & 7

    return (x, y, z)

