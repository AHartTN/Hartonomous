"""
Ingestion and atomization pipeline.
Breaks down any input into atomic units for storage in the 3D spatial system.
"""

from .atomizer import Atomizer
from .parsers import (
    TextParser,
    ImageParser,
    AudioParser,
    VideoParser,
    DocumentParser,
    CodeParser,
)

__all__ = [
    'Atomizer',
    'TextParser',
    'ImageParser',
    'AudioParser',
    'VideoParser',
    'DocumentParser',
    'CodeParser',
]
