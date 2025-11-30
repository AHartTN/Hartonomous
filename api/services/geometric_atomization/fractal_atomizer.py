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
import logging
from typing import List, Tuple, Optional, Dict
import numpy as np
from . import spatial_utils

logger = logging.getLogger(__name__)


class FractalAtomizer:
    """
    Implements fractal deduplication via recursive composition.
    
    "A Composition is just an Atom whose value is a pointer to other Atoms."
    """
    
    def __init__(self, db_connection=None, coordinate_range: float = 1e6, bpe_crystallizer=None):
        """
        Initialize FractalAtomizer.
        
        Args:
            db_connection: Database connection for persistence
            coordinate_range: Spatial coordinate range
            bpe_crystallizer: Optional BPECrystallizer instance for autonomous learning
        """
        self.db = db_connection
        self.coordinate_range = coordinate_range
        
        # Import BPECrystallizer here to avoid circular import
        from .bpe_crystallizer import BPECrystallizer
        
        # BPE Crystallizer for autonomous pattern learning (OODA loop)
        self.bpe = bpe_crystallizer or BPECrystallizer()
        
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
        greedy: bool = True,
        learn: bool = True
    ) -> List[int]:
        """
        Crystallize a sequence with BPE-powered greedy deduplication.
        
        This implements autonomous pattern learning via BPE Crystallizer:
        - OBSERVE: Count pair frequencies (if learn=True)
        - ACT: Apply learned merge rules to compress sequence
        
        Example:
            Input: ['L','o','r','e','m',' ','I','p','s','u','m']
            
            After learning "Lorem" and "Ipsum" are frequent:
            Output: [atom_id_lorem, atom_id_space, atom_id_ipsum]
            
            Before learning:
            Output: [atom_L, atom_o, atom_r, atom_e, atom_m, ...]
        
        Args:
            sequence: List of primitive values
            greedy: Whether to apply BPE compression
            learn: Whether to observe this sequence for learning (OODA loop)
        
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
        
        # BPE-powered greedy mode: autonomous pattern learning
        
        # First pass: create all primitives
        primitive_ids = []
        for value in sequence:
            atom_id = await self.get_or_create_primitive(value)
            primitive_ids.append(atom_id)
        
        # OBSERVE phase: Let BPE crystallizer observe this sequence
        if learn:
            self.bpe.observe_sequence(primitive_ids)
        
        # ACT phase: Apply learned merge rules to compress
        compressed_ids = self.bpe.apply_merges(primitive_ids)
        
        return compressed_ids
    
    def _compute_hilbert(self, x: float, y: float, z: float) -> int:
        """
        Compute Hilbert M coordinate.
        
        Delegates to spatial_utils.compute_hilbert_index().
        """
        return spatial_utils.compute_hilbert_index(x, y, z, self.coordinate_range)
    
    async def learn_patterns(self, auto_mint: bool = True, top_k: int = 100) -> Dict:
        """
        Execute BPE learning cycle (ORIENT + DECIDE phases).
        
        This should be called periodically (e.g., after ingesting N documents)
        to mint new composition atoms for frequently observed patterns.
        
        Process:
        1. ORIENT: Identify most frequent pairs from observations
        2. DECIDE: Decide which patterns warrant composition atoms
        3. ACT: Mint new composition atoms for these patterns
        
        Args:
            auto_mint: If True, automatically mint compositions for frequent pairs
            top_k: Consider top K most frequent pairs
        
        Returns:
            Dict with learning statistics
        """
        logger.info("\\n" + "="*80)
        logger.info("BPE LEARNING CYCLE")
        logger.info("="*80)
        
        # Get current statistics
        stats_before = self.bpe.get_stats()
        logger.info(f"Total pairs observed: {stats_before['total_pairs_observed']:,}")
        logger.info(f"Unique pairs: {stats_before['unique_pairs']:,}")
        logger.info(f"Merge rules learned: {stats_before['merge_rules_learned']}")
        
        # ORIENT + DECIDE + ACT: Mint new composition atoms
        minted = await self.bpe.decide_and_mint(self, auto_mint=auto_mint)
        
        stats_after = self.bpe.get_stats()
        logger.info(f"\\nNew compositions minted: {len(minted)}")
        logger.info(f"Total merge rules: {stats_after['merge_rules_learned']}")
        
        if minted:
            logger.info("\\nTop 10 new patterns:")
            for i, (pair, comp_id) in enumerate(minted[:10], 1):
                logger.info(f"  {i}. Pair {pair} -> Composition {comp_id}")
        
        logger.info("="*80 + "\\n")
        
        return {
            'minted_count': len(minted),
            'total_merge_rules': stats_after['merge_rules_learned'],
            'patterns': [(str(pair), comp_id) for pair, comp_id in minted]
        }
