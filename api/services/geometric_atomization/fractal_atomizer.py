"""
Fractal Atomizer: Recursive Composition with Greedy Crystallization

BREAKTHROUGH: Everything is an atom - primitives AND compositions.

Architecture:
- Level 0: Primitives ('.')
- Level 1: Compositions ('...' = ['.', '.', '.'])
- Level 2: Higher Order ('Hello...' = ['Hello', '...'])

Key Features:
1. Greedy Crystallization: Collapse sequences into largest known chunks
2. O(1) Lookup: Coordinate = hash of composition
3. Semantic Compression: Paragraphs/disclaimers stored once, referenced 1000x

REFACTORED: All spatial operations delegate to spatial_utils (DRY principle).

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import struct
from typing import List, Tuple, Optional, Dict
import numpy as np
from . import spatial_utils


class FractalAtomizer:
    """
    Implements fractal deduplication via recursive composition.
    
    "A Composition is just an Atom whose value is a pointer to other Atoms."
    """
    
    def __init__(self, db_connection=None, coordinate_range: float = 1e6):
        """
        Initialize FractalAtomizer.
        
        Args:
            db_connection: Database connection for persistence
            coordinate_range: Spatial coordinate range
        """
        self.db = db_connection
        self.coordinate_range = coordinate_range
        
        # Local cache for O(1) lookups (in-memory deduplication)
        # Maps: (atom_value OR composition_tuple) -> atom_id
        self.atom_cache: Dict[bytes, int] = {}
        self.composition_cache: Dict[Tuple[int, ...], int] = {}
        
        # Reverse cache: atom_id -> coordinate (for composition calculation)
        self.coord_cache: Dict[int, Tuple[float, float, float]] = {}
    
    def hash_value(self, value: bytes) -> bytes:
        """Compute SHA-256 hash of primitive value."""
        return hashlib.sha256(value).digest()
    
    def hash_composition(self, child_ids: List[int]) -> bytes:
        """
        Compute SHA-256 hash of composition.
        
        Hash is deterministic: same children -> same hash.
        
        Args:
            child_ids: List of child atom IDs
        
        Returns:
            SHA-256 hash
        """
        # Serialize child IDs as bytes
        composition_bytes = struct.pack(f'{len(child_ids)}Q', *child_ids)
        return hashlib.sha256(composition_bytes).digest()
    
    def locate_primitive(self, value: bytes) -> Tuple[float, float, float]:
        """
        Compute deterministic coordinate for primitive atom.
        
        Delegates to spatial_utils.hash_to_coordinate().
        """
        return spatial_utils.hash_to_coordinate(value, self.coordinate_range)
    
    def locate_composition(
        self, 
        child_ids: List[int],
        strategy: str = 'midpoint'
    ) -> Tuple[float, float, float]:
        """
        Compute deterministic coordinate for composition atom.
        
        Delegates to spatial_utils composition helpers.
        
        Strategies:
        - 'midpoint': Average of child coordinates (spatial center)
        - 'concept': Hash-based (independent of children, represents abstract concept)
        
        Args:
            child_ids: List of child atom IDs
            strategy: Coordinate calculation strategy
        
        Returns:
            (x, y, z) coordinate tuple
        """
        if strategy == 'midpoint':
            # Get child coordinates
            child_coords = []
            for child_id in child_ids:
                if child_id in self.coord_cache:
                    child_coords.append(self.coord_cache[child_id])
                else:
                    # TODO: Query from database if not in cache
                    raise ValueError(f"Child atom {child_id} coordinate not in cache")
            
            # Delegate to spatial_utils
            x, y, z, _ = spatial_utils.locate_composition_midpoint(
                child_coords, self.coordinate_range
            )
            return (x, y, z)
        
        elif strategy == 'concept':
            # Delegate to spatial_utils
            x, y, z, _ = spatial_utils.locate_composition_concept(
                child_ids, self.coordinate_range
            )
            return (x, y, z)
        
        else:
            raise ValueError(f"Unknown strategy: {strategy}")
    
    async def get_or_create_primitive(
        self, 
        value: bytes,
        metadata: dict = None
    ) -> int:
        """
        Get existing primitive atom or create new one.
        
        Args:
            value: Primitive value (≤64 bytes)
            metadata: Optional metadata
        
        Returns:
            atom_id
        """
        # Check cache first
        if value in self.atom_cache:
            return self.atom_cache[value]
        
        # Compute hash and coordinate
        content_hash = self.hash_value(value)
        coord = self.locate_primitive(value)
        
        if self.db is None:
            # No database - assign temporary ID
            temp_id = len(self.atom_cache) + 1
            self.atom_cache[value] = temp_id
            self.coord_cache[temp_id] = coord
            return temp_id
        
        from ..utils import query_one, query_one_returning
        
        # Check database - Check if exists by content_hash
        row = await query_one(self.db, """
            SELECT atom_id FROM atom WHERE content_hash = %s
        """, (content_hash,))
        
        if row:
            atom_id = row[0]
            self.atom_cache[value] = atom_id
            self.coord_cache[atom_id] = coord
            return atom_id
        
        # Create new atom
        import json
        x, y, z = coord
        m = self._compute_hilbert(x, y, z)
        
        # Use spatial_utils for safe point creation
        point_sql, point_params = spatial_utils.make_point_zm(x, y, z, m)
        
        atom_id = await query_one_returning(self.db, f"""
            INSERT INTO atom (
                content_hash, 
                atom_value, 
                spatial_key,
                is_stable,
                metadata
            )
            VALUES (
                %s, %s, 
                {point_sql},
                TRUE,
                %s::JSONB
            )
            RETURNING atom_id
        """, (
            content_hash, 
            value,
            *point_params,
            json.dumps(metadata or {})
        ))
        
        await self.db.commit()
        
        # Cache it
        self.atom_cache[value] = atom_id
        self.coord_cache[atom_id] = coord
        
        return atom_id
    
    async def get_or_create_composition(
        self,
        child_ids: List[int],
        canonical_text: str = None,
        metadata: dict = None,
        is_stable: bool = True
    ) -> int:
        """
        Get existing composition or create new one.
        
        This is the KEY function for fractal deduplication.
        
        Args:
            child_ids: List of child atom IDs
            canonical_text: Optional text representation
            metadata: Optional metadata
            is_stable: Whether this is a stable concept (vs transient)
        
        Returns:
            atom_id of composition
        """
        # Validate input
        if not child_ids:
            raise ValueError("Cannot create composition with empty child_ids")
        
        # Check cache
        child_tuple = tuple(child_ids)
        if child_tuple in self.composition_cache:
            return self.composition_cache[child_tuple]
        
        # Compute hash and coordinate
        content_hash = self.hash_composition(child_ids)
        coord = self.locate_composition(child_ids, strategy='concept')
        
        if self.db is None:
            # No database - assign temporary ID
            temp_id = len(self.atom_cache) + len(self.composition_cache) + 1
            self.composition_cache[child_tuple] = temp_id
            self.coord_cache[temp_id] = coord
            return temp_id
        
        from ..utils import query_one, query_one_returning
        
        # Check database (by coordinate - O(1) collision detection)
        x, y, z = coord
        m = self._compute_hilbert(x, y, z)
        
        # Use spatial_utils for safe point creation
        point_sql, point_params = spatial_utils.make_point_zm(x, y, z, m)
        
        # Check if composition exists at this coordinate
        row = await query_one(self.db, f"""
            SELECT atom_id, composition_ids 
            FROM atom 
            WHERE composition_ids IS NOT NULL
            AND ST_DWithin(spatial_key, {point_sql}, %s)
            ORDER BY ST_Distance(spatial_key, {point_sql})
            LIMIT 1
        """, (*point_params, 1e-6, *point_params))  # Tolerance for float comparison
        
        if row:
            atom_id, db_child_ids = row
            # Verify it's the same composition
            if tuple(db_child_ids) == child_tuple:
                self.composition_cache[child_tuple] = atom_id
                self.coord_cache[atom_id] = coord
                return atom_id
        
        # Create new composition
        import json
        # M already computed above
        
        # Use spatial_utils for safe point creation
        point_sql, point_params = spatial_utils.make_point_zm(x, y, z, m)
        
        atom_id = await query_one_returning(self.db, f"""
            INSERT INTO atom (
                content_hash,
                composition_ids,
                spatial_key,
                canonical_text,
                is_stable,
                metadata
            )
            VALUES (
                %s, %s,
                {point_sql},
                %s, %s,
                %s::JSONB
            )
            RETURNING atom_id
        """, (
            content_hash,
            child_ids,
            *point_params,
            canonical_text,
            is_stable,
            json.dumps(metadata or {})
        ))
        
        await self.db.commit()
        
        # Cache it
        self.composition_cache[child_tuple] = atom_id
        self.coord_cache[atom_id] = coord
        
        return atom_id
    
    async def crystallize_sequence(
        self,
        sequence: List[bytes],
        greedy: bool = True
    ) -> List[int]:
        """
        Crystallize a sequence with greedy deduplication.
        
        This implements "Longest Common Subsequence" collapsing.
        
        Example:
            Input: ['L','o','r','e','m',' ','I','p','s','u','m']
            
            If "Lorem" and "Ipsum" are known compositions:
            Output: [atom_id_lorem, atom_id_space, atom_id_ipsum]
            
            If not known:
            Output: [atom_L, atom_o, atom_r, atom_e, atom_m, ...]
        
        Args:
            sequence: List of primitive values
            greedy: Whether to greedily collapse into largest chunks
        
        Returns:
            List of atom IDs (potentially much shorter than input)
        """
        if not greedy:
            # Simple mode: atomize each value individually
            atom_ids = []
            for value in sequence:
                atom_id = await self.get_or_create_primitive(value)
                atom_ids.append(atom_id)
            return atom_ids
        
        # Greedy mode: try to find largest known compositions
        # This is a sliding window approach
        
        # First pass: create all primitives
        primitive_ids = []
        for value in sequence:
            atom_id = await self.get_or_create_primitive(value)
            primitive_ids.append(atom_id)
        
        # Second pass: try to collapse into compositions
        # For now, implement simple 2-element and 3-element patterns
        # TODO: Implement full longest-common-subsequence algorithm
        
        collapsed = []
        i = 0
        while i < len(primitive_ids):
            # Try 3-element window
            if i + 2 < len(primitive_ids):
                triple = tuple(primitive_ids[i:i+3])
                if triple in self.composition_cache:
                    collapsed.append(self.composition_cache[triple])
                    i += 3
                    continue
            
            # Try 2-element window
            if i + 1 < len(primitive_ids):
                pair = tuple(primitive_ids[i:i+2])
                if pair in self.composition_cache:
                    collapsed.append(self.composition_cache[pair])
                    i += 2
                    continue
            
            # No match - use primitive
            collapsed.append(primitive_ids[i])
            i += 1
        
        return collapsed
    
    def _compute_hilbert(self, x: float, y: float, z: float) -> int:
        """
        Compute Hilbert M coordinate.
        
        Delegates to spatial_utils.compute_hilbert_index().
        """
        return spatial_utils.compute_hilbert_index(x, y, z, self.coordinate_range)
