"""Landmark projection - convenience imports."""

from .landmark import (
    LandmarkType,
    LandmarkPosition,
    LandmarkRegistry,
    HilbertEncoder,
    LandmarkProjector,
)

# Alias for compatibility
Landmark = LandmarkPosition

__all__ = [
    'LandmarkType',
    'LandmarkPosition',
    'LandmarkRegistry',
    'HilbertEncoder',
    'LandmarkProjector',
    'Landmark',
]
