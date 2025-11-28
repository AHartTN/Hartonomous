"""Hilbert 3D decode implementation."""

from typing import Tuple

from .gray_decode_3bit import gray_decode_3bit


def hilbert_3d_decode(index: int, order: int) -> Tuple[int, int, int]:
    """Pure Python implementation of 3D Hilbert curve decoding."""
    x, y, z = 0, 0, 0
    
    for i in range(order):
        shift = 3 * (order - 1 - i)
        bits = (index >> shift) & 0x7
        
        bits = gray_decode_3bit(bits)
        
        xi = (bits >> 2) & 1
        yi = (bits >> 1) & 1
        zi = bits & 1
        
        x = (x << 1) | xi
        y = (y << 1) | yi
        z = (z << 1) | zi
    
    return (x, y, z)
