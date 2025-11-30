"""
TrajectoryBuilder: Construct LINESTRING Trajectories

Builds LINESTRINGZM geometries from sequences of atom coordinates.
This is the KEY innovation that eliminates record explosion.

Architecture:
- Input: Sequence of atom coordinates [(x1,y1,z1), (x2,y2,z2), ...]
- Output: LINESTRINGZM(x1 y1 z1 m1, x2 y2 z2 m2, ...) as WKT
- M coordinate: Sequence index (0, 1, 2, ...) for reshape during reconstruction

Example:
  "Hello" → LINESTRING(coord_H 0, coord_e 1, coord_l 2, coord_l 3, coord_o 4)
  
  Tensor[53M] → LINESTRING(coord_w1 0, coord_w2 1, ..., coord_w53M 53M-1)
  
This stores ENTIRE sequences as SINGLE database rows instead of millions.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

from typing import List, Tuple


class TrajectoryBuilder:
    """
    Constructs LINESTRING trajectories from atom coordinate sequences.
    
    This eliminates record explosion by storing sequences as geometries
    instead of relational foreign keys.
    """
    
    def __init__(self):
        """Initialize TrajectoryBuilder."""
    
    def build_wkt(
        self, 
        coordinates: List[Tuple[float, float, float]], 
        m_values: List[float] = None
    ) -> str:
        """
        Build LINESTRINGZM Well-Known Text (WKT) from coordinates.
        
        Args:
            coordinates: List of (x, y, z) tuples
            m_values: Optional M values (defaults to 0, 1, 2, ...)
        
        Returns:
            WKT string: "LINESTRING ZM (x1 y1 z1 m1, x2 y2 z2 m2, ...)"
        """
        if not coordinates:
            raise ValueError("Cannot build trajectory from empty coordinate list")
        
        # Default M values: sequence indices
        if m_values is None:
            m_values = list(range(len(coordinates)))
        
        if len(coordinates) != len(m_values):
            raise ValueError(
                f"Coordinate count ({len(coordinates)}) must match M value count ({len(m_values)})"
            )
        
        # Build point strings: "x y z m"
        points = []
        for (x, y, z), m in zip(coordinates, m_values):
            points.append(f"{x} {y} {z} {m}")
        
        # Construct WKT
        wkt = f"LINESTRING ZM ({', '.join(points)})"
        return wkt
    
    def build_from_atoms(
        self,
        atom_values: List[bytes],
        atom_locator,  # AtomLocator instance
        m_values: List[float] = None
    ) -> str:
        """
        Build trajectory directly from atom values.
        
        Args:
            atom_values: List of atom values (raw bytes)
            atom_locator: AtomLocator instance for coordinate mapping
            m_values: Optional M values (defaults to sequence indices)
        
        Returns:
            WKT string
        """
        # Locate all atoms
        coordinates = atom_locator.locate_multiple(atom_values)
        
        # Build trajectory
        return self.build_wkt(coordinates, m_values)
    
    def build_weighted(
        self,
        atom_values: List[bytes],
        weights: List[float],
        atom_locator
    ) -> str:
        """
        Build trajectory with weights stored in M coordinate.
        
        This is useful for attention mechanisms where M = attention weight
        instead of sequence index.
        
        Args:
            atom_values: List of atom values
            weights: List of weights (e.g., attention scores)
            atom_locator: AtomLocator instance
        
        Returns:
            WKT string with weights as M values
        """
        coordinates = atom_locator.locate_multiple(atom_values)
        return self.build_wkt(coordinates, m_values=weights)
    
    def chunk_trajectory(
        self,
        coordinates: List[Tuple[float, float, float]],
        chunk_size: int = 10000,
        m_values: List[float] = None
    ) -> List[str]:
        """
        Split large trajectory into chunks to avoid PostGIS limits.
        
        PostGIS has practical limits on geometry complexity.
        For very large sequences (e.g., 53M weights), split into chunks.
        
        Args:
            coordinates: Full coordinate list
            chunk_size: Max points per LINESTRING (default 10K)
            m_values: Optional M values
        
        Returns:
            List of WKT strings (one per chunk)
        """
        from ..utils import chunk_list
        
        if m_values is None:
            m_values = list(range(len(coordinates)))
        
        # Chunk coordinates and m_values together
        coord_chunks = chunk_list(coordinates, chunk_size)
        m_chunks = chunk_list(m_values, chunk_size)
        
        # Build WKT for each chunk
        chunks = [
            self.build_wkt(coords, m_vals) 
            for coords, m_vals in zip(coord_chunks, m_chunks)
        ]
        
        return chunks
    
    def build_ewkt(
        self,
        coordinates: List[Tuple[float, float, float]],
        srid: int = 0,
        m_values: List[float] = None
    ) -> str:
        """
        Build Extended Well-Known Text with SRID.
        
        Args:
            coordinates: List of (x, y, z) tuples
            srid: Spatial Reference System ID (default 0 = undefined/local)
            m_values: Optional M values
        
        Returns:
            EWKT string: "SRID=0;LINESTRING ZM (...)"
        """
        wkt = self.build_wkt(coordinates, m_values)
        ewkt = f"SRID={srid};{wkt}"
        return ewkt
