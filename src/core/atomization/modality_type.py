"""
Modality type enumeration.

Data modality types for atomization.
"""

from enum import IntEnum


class ModalityType(IntEnum):
    """Data modality types."""

    UNKNOWN = 0
    MODEL_WEIGHT = 1
    MODEL_BIAS = 2
    MODEL_ACTIVATION = 3
    IMAGE_PIXEL = 10
    IMAGE_FEATURE = 11
    TEXT_TOKEN = 20
    TEXT_EMBEDDING = 21
    AUDIO_SAMPLE = 30
    AUDIO_FEATURE = 31
    VIDEO_FRAME = 40
    CODE_TOKEN = 50
    CODE_AST = 51
    STRUCTURED_FIELD = 60
