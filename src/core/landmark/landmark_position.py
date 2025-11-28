"""
Landmark position dataclass.

Fixed 3D position for a landmark in semantic space.
"""

import numpy as np
from typing import Tuple
from dataclasses import dataclass

from .landmark_type import LandmarkType


@dataclass
class LandmarkPosition:
    """Fixed 3D position for a landmark."""
    x: float
    y: float
    z: float
    landmark_type: LandmarkType
    
    def to_point(self) -> Tuple[float, float, float]:
        """Return as (x, y, z) tuple."""
        return (self.x, self.y, self.z)
    
    def distance_to(self, other: 'LandmarkPosition') -> float:
        """Euclidean distance to another position."""
        dx = self.x - other.x
        dy = self.y - other.y
        dz = self.z - other.z
        return np.sqrt(dx*dx + dy*dy + dz*dz)
