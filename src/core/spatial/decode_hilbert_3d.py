"""Decode Hilbert index to 3D coordinates."""

from typing import Tuple

from .hilbert_3d_decode_impl import hilbert_3d_decode


def decode_hilbert_3d(index: int, order: int = 21) -> Tuple[float, float, float]:
    """Decode Hilbert curve index to 3D coordinates."""
    max_coord = (1 << order) - 1
    ix, iy, iz = hilbert_3d_decode(index, order)
    
    return (
        ix / max_coord,
        iy / max_coord,
        iz / max_coord
    )
