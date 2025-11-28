"""Modality landmarks for X-axis positioning."""

from typing import Dict

MODALITY_LANDMARKS: Dict[str, float] = {
    'code': 0.1,
    'text': 0.3,
    'numeric': 0.4,
    'image': 0.5,
    'audio': 0.7,
    'video': 0.9,
    'binary': 0.95,
    'graph': 0.25,
    'document': 0.35,
    'structured': 0.45,
}
