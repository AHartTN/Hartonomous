"""Encode 3D coordinates to Hilbert curve index."""

import numpy as np

from .hilbert_3d_encode_impl import hilbert_3d_encode


def encode_hilbert_3d(x: float, y: float, z: float, order: int = 21) -> int:
    """Encode 3D coordinates [0,1] cubed to Hilbert curve index."""
    max_coord = (1 << order) - 1
    ix = int(np.clip(x * max_coord, 0, max_coord))
    iy = int(np.clip(y * max_coord, 0, max_coord))
    iz = int(np.clip(z * max_coord, 0, max_coord))
    
    return hilbert_3d_encode(ix, iy, iz, order)
