"""Get all landmark coordinates."""

from typing import Dict, Tuple

from .category_landmarks import CATEGORY_LANDMARKS
from .modality_landmarks import MODALITY_LANDMARKS
from .specificity_landmarks import SPECIFICITY_LANDMARKS


def get_all_landmarks() -> Dict[str, Tuple[float, float, float]]:
    """Get all landmark coordinates for visualization/debugging."""
    landmarks = {}

    for mod_key, mod_val in MODALITY_LANDMARKS.items():
        for cat_key, cat_val in CATEGORY_LANDMARKS.items():
            for spec_key, spec_val in SPECIFICITY_LANDMARKS.items():
                key = f"{mod_key}:{cat_key}:{spec_key}"
                landmarks[key] = (mod_val, cat_val, spec_val)

    return landmarks
