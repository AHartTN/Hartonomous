"""Base atomizer for SQL-based atomization."""

import hashlib
import logging
from typing import Any, Dict, Optional
from psycopg import AsyncConnection
from psycopg.types.json import Json

logger = logging.getLogger(__name__)


class BaseAtomizer:
    """Base class for all atomizers - handles SQL atomize_value calls."""
    
    def __init__(self, threshold: float = 0.01):
        self.threshold = threshold
        self.cache: Dict[Any, int] = {}
        self.stats = {
            "total_processed": 0,
            "atoms_created": 0,
            "atoms_deduped": 0,
            "sparse_skipped": 0,
        }
    
    async def create_atom(
        self,
        conn: AsyncConnection,
        value: bytes,
        canonical_text: Optional[str],
        metadata: Dict[str, Any]
    ) -> int:
        """Create or retrieve atom via SQL atomize_value()."""
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (value, canonical_text, Json(metadata))
            )
            return (await cur.fetchone())[0]
    
    async def create_composition(
        self,
        conn: AsyncConnection,
        parent_id: int,
        component_id: int,
        sequence_idx: int
    ):
        """Link component to parent via atom_composition."""
        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                VALUES (%s, %s, %s)
                ON CONFLICT DO NOTHING
                """,
                (parent_id, component_id, sequence_idx)
            )
