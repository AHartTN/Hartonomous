"""Specificity landmarks for Z-axis positioning."""

from typing import Dict

SPECIFICITY_LANDMARKS: Dict[str, float] = {
    "abstract": 0.1,
    "generic": 0.3,
    "concrete": 0.5,
    "instance": 0.7,
    "literal": 0.9,
}
