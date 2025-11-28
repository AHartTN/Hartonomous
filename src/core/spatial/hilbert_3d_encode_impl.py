"""Hilbert 3D encode implementation."""

from .gray_encode_3bit import gray_encode_3bit


def hilbert_3d_encode(x: int, y: int, z: int, order: int) -> int:
    """Pure Python implementation of 3D Hilbert curve encoding."""
    index = 0
    
    for i in range(order - 1, -1, -1):
        xi = (x >> i) & 1
        yi = (y >> i) & 1
        zi = (z >> i) & 1
        
        bits = (xi << 2) | (yi << 1) | zi
        bits = gray_encode_3bit(bits)
        
        index = (index << 3) | bits
    
    return index
