"""Compute spatial position for an atom."""

import hashlib
import numpy as np
from typing import Tuple, Optional

from .modality_landmarks import MODALITY_LANDMARKS
from .category_landmarks import CATEGORY_LANDMARKS
from .specificity_landmarks import SPECIFICITY_LANDMARKS
from .hilbert_curve import encode_hilbert_3d


def compute_position(
    modality: str,
    category: str,
    specificity: Optional[str] = None,
    identifier: Optional[str] = None,
    hilbert_order: int = 21
) -> Tuple[float, float, float, int]:
    """Compute 3D spatial position and Hilbert index for an atom."""
    x = MODALITY_LANDMARKS.get(modality.lower(), 0.5)
    y = CATEGORY_LANDMARKS.get(category.lower(), 0.5)
    z = SPECIFICITY_LANDMARKS.get(specificity.lower(), 0.5) if specificity else 0.5
    
    if identifier:
        hash_bytes = hashlib.sha256(identifier.encode('utf-8')).digest()
        
        x += (hash_bytes[0] % 100 - 50) / 1000.0
        y += (hash_bytes[1] % 100 - 50) / 1000.0
        z += (hash_bytes[2] % 100 - 50) / 1000.0
        
        x = np.clip(x, 0.0, 1.0)
        y = np.clip(y, 0.0, 1.0)
        z = np.clip(z, 0.0, 1.0)
    
    hilbert_index = encode_hilbert_3d(x, y, z, hilbert_order)
    
    return (x, y, z, hilbert_index)
