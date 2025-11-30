"""
AtomLocator: Deterministic Semantic Coordinate Mapping

Maps atom values to fixed 3D semantic coordinates via SHA-256 hashing.
Ensures atoms always live at the SAME coordinate (deterministic).

NOW DELEGATES TO: spatial_utils for all coordinate operations (DRY principle).

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from typing import Tuple

from . import spatial_utils


class AtomLocator:
    """
    Maps atom values to deterministic semantic coordinates.

    The same atom value always produces the same (x, y, z) coordinate.
    This is the foundation of content-addressable geometry.

    REFACTORED: All spatial operations now delegate to spatial_utils module.
    """

    def __init__(self, coordinate_range: float = 1e6):
        """
        Initialize AtomLocator.

        Args:
            coordinate_range: Max coordinate value (default 1M for PostGIS compatibility)
        """
        self.coordinate_range = coordinate_range

    def locate(self, value: bytes) -> Tuple[float, float, float]:
        """
        Compute deterministic semantic coordinate for atom value.

        Delegates to spatial_utils.hash_to_coordinate().

        Args:
            value: Atom value (raw bytes)

        Returns:
            (x, y, z) coordinate tuple
        """
        return spatial_utils.hash_to_coordinate(value, self.coordinate_range)

    def locate_multiple(self, values: list[bytes]) -> list[Tuple[float, float, float]]:
        """
        Compute coordinates for multiple atoms (vectorized).

        Args:
            values: List of atom values

        Returns:
            List of (x, y, z) coordinate tuples
        """
        return [self.locate(value) for value in values]

    def compute_hilbert_index(
        self, x: float, y: float, z: float, bits: int = 21
    ) -> int:
        """
        Compute Hilbert curve index for M coordinate.

        Delegates to spatial_utils.compute_hilbert_index().

        Args:
            x, y, z: Semantic coordinates
            bits: Bits per dimension (default 21 = 63 bits total for BIGINT)

        Returns:
            Hilbert index (M coordinate)
        """
        return spatial_utils.compute_hilbert_index(
            x, y, z, coordinate_range=self.coordinate_range, bits=bits
        )

    def locate_with_hilbert(self, value: bytes) -> Tuple[float, float, float, int]:
        """
        Compute complete (x, y, z, m) for atom value.

        Convenience method that combines coordinate and Hilbert calculation.

        Args:
            value: Atom value (raw bytes)

        Returns:
            (x, y, z, m) tuple
        """
        return spatial_utils.locate_atom(value, self.coordinate_range)
