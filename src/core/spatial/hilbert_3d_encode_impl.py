"""Hilbert 3D encode - rotation-based algorithm matching SQL implementation."""


def hilbert_3d_encode(x: int, y: int, z: int, order: int) -> int:
    """
    Encode 3D coordinates to Hilbert index using rotation-based algorithm.

    Based on "Efficient 3D Hilbert Curve Encoding and Decoding Algorithms"
    Reference: https://arxiv.org/pdf/2308.05673

    This matches the SQL implementation in schema/functions/hilbert_encoding.sql

    Args:
        x, y, z: Integer coordinates in range [0, 2^order - 1]
        order: Number of bits per dimension (precision)

    Returns:
        63-bit Hilbert index (3 * order bits)
    """
    if order <= 0:
        return 0

    hilbert = 0
    rotation = 0

    # Process from most significant bit to least significant
    for level in range(order - 1, -1, -1):
        bit = 1 << level

        # Extract octant bits at this level
        quadrant = 0
        if (x & bit) != 0:
            quadrant |= 4
        if (y & bit) != 0:
            quadrant |= 2
        if (z & bit) != 0:
            quadrant |= 1

        # Apply rotation based on current state (XOR with rotation)
        quadrant = quadrant ^ rotation

        # Append to Hilbert index (shift left 3 bits, add quadrant)
        hilbert = (hilbert << 3) | quadrant

        # Calculate rotation for next level
        rotation = (rotation + (quadrant << 3)) & 7

    return hilbert
