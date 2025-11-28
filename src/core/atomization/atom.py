"""
Atom dataclass.

Atomic data unit - fundamental storage unit.
Maximum 64 bytes total.
"""

from dataclasses import dataclass
from typing import Any, Dict

from .modality_type import ModalityType


@dataclass
class Atom:
    """
    Atomic data unit - fundamental storage unit.
    Maximum 64 bytes total.
    """

    atom_id: bytes
    modality: ModalityType
    data: bytes
    compression_type: int
    metadata: Dict[str, Any]

    def __post_init__(self):
        """Validate atom size constraint."""
        total_size = len(self.atom_id) + 1 + len(self.data) + 1
        if total_size > 64:
            raise ValueError(f"Atom exceeds 64 bytes: {total_size} bytes")

    @property
    def content_hash(self) -> str:
        """Content-addressable hash."""
        return self.atom_id.hex()
