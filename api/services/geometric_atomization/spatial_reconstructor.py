"""SpatialReconstructor: Reconstruct Content from Trajectories

Parses LINESTRING trajectories to reconstruct original content.
Achieves bit-perfect reconstruction by walking coordinates and looking up atoms.

Architecture:
1. Parse LINESTRING ZM from database
2. Extract (x,y,z,m) points in sequence
3. For each point: query atom table for atom at that coordinate
4. Rebuild original content from atom values
5. Use M coordinate for reshaping (e.g., tensor dimensions)

This is the inverse of TrajectoryBuilder - proves the architecture works.

REFACTORED: Uses spatial_utils for safe SQL generation.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import re
from typing import List, Optional, Tuple

import numpy as np

from . import spatial_utils


class SpatialReconstructor:
    """
    Reconstructs content from LINESTRING trajectories.

    Walks trajectory points, looks up atoms at coordinates,
    rebuilds original sequences bit-perfect.
    """

    def __init__(self, db_connection=None):
        """
        Initialize SpatialReconstructor.

        Args:
            db_connection: Database connection for atom lookups (optional)
        """
        self.db = db_connection

    def parse_wkt(self, wkt: str) -> List[Tuple[float, float, float, float]]:
        """
        Parse LINESTRING ZM WKT into list of (x, y, z, m) points.

        Args:
            wkt: WKT string like "LINESTRING ZM (x1 y1 z1 m1, x2 y2 z2 m2, ...)"

        Returns:
            List of (x, y, z, m) tuples
        """
        # Remove "LINESTRING ZM (" prefix and ")" suffix
        match = re.match(r"LINESTRING\s*ZM\s*\((.*)\)", wkt, re.IGNORECASE)
        if not match:
            raise ValueError(f"Invalid LINESTRING ZM WKT: {wkt}")

        points_str = match.group(1)

        # Split by comma to get individual points
        point_strs = points_str.split(",")

        points = []
        for point_str in point_strs:
            # Split by whitespace to get x, y, z, m
            coords = point_str.strip().split()
            if len(coords) != 4:
                raise ValueError(
                    f"Expected 4 coordinates (x,y,z,m), got {len(coords)}: {point_str}"
                )

            x, y, z, m = map(float, coords)
            points.append((x, y, z, m))

        return points

    async def lookup_atom_at_coordinate(
        self,
        x: float,
        y: float,
        z: float,
        tolerance: float = 1e-6,
        expand_compositions: bool = True,
    ) -> Optional[bytes]:
        """
        Look up atom at specific coordinate.

        Args:
            x, y, z: Coordinate
            tolerance: Distance tolerance for matching (handles floating point imprecision)
            expand_compositions: If True, recursively expand composition_ids

        Returns:
            Atom value (bytes) or None if not found
        """
        if self.db is None:
            raise ValueError("Database connection required for atom lookup")

        # Use spatial_utils for safe point creation (3D only, M ignored for distance)
        point_sql, point_params = spatial_utils.make_point_3d(x, y, z)

        # Query: Find atom within tolerance of coordinate
        # Uses ST_DWithin for spatial proximity in XYZ only (M ignored for spatial ops)
        query = f"""
            SELECT atom_value, composition_ids, canonical_text
            FROM atom
            WHERE ST_DWithin(
                spatial_key,
                {point_sql},
                %s
            )
            ORDER BY ST_Distance(spatial_key, {point_sql})
            LIMIT 1
        """

        from ..utils import query_one

        row = await query_one(self.db, query, (*point_params, tolerance, *point_params))

        if row is None:
            return None

            # If composition and expansion requested, recursively expand
            if composition_ids is not None and expand_compositions:
                return await self._expand_composition(composition_ids)

            # Fallback: return canonical text as bytes
            if canonical_text:
                return canonical_text.encode("utf-8")

            return None

    async def _expand_composition(self, composition_ids: List[int]) -> bytes:
        """
        Recursively expand composition to get final value.

        Args:
            composition_ids: Array of child atom IDs

        Returns:
            Concatenated bytes from all children (recursively expanded)
        """
        result = b""

        from ..utils import query_many

        # Query to get atoms by IDs
        query = """
            SELECT atom_value, composition_ids, canonical_text
            FROM atom
            WHERE atom_id = ANY(%s)
            ORDER BY array_position(%s, atom_id)
        """

        rows = await query_many(self.db, query, (composition_ids, composition_ids))

        for atom_value, child_comp_ids, canonical_text in rows:
            # Primitive: use value
            if atom_value is not None:
                result += atom_value
            # Composition: recurse
            elif child_comp_ids is not None:
                result += await self._expand_composition(child_comp_ids)
            # Fallback: canonical text
            elif canonical_text:
                result += canonical_text.encode("utf-8")

        return result

    async def reconstruct_sequence(
        self, wkt: str, tolerance: float = 1e-6
    ) -> List[bytes]:
        """
        Reconstruct sequence of atom values from trajectory.

        Args:
            wkt: LINESTRING ZM WKT
            tolerance: Coordinate matching tolerance

        Returns:
            List of atom values in sequence order
        """
        # Parse trajectory
        points = self.parse_wkt(wkt)

        # Sort by M coordinate (sequence index)
        points_sorted = sorted(points, key=lambda p: p[3])  # Sort by M value

        # Look up atoms
        atom_values = []
        for x, y, z, m in points_sorted:
            atom_value = await self.lookup_atom_at_coordinate(x, y, z, tolerance)
            if atom_value is None:
                raise ValueError(f"No atom found at coordinate ({x}, {y}, {z})")
            atom_values.append(atom_value)

        return atom_values

    async def reconstruct_text(self, wkt: str) -> str:
        """
        Reconstruct text from trajectory.

        Args:
            wkt: LINESTRING ZM WKT

        Returns:
            Reconstructed text string
        """
        atom_values = await self.reconstruct_sequence(wkt)

        # Assume atoms are UTF-8 encoded characters
        text = b"".join(atom_values).decode("utf-8")
        return text

    async def reconstruct_tensor(
        self, wkt: str, shape: Tuple[int, ...], dtype: str = "float32"
    ) -> np.ndarray:
        """
        Reconstruct tensor from trajectory.

        Args:
            wkt: LINESTRING ZM WKT
            shape: Target tensor shape (e.g., (768, 768) for weight matrix)
            dtype: Numpy dtype (default float32)

        Returns:
            Reconstructed numpy tensor
        """
        atom_values = await self.reconstruct_sequence(wkt)

        # Convert atom values to numpy array
        # Assume atoms are 4-byte float32 values
        if dtype == "float32":
            # Each atom should be 4 bytes
            flat_values = np.frombuffer(b"".join(atom_values), dtype=np.float32)
        else:
            raise NotImplementedError(f"Dtype {dtype} not yet supported")

        # Reshape to target shape
        tensor = flat_values.reshape(shape)
        return tensor

    def reconstruct_local(
        self,
        wkt: str,
        atom_lookup_dict: dict[Tuple[float, float, float], bytes],
        tolerance: float = 1e-6,
    ) -> List[bytes]:
        """
        Reconstruct sequence using local atom lookup (no database).

        Useful for testing without database connection.

        Args:
            wkt: LINESTRING ZM WKT
            atom_lookup_dict: Dict mapping (x,y,z) to atom_value
            tolerance: Coordinate matching tolerance

        Returns:
            List of atom values in sequence order
        """
        # Parse trajectory
        points = self.parse_wkt(wkt)

        # Sort by M coordinate
        points_sorted = sorted(points, key=lambda p: p[3])

        # Look up atoms from dict
        atom_values = []
        for x, y, z, m in points_sorted:
            # Find closest coordinate in dict (within tolerance)
            found = False
            for (cx, cy, cz), value in atom_lookup_dict.items():
                distance = ((x - cx) ** 2 + (y - cy) ** 2 + (z - cz) ** 2) ** 0.5
                if distance <= tolerance:
                    atom_values.append(value)
                    found = True
                    break

            if not found:
                raise ValueError(f"No atom found at coordinate ({x}, {y}, {z})")

        return atom_values
