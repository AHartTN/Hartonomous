"""
Landmark registry.

Registry of fixed landmarks that define the 3D semantic space.
These landmarks NEVER change - they are the foundation of spatial semantics.
"""

from typing import Dict, List, Tuple

from .landmark_position import LandmarkPosition
from .landmark_type import LandmarkType


class LandmarkRegistry:
    """
    Registry of fixed landmarks.

    These landmarks NEVER change - they define the semantic space.
    New atoms are positioned relative to these landmarks.
    """

    # Fixed landmark positions in normalized [0, 1]^3 space
    LANDMARKS: Dict[LandmarkType, Tuple[float, float, float]] = {
        # Modality landmarks (spread along X)
        LandmarkType.MODALITY_TEXT: (0.15, 0.5, 0.5),
        LandmarkType.MODALITY_IMAGE: (0.30, 0.5, 0.5),
        LandmarkType.MODALITY_AUDIO: (0.45, 0.5, 0.5),
        LandmarkType.MODALITY_VIDEO: (0.60, 0.5, 0.5),
        LandmarkType.MODALITY_CODE: (0.75, 0.5, 0.5),
        LandmarkType.MODALITY_STRUCTURED: (0.90, 0.5, 0.5),
        LandmarkType.MODALITY_MODEL: (0.05, 0.5, 0.5),
        # Category landmarks (spread along Y)
        LandmarkType.CATEGORY_LITERAL: (0.5, 0.15, 0.5),
        LandmarkType.CATEGORY_SYMBOLIC: (0.5, 0.30, 0.5),
        LandmarkType.CATEGORY_ABSTRACT: (0.5, 0.55, 0.5),
        LandmarkType.CATEGORY_RELATIONAL: (0.5, 0.75, 0.5),
        LandmarkType.CATEGORY_COMPOSITIONAL: (0.5, 0.90, 0.5),
        # Specificity landmarks (spread along Z)
        LandmarkType.SPECIFICITY_ATOMIC: (0.5, 0.5, 0.15),
        LandmarkType.SPECIFICITY_COMPOUND: (0.5, 0.5, 0.40),
        LandmarkType.SPECIFICITY_AGGREGATE: (0.5, 0.5, 0.70),
        LandmarkType.SPECIFICITY_UNIVERSAL: (0.5, 0.5, 0.95),
    }

    @classmethod
    def get_landmark(cls, landmark_type: LandmarkType) -> LandmarkPosition:
        """Get fixed landmark position."""
        coords = cls.LANDMARKS[landmark_type]
        return LandmarkPosition(*coords, landmark_type)

    @classmethod
    def get_all_landmarks(cls) -> List[LandmarkPosition]:
        """Get all landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys()]

    @classmethod
    def get_modality_landmarks(cls) -> List[LandmarkPosition]:
        """Get modality-specific landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys() if 1 <= lt <= 9]

    @classmethod
    def get_category_landmarks(cls) -> List[LandmarkPosition]:
        """Get category landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys() if 10 <= lt <= 19]

    @classmethod
    def get_specificity_landmarks(cls) -> List[LandmarkPosition]:
        """Get specificity landmarks."""
        return [cls.get_landmark(lt) for lt in cls.LANDMARKS.keys() if 20 <= lt <= 29]
