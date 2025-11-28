"""Compute Euclidean distance between two positions."""

from typing import Tuple

import numpy as np


def compute_distance(
    pos1: Tuple[float, float, float], pos2: Tuple[float, float, float]
) -> float:
    """Compute Euclidean distance between two 3D positions."""
    return np.sqrt(sum((a - b) ** 2 for a, b in zip(pos1, pos2)))
