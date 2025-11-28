"""
Landmark type enumeration.

Fixed landmark types defining the 3D semantic space.
These are CONSTANTS, not learned parameters.
"""

from enum import IntEnum


class LandmarkType(IntEnum):
    """
    Fixed landmark types defining the 3D semantic space.
    These are CONSTANTS, not learned parameters.
    """
    # Modality landmarks (X-axis influence)
    MODALITY_TEXT = 1
    MODALITY_IMAGE = 2
    MODALITY_AUDIO = 3
    MODALITY_VIDEO = 4
    MODALITY_CODE = 5
    MODALITY_STRUCTURED = 6
    MODALITY_MODEL = 7
    
    # Category landmarks (Y-axis influence)
    CATEGORY_LITERAL = 10
    CATEGORY_SYMBOLIC = 11
    CATEGORY_ABSTRACT = 12
    CATEGORY_RELATIONAL = 13
    CATEGORY_COMPOSITIONAL = 14
    
    # Specificity landmarks (Z-axis influence)
    SPECIFICITY_ATOMIC = 20
    SPECIFICITY_COMPOUND = 21
    SPECIFICITY_AGGREGATE = 22
    SPECIFICITY_UNIVERSAL = 23
