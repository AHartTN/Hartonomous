"""Spatial indexing and projection system for Hartonomous."""

from .landmark_projection import (
    compute_position,
    infer_specificity,
    compute_distance,
    get_nearest_category,
    get_all_landmarks,
    MODALITY_LANDMARKS,
    CATEGORY_LANDMARKS,
    SPECIFICITY_LANDMARKS,
)
from .hilbert_curve import encode_hilbert_3d, decode_hilbert_3d

__all__ = [
    'compute_position',
    'infer_specificity',
    'compute_distance',
    'get_nearest_category',
    'get_all_landmarks',
    'encode_hilbert_3d',
    'decode_hilbert_3d',
    'MODALITY_LANDMARKS',
    'CATEGORY_LANDMARKS',
    'SPECIFICITY_LANDMARKS',
]
