"""
Atomizer - delegates to SQL atomize_value() for proper content-addressing.
"""

import hashlib
import numpy as np
from typing import Any, Dict, List, Tuple, Optional

from .atom import Atom
from .modality_type import ModalityType


class Atomizer:
    """
    Atomizer that properly delegates to SQL functions.
    Should be used with database connection to call atomize_value().
    """
    
    def __init__(self, db_connection=None, sparse_threshold: float = 1e-6):
        self.conn = db_connection
        self.sparse_threshold = sparse_threshold
        self.atom_cache: Dict[bytes, int] = {}
    
    async def atomize_array_to_db(
        self,
        data: np.ndarray,
        modality: str,
        parent_metadata: Dict[str, Any]
    ) -> int:
        """
        Atomize array by calling SQL functions.
        Returns parent atom_id.
        """
        if self.conn is None:
            raise RuntimeError("Database connection required for atomization")
        
        # Create parent atom
        parent_hash = hashlib.sha256(data.tobytes()).digest()
        
        async with self.conn.cursor() as cur:
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    NULL,
                    %s::jsonb
                )
                """,
                (parent_hash, {
                    **parent_metadata,
                    'modality': modality,
                    'shape': list(data.shape),
                    'dtype': str(data.dtype)
                })
            )
            parent_atom_id = (await cur.fetchone())[0]
        
        # Chunk and atomize components
        flat_data = data.flatten()
        chunk_size = max(1, 48 // data.dtype.itemsize)
        
        for i in range(0, len(flat_data), chunk_size):
            chunk = flat_data[i:i+chunk_size]
            
            # Skip near-zero chunks (sparse)
            if np.abs(chunk).max() < self.sparse_threshold:
                continue
            
            chunk_bytes = chunk.tobytes()
            
            # Atomize chunk
            async with self.conn.cursor() as cur:
                await cur.execute(
                    """
                    SELECT atomize_value(
                        %s::bytea,
                        NULL,
                        %s::jsonb
                    )
                    """,
                    (chunk_bytes, {'dtype': str(chunk.dtype)})
                )
                component_atom_id = (await cur.fetchone())[0]
            
            # Create composition
            async with self.conn.cursor() as cur:
                await cur.execute(
                    """
                    INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                    VALUES (%s, %s, %s)
                    ON CONFLICT DO NOTHING
                    """,
                    (parent_atom_id, component_atom_id, i // chunk_size)
                )
        
        return parent_atom_id
