"""Spatial indexing and projection system for Hartonomous."""

from .hilbert_curve import decode_hilbert_3d, encode_hilbert_3d
from .landmark_projection import (CATEGORY_LANDMARKS, MODALITY_LANDMARKS,
                                  SPECIFICITY_LANDMARKS, compute_distance,
                                  compute_position, get_all_landmarks,
                                  get_nearest_category, infer_specificity)

__all__ = [
    "compute_position",
    "infer_specificity",
    "compute_distance",
    "get_nearest_category",
    "get_all_landmarks",
    "encode_hilbert_3d",
    "decode_hilbert_3d",
    "MODALITY_LANDMARKS",
    "CATEGORY_LANDMARKS",
    "SPECIFICITY_LANDMARKS",
]
