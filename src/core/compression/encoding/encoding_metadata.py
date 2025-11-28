"""Encoding metadata dataclass."""

from dataclasses import dataclass
from typing import Tuple

import numpy as np


@dataclass
class EncodingMetadata:
    """Metadata about applied encodings for reconstruction."""

    rle_applied: bool
    sparse_applied: bool
    sparse_threshold: float
    original_shape: Tuple[int, ...]
    dtype: np.dtype
