"""
Atomizer - properly delegates to SQL atomize_value() for content-addressing.
Follows BaseAtomizer pattern for all atomization operations.
"""

import hashlib
from typing import Any, Dict, Optional

import numpy as np
from psycopg import AsyncConnection

from .base_atomizer import BaseAtomizer


class Atomizer(BaseAtomizer):
    """
    Generic atomizer for arrays and binary data.
    Delegates to SQL functions for proper content-addressing.
    """

    async def atomize_array(
        self,
        conn: AsyncConnection,
        data: np.ndarray,
        modality: str,
        parent_metadata: Optional[Dict[str, Any]] = None,
    ) -> int:
        """
        Atomize numpy array by calling SQL functions.
        Returns parent atom_id.
        """
        if parent_metadata is None:
            parent_metadata = {}

        # Create parent atom
        parent_hash = hashlib.sha256(data.tobytes()).digest()
        parent_atom_id = await self.create_atom(
            conn,
            parent_hash,
            None,
            {
                **parent_metadata,
                "modality": modality,
                "shape": list(data.shape),
                "dtype": str(data.dtype),
            },
        )

        # Chunk and atomize components
        flat_data = data.flatten()
        chunk_size = max(1, 48 // data.dtype.itemsize)

        sequence_idx = 0
        for i in range(0, len(flat_data), chunk_size):
            chunk = flat_data[i : i + chunk_size]

            self.stats["total_processed"] += 1

            # Skip near-zero chunks (sparse)
            if np.abs(chunk).max() < self.threshold:
                self.stats["sparse_skipped"] += 1
                continue

            chunk_bytes = chunk.tobytes()

            # Atomize chunk
            component_atom_id = await self.create_atom(
                conn, chunk_bytes, None, {"dtype": str(chunk.dtype)}
            )

            # Create composition
            await self.create_composition(
                conn, parent_atom_id, component_atom_id, sequence_idx
            )
            sequence_idx += 1

            self.stats["atoms_created"] += 1

        return parent_atom_id
