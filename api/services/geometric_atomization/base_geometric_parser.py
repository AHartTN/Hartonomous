"""
Base Geometric Parser - Standardized ingestion pipeline

All data types (text, code, audio, etc.) should inherit from this base class
to ensure consistent use of the geometric/fractal atomization pipeline.

Architecture:
1. Chunk data (implementation-specific per modality)
2. Fractal deduplication (standardized via BPE Crystallizer)
3. Geometric storage (standardized via TrajectoryBuilder)

This forces all ingestion to use the efficient "Fractal/Geometric" path.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from typing import List, Any, Dict, Optional
from psycopg import AsyncConnection
from abc import ABC, abstractmethod

from .fractal_atomizer import FractalAtomizer
from .trajectory_builder import TrajectoryBuilder
from .bpe_crystallizer import BPECrystallizer

logger = logging.getLogger(__name__)


class BaseGeometricParser(ABC):
    """
    Base class for all geometric parsers.
    
    Enforces consistent pipeline:
    1. chunk_data() - modality-specific tokenization
    2. crystallize_sequence() - fractal deduplication with BPE learning
    3. build_trajectory() - geometric storage as LINESTRING
    """
    
    def __init__(
        self,
        fractal_atomizer: Optional[FractalAtomizer] = None,
        trajectory_builder: Optional[TrajectoryBuilder] = None,
        bpe_crystallizer: Optional[BPECrystallizer] = None
    ):
        """
        Initialize base geometric parser.
        
        Args:
            fractal_atomizer: Fractal atomizer instance (creates if None)
            trajectory_builder: Trajectory builder instance (creates if None)
            bpe_crystallizer: BPE crystallizer instance (creates if None)
        """
        self.atomizer = fractal_atomizer or FractalAtomizer()
        self.builder = trajectory_builder or TrajectoryBuilder()
        self.crystallizer = bpe_crystallizer or BPECrystallizer()
        
        self.stats = {
            "total_processed": 0,
            "atoms_created": 0,
            "trajectories_created": 0,
            "sequences_processed": 0,
        }
    
    @abstractmethod
    async def chunk_data(self, stream: Any, modality: str) -> List[bytes]:
        """
        Tokenize/chunk data into byte sequences.
        
        Implementation-specific per modality:
        - Text: character bytes, BPE tokens, word boundaries
        - Code: syntax-aware chunks, AST nodes
        - Audio: frames, samples, spectral features
        - Images: patches, pixels, embeddings
        
        Args:
            stream: Input data stream (file, bytes, etc.)
            modality: Data type identifier
            
        Returns:
            List of byte chunks ready for atomization
        """
        pass
    
    async def process_stream(
        self,
        stream: Any,
        modality: str,
        conn: AsyncConnection,
        metadata: Optional[Dict[str, Any]] = None
    ) -> int:
        """
        Process complete data stream using geometric pipeline.
        
        This is the standardized method that all parsers use.
        
        Args:
            stream: Input data stream
            modality: Data type identifier
            conn: Database connection
            metadata: Optional metadata for trajectory atom
            
        Returns:
            Trajectory atom ID
        """
        logger.info(f"Processing {modality} stream via geometric pipeline...")
        
        # Step 1: Tokenize/Chunk (implementation-specific)
        chunks = await self.chunk_data(stream, modality)
        self.stats['total_processed'] += len(chunks)
        logger.info(f"  Chunked into {len(chunks):,} elements")
        
        # Step 2: Fractal Deduplication (standardized)
        # Uses BPE Crystallizer logic automatically
        logger.info(f"  Crystallizing sequence with fractal deduplication...")
        atom_ids = await self.crystallize_sequence(conn, chunks)
        unique_atoms = len(set(atom_ids))
        self.stats['atoms_created'] += unique_atoms
        
        dedup_ratio = len(atom_ids) / unique_atoms if unique_atoms > 0 else 1.0
        logger.info(
            f"  ✓ {len(atom_ids):,} elements → {unique_atoms:,} unique atoms "
            f"({dedup_ratio:.1f}x deduplication)"
        )
        
        # Step 3: Geometric Storage (standardized)
        # Stores as a trajectory in one go
        logger.info(f"  Building trajectory from {len(atom_ids):,} atoms...")
        trajectory_wkt = await self.build_trajectory_from_atoms(conn, atom_ids)
        
        # Step 4: Save trajectory as composition atom
        trajectory_metadata = metadata or {}
        trajectory_metadata.update({
            "modality": modality,
            "total_elements": len(chunks),
            "unique_atoms": unique_atoms,
        })
        
        trajectory_atom_id = await self.save_trajectory(
            conn,
            atom_ids,
            trajectory_wkt,
            trajectory_metadata
        )
        
        self.stats['trajectories_created'] += 1
        self.stats['sequences_processed'] += 1
        
        logger.info(f"  ✓ Trajectory saved: atom_id={trajectory_atom_id}")
        
        return trajectory_atom_id
    
    async def crystallize_sequence(
        self,
        conn: AsyncConnection,
        chunks: List[bytes]
    ) -> List[int]:
        """
        Crystallize sequence with fractal deduplication and BPE learning.
        
        Delegates to FractalAtomizer which:
        1. Creates primitive atoms for each chunk
        2. Observes patterns (OBSERVE phase of OODA loop)
        3. Applies learned merge rules (ACT phase)
        4. Returns compressed atom ID sequence
        
        Args:
            conn: Database connection
            chunks: List of byte chunks
            
        Returns:
            List of atom IDs (compressed via BPE merges)
        """
        # Set database connection on atomizer
        self.atomizer.db = conn
        
        # Use FractalAtomizer's crystallize_sequence with BPE learning
        atom_ids = await self.atomizer.crystallize_sequence(
            chunks,
            greedy=True,  # Enable BPE compression
            learn=True    # Enable pattern learning (OODA loop)
        )
        
        return atom_ids
    
    async def build_trajectory_from_atoms(
        self,
        conn: AsyncConnection,
        atom_ids: List[int]
    ) -> str:
        """
        Build LINESTRING WKT from atom coordinates.
        
        Args:
            conn: Database connection
            atom_ids: List of atom IDs
            
        Returns:
            WKT string for LINESTRING trajectory
        """
        # Query coordinates for all atoms
        query = """
            SELECT atom_id, ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key)
            FROM atom
            WHERE atom_id = ANY($1)
            ORDER BY atom_id
        """
        
        from ..utils import query_many
        rows = await query_many(conn, query, (atom_ids,))
        
        # Build coordinate lookup
        coord_map = {
            row[0]: (row[1], row[2], row[3])
            for row in rows
        }
        
        # Build ordered coordinate list
        coordinates = [coord_map[aid] for aid in atom_ids]
        
        # Build LINESTRING WKT
        wkt = self.builder.build_wkt(coordinates)
        
        return wkt
    
    async def save_trajectory(
        self,
        conn: AsyncConnection,
        atom_ids: List[int],
        trajectory_wkt: str,
        metadata: Dict[str, Any]
    ) -> int:
        """
        Save trajectory as composition atom.
        
        Args:
            conn: Database connection
            atom_ids: List of child atom IDs
            trajectory_wkt: WKT string for trajectory geometry
            metadata: Trajectory metadata
            
        Returns:
            Trajectory atom ID
        """
        trajectory_atom_id = await self.atomizer.get_or_create_composition(
            conn=conn,
            child_atom_ids=atom_ids,
            metadata=metadata,
            canonical_text=f"Trajectory: {metadata.get('modality', 'unknown')}",
            spatial_wkt=trajectory_wkt
        )
        
        return trajectory_atom_id
    
    def get_stats(self) -> Dict[str, Any]:
        """Get parsing statistics."""
        return self.stats.copy()
    
    async def trigger_learning_cycle(self, conn: AsyncConnection) -> Dict:
        """
        Trigger BPE learning cycle (ORIENT + DECIDE + ACT phases).
        
        Call this periodically after processing batches of documents
        to mint new composition atoms for frequently observed patterns.
        
        Args:
            conn: Database connection
            
        Returns:
            Learning statistics
        """
        self.atomizer.db = conn
        return await self.atomizer.learn_patterns(auto_mint=True)
