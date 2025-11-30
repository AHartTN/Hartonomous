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

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import struct
from typing import List, Tuple, Optional, Dict
import numpy as np


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
        
        Same as AtomLocator.locate() from geometric implementation.
        """
        hash_bytes = hashlib.sha256(value).digest()
        
        # Extract 24 bytes (8 per dimension)
        x_bytes = hash_bytes[0:8]
        y_bytes = hash_bytes[8:16]
        z_bytes = hash_bytes[16:24]
        
        # Convert to coordinates
        x_int = struct.unpack('<Q', x_bytes)[0]
        y_int = struct.unpack('<Q', y_bytes)[0]
        z_int = struct.unpack('<Q', z_bytes)[0]
        
        max_uint64 = 2**64 - 1
        x_norm = x_int / max_uint64
        y_norm = y_int / max_uint64
        z_norm = z_int / max_uint64
        
        x = (x_norm * 2 - 1) * self.coordinate_range
        y = (y_norm * 2 - 1) * self.coordinate_range
        z = (z_norm * 2 - 1) * self.coordinate_range
        
        return (x, y, z)
    
    def locate_composition(
        self, 
        child_ids: List[int],
        strategy: str = 'midpoint'
    ) -> Tuple[float, float, float]:
        """
        Compute deterministic coordinate for composition atom.
        
        Strategies:
        - 'midpoint': Average of child coordinates (spatial center)
        - 'concept': Hash-based (independent of children, represents abstract concept)
        - 'weighted': Weighted average (children have different importance)
        
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
            
            # Compute midpoint
            x = sum(c[0] for c in child_coords) / len(child_coords)
            y = sum(c[1] for c in child_coords) / len(child_coords)
            z = sum(c[2] for c in child_coords) / len(child_coords)
            
            return (x, y, z)
        
        elif strategy == 'concept':
            # Hash the composition itself
            # This treats the composition as an independent concept
            comp_hash = self.hash_composition(child_ids)
            return self.locate_primitive(comp_hash)
        
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
        
        # Check database
        async with self.db.cursor() as cursor:
            # Check if exists by content_hash
            await cursor.execute("""
                SELECT atom_id FROM atom WHERE content_hash = $1
            """, (content_hash,))
            
            row = await cursor.fetchone()
            if row:
                atom_id = row[0]
                self.atom_cache[value] = atom_id
                self.coord_cache[atom_id] = coord
                return atom_id
            
            # Create new atom
            import json
            x, y, z = coord
            m = self._compute_hilbert(x, y, z)
            
            await cursor.execute("""
                INSERT INTO atom (
                    content_hash, 
                    atom_value, 
                    spatial_key,
                    is_stable,
                    metadata
                )
                VALUES (
                    $1, $2, 
                    ST_MakePointM($3, $4, $5, $6),
                    TRUE,
                    $7::JSONB
                )
                RETURNING atom_id
            """, (
                content_hash, 
                value, 
                x, y, z, m,
                json.dumps(metadata or {})
            ))
            
            atom_id = (await cursor.fetchone())[0]
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
        
        # Check database (by coordinate - O(1) collision detection)
        async with self.db.cursor() as cursor:
            x, y, z = coord
            
            # Check if composition exists at this coordinate
            await cursor.execute("""
                SELECT atom_id, composition_ids 
                FROM atom 
                WHERE composition_ids IS NOT NULL
                AND ST_DWithin(spatial_key, ST_MakePointM($1, $2, $3, 0), $4)
                ORDER BY ST_Distance(spatial_key, ST_MakePointM($1, $2, $3, 0))
                LIMIT 1
            """, (x, y, z, 1e-6))  # Tolerance for float comparison
            
            row = await cursor.fetchone()
            if row:
                atom_id, db_child_ids = row
                # Verify it's the same composition
                if tuple(db_child_ids) == child_tuple:
                    self.composition_cache[child_tuple] = atom_id
                    self.coord_cache[atom_id] = coord
                    return atom_id
            
            # Create new composition
            import json
            m = self._compute_hilbert(x, y, z)
            
            await cursor.execute("""
                INSERT INTO atom (
                    content_hash,
                    composition_ids,
                    spatial_key,
                    canonical_text,
                    is_stable,
                    metadata
                )
                VALUES (
                    $1, $2,
                    ST_MakePointM($3, $4, $5, $6),
                    $7, $8,
                    $9::JSONB
                )
                RETURNING atom_id
            """, (
                content_hash,
                child_ids,
                x, y, z, m,
                canonical_text,
                is_stable,
                json.dumps(metadata or {})
            ))
            
            atom_id = (await cursor.fetchone())[0]
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
        """Compute Hilbert M coordinate (Morton encoding as placeholder)."""
        # Simplified Morton encoding (same as AtomLocator)
        bits = 21
        max_val = 2**bits - 1
        
        x_int = int((x / self.coordinate_range + 1) / 2 * max_val)
        y_int = int((y / self.coordinate_range + 1) / 2 * max_val)
        z_int = int((z / self.coordinate_range + 1) / 2 * max_val)
        
        x_int = max(0, min(max_val, x_int))
        y_int = max(0, min(max_val, y_int))
        z_int = max(0, min(max_val, z_int))
        
        return self._morton_encode(x_int, y_int, z_int)
    
    def _morton_encode(self, x: int, y: int, z: int) -> int:
        """Morton encoding."""
        def split_by_3(value: int) -> int:
            value &= 0x1fffff
            value = (value | value << 32) & 0x1f00000000ffff
            value = (value | value << 16) & 0x1f0000ff0000ff
            value = (value | value << 8) & 0x100f00f00f00f00f
            value = (value | value << 4) & 0x10c30c30c30c30c3
            value = (value | value << 2) & 0x1249249249249249
            return value
        
        return split_by_3(x) | (split_by_3(y) << 1) | (split_by_3(z) << 2)
