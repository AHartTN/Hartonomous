"""Landmark projection - convenience imports."""

from .landmark import LandmarkProjector, LandmarkPosition

# Alias for compatibility
Landmark = LandmarkPosition

__all__ = ['LandmarkProjector', 'Landmark', 'LandmarkPosition']
