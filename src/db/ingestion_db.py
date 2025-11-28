"""Database ingestion layer - writes atoms/landmarks/geometry to PostgreSQL."""

import json
import logging
from typing import Dict, List, Any, Optional

from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool

from ..core.atomization import Atom
from ..core.landmark import LandmarkPosition as Landmark

logger = logging.getLogger(__name__)


class IngestionDB:
    """
    Async database ingestion layer.
    Handles atomic writes with proper error handling and batching.
    """
    
    def __init__(self, pool: AsyncConnectionPool):
        """Initialize with connection pool instead of connection string."""
        self.pool = pool
        self._batch_size = 1000
    
    async def store_atom(self, atom: Atom) -> int:
        """
        Store single atom in database.
        Returns atom_id (existing or newly created).
        """
        async with self.pool.connection() as conn:
            async with conn.cursor() as cur:
                # Use atomize_value function which handles deduplication
                await cur.execute("""
                    SELECT atomize_value(
                        %s::bytea,
                        %s::text,
                        %s::jsonb
                    )
                """, (
                    atom.data,
                    atom.metadata.get('canonical_text', ''),
                    json.dumps(atom.metadata)
                ))
                
                result = await cur.fetchone()
                atom_id = result[0]
                
                logger.debug(f"Stored atom {atom_id}")
                return atom_id
    
    async def store_atoms_batch(self, atoms: List[Atom]) -> List[int]:
        """
        Batch store atoms efficiently.
        Returns list of atom_ids.
        """
        atom_ids = []
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cur:
                # Process each atom (psycopg3 doesn't have execute_batch equivalent)
                for atom in atoms:
                    await cur.execute("""
                        SELECT atomize_value(%s::bytea, %s::text, %s::jsonb)
                    """, (
                        atom.data,
                        atom.metadata.get('canonical_text', ''),
                        json.dumps(atom.metadata)
                    ))
                    
                    result = await cur.fetchone()
                    atom_ids.append(result[0])
                
                logger.info(f"Stored {len(atom_ids)} atoms in batch")
                return atom_ids
    
    async def create_composition(
        self,
        parent_atom_id: int,
        component_atom_ids: List[int],
        metadata: Optional[Dict] = None
    ) -> List[int]:
        """
        Create hierarchical composition using SQL function.
        Returns list of composition_ids.
        """
        composition_ids = []
        
        async with self.pool.connection() as conn:
            async with conn.cursor() as cur:
                for seq_idx, comp_id in enumerate(component_atom_ids):
                    await cur.execute("""
                        SELECT create_composition(
                            %s::bigint,
                            %s::bigint,
                            %s::bigint,
                            %s::jsonb
                        )
                    """, (
                        parent_atom_id,
                        comp_id,
                        seq_idx,
                        json.dumps(metadata or {})
                    ))
                    
                    result = await cur.fetchone()
                    if result:
                        composition_ids.append(result[0])
                
                logger.debug(f"Created {len(composition_ids)} compositions")
                return composition_ids
    
    async def create_relation(
        self,
        source_atom_id: int,
        target_atom_id: int,
        relation_type: str,
        weight: float = 0.5,
        metadata: Optional[Dict] = None
    ) -> int:
        """
        Create semantic relation between atoms.
        Returns relation_id.
        """
        async with self.pool.connection() as conn:
            async with conn.cursor() as cur:
                await cur.execute("""
                    SELECT create_relation(
                        %s::bigint,
                        %s::bigint,
                        %s::text,
                        %s::real,
                        %s::jsonb
                    )
                """, (
                    source_atom_id,
                    target_atom_id,
                    relation_type,
                    weight,
                    json.dumps(metadata or {})
                ))
                
                result = await cur.fetchone()
                relation_id = result[0]
                
                logger.debug(f"Created relation {relation_id}")
                return relation_id
