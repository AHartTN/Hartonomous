"""
GGUF Model Atomizer - Geometric/Fractal Implementation

Stores entire tensors as LINESTRING trajectories instead of millions of rows.

Key Innovation:
- 70B parameter model = ~500 LINESTRING rows (one per tensor)
- NOT 70 billion composition rows

Architecture:
1. Load GGUF file and extract tensors
2. For each tensor: atomize unique weights
3. Build LINESTRING trajectory from weight atom coordinates
4. Store single trajectory row per tensor

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool

from ..utils import execute_query, query_one
from .atom_locator import AtomLocator
from .fractal_atomizer import FractalAtomizer
from .trajectory_builder import TrajectoryBuilder

logger = logging.getLogger(__name__)


class GGUFAtomizer:
    """
    Atomizes GGUF model files using geometric/fractal approach.

    Stores tensors as LINESTRING trajectories for massive compression.
    """

    def __init__(self, threshold: float = 1e-6):
        """
        Initialize GGUF atomizer.

        Args:
            threshold: Sparsity threshold for filtering near-zero weights
        """
        self.threshold = threshold
        self.fractal_atomizer = FractalAtomizer()
        self.trajectory_builder = TrajectoryBuilder()
        self.atom_locator = AtomLocator()

        self.stats = {
            "total_processed": 0,
            "atoms_created": 0,
            "sparse_skipped": 0,
            "tensors_processed": 0,
            "trajectories_created": 0,
        }

    async def atomize_model(
        self,
        file_path: Path,
        model_name: str,
        conn: AsyncConnection,
        pool: Optional[AsyncConnectionPool] = None,
    ) -> Dict[str, Any]:
        """
        Atomize entire GGUF model file.

        Args:
            file_path: Path to GGUF file
            model_name: Model identifier
            conn: Database connection
            pool: Connection pool for parallel operations

        Returns:
            Dict with atomization statistics
        """
        logger.info(f"\n{'='*80}")
        logger.info(f"Starting Geometric GGUF Model Atomization")
        logger.info(f"  Model: {model_name}")
        logger.info(f"  File: {file_path}")
        logger.info(f"  Size: {file_path.stat().st_size / 1e6:.2f} MB")
        logger.info(f"  Threshold: {self.threshold}")
        logger.info(f"{'='*80}\n")

        # Load GGUF file
        try:
            from gguf import GGUFReader
        except ImportError:
            raise ImportError(
                "gguf package required for GGUF atomization. "
                "Install with: pip install gguf"
            )

        logger.info("Loading GGUF file...")
        reader = GGUFReader(str(file_path))

        # Atomize metadata
        await self._atomize_architecture(conn, reader, model_name)
        await self._atomize_vocabulary(conn, reader, model_name)

        # Atomize tensors
        logger.info(f"\nProcessing {len(reader.tensors)} tensors as trajectories...")

        for idx, tensor in enumerate(reader.tensors, 1):
            logger.info(f"\n[Tensor {idx}/{len(reader.tensors)}] {tensor.name}")

            await self._atomize_tensor_as_trajectory(
                conn=conn,
                tensor_name=tensor.name,
                tensor_data=tensor.data,
                model_name=model_name,
            )

            self.stats["tensors_processed"] += 1

        # Calculate final stats
        if self.stats["total_processed"] > 0:
            self.stats["deduplication_ratio"] = (
                self.stats["total_processed"] / self.stats["atoms_created"]
                if self.stats["atoms_created"] > 0
                else 1.0
            )

        if self.stats["total_processed"] > 0:
            self.stats["sparse_percentage"] = (
                self.stats["sparse_skipped"] / self.stats["total_processed"] * 100
            )

        logger.info(f"\n{'='*80}")
        logger.info(f"Geometric GGUF Atomization Complete")
        logger.info(f"  Tensors processed: {self.stats['tensors_processed']:,}")
        logger.info(f"  Trajectories created: {self.stats['trajectories_created']:,}")
        logger.info(f"  Total weights processed: {self.stats['total_processed']:,}")
        logger.info(f"  Unique atoms created: {self.stats['atoms_created']:,}")
        logger.info(f"  Sparse weights skipped: {self.stats['sparse_skipped']:,}")
        logger.info(
            f"  Deduplication ratio: {self.stats.get('deduplication_ratio', 0):.1f}x"
        )
        logger.info(f"  Sparse savings: {self.stats.get('sparse_percentage', 0):.1f}%")
        logger.info(f"{'='*80}\n")

        return self.stats

    async def _atomize_tensor_as_trajectory(
        self, conn: AsyncConnection, tensor_name: str, tensor_data: Any, model_name: str
    ) -> int:
        """
        Atomize a single tensor and store as LINESTRING trajectory.

        This is the key method that implements geometric storage.

        Args:
            conn: Database connection
            tensor_name: Name of the tensor
            tensor_data: Tensor data (numpy array)
            model_name: Model identifier

        Returns:
            Trajectory atom ID
        """
        import json

        import numpy as np

        logger.info(f"  Shape: {tensor_data.shape}")
        logger.info(f"  Size: {tensor_data.size:,} elements")
        logger.info(f"  Dtype: {tensor_data.dtype}")

        # Flatten and convert to float32
        weights = tensor_data.flatten().astype(np.float32).tolist()
        original_count = len(weights)
        self.stats["total_processed"] += original_count

        # Filter sparse weights
        non_sparse_weights = [w for w in weights if abs(w) >= self.threshold]
        sparse_skipped = original_count - len(non_sparse_weights)
        self.stats["sparse_skipped"] += sparse_skipped

        if sparse_skipped > 0:
            logger.info(
                f"  Filtered {sparse_skipped:,} sparse weights "
                f"({sparse_skipped/original_count*100:.1f}%)"
            )

        # Convert weights to bytes for fractal atomization
        weight_bytes = [np.float32(w).tobytes() for w in non_sparse_weights]

        # Get or create atoms for unique weights using fractal atomizer
        logger.info(f"  Atomizing {len(weight_bytes):,} weights...")
        atom_ids = []
        for weight in weight_bytes:
            atom_id = await self.fractal_atomizer.get_or_create_atom(
                conn=conn, atom_value=weight, metadata={"modality": "model-weight"}
            )
            atom_ids.append(atom_id)

        unique_atoms = len(set(atom_ids))
        self.stats["atoms_created"] += unique_atoms

        dedup_ratio = len(atom_ids) / unique_atoms if unique_atoms > 0 else 1.0
        logger.info(
            f"  ✓ {len(atom_ids):,} weights → {unique_atoms:,} unique atoms "
            f"({dedup_ratio:.1f}x deduplication)"
        )

        # Get coordinates for all atoms
        logger.info(f"  Building trajectory from {len(atom_ids):,} atom coordinates...")

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
        coord_map = {row[0]: (row[1], row[2], row[3]) for row in rows}

        # Build ordered coordinate list
        coordinates = [coord_map[aid] for aid in atom_ids]

        # Build LINESTRING WKT
        wkt = self.trajectory_builder.build_wkt(coordinates)

        logger.info(f"  ✓ Trajectory built: {len(coordinates):,} points")

        # Store trajectory as composition atom
        metadata = {
            "model_name": model_name,
            "tensor_name": tensor_name,
            "shape": list(tensor_data.shape),
            "dtype": str(tensor_data.dtype),
            "modality": "model-tensor-trajectory",
            "parameter_type": (
                "weight"
                if "weight" in tensor_name
                else "bias" if "bias" in tensor_name else "unknown"
            ),
            "total_elements": int(np.prod(tensor_data.shape)),
            "non_sparse_elements": len(non_sparse_weights),
            "unique_atoms": unique_atoms,
        }

        # Create composition atom with trajectory geometry
        trajectory_atom_id = await self.fractal_atomizer.get_or_create_composition(
            conn=conn,
            child_atom_ids=atom_ids,
            metadata=metadata,
            canonical_text=f"Tensor: {tensor_name}",
            spatial_wkt=wkt,  # Store as LINESTRING trajectory
        )

        self.stats["trajectories_created"] += 1

        logger.info(f"  ✓ Trajectory atom created: ID={trajectory_atom_id}")

        return trajectory_atom_id

    async def _atomize_vocabulary(self, conn: AsyncConnection, reader, model_name: str):
        """Atomize model vocabulary tokens."""
        logger.info("Atomizing vocabulary...")
        # TODO: Implement vocabulary atomization
        logger.info("✓ Vocabulary atomization (placeholder)")

    async def _atomize_architecture(
        self, conn: AsyncConnection, reader, model_name: str
    ):
        """Atomize model architecture metadata."""
        logger.info("Atomizing architecture metadata...")
        # TODO: Implement architecture atomization
        logger.info("✓ Architecture atomization (placeholder)")

    def get_stats(self) -> Dict[str, Any]:
        """Get atomization statistics."""
        return self.stats.copy()
