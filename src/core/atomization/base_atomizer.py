"""Base atomizer for SQL-based atomization."""

import hashlib
import json
import logging
from typing import Any, Dict, Optional

from psycopg import AsyncConnection

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
        metadata: Dict[str, Any],
        spatial_key: Optional[str] = None,
    ) -> int:
        """
        Create or retrieve atom via SQL atomize_value().

        Args:
            conn: Database connection
            value: Hash bytes for content addressing
            canonical_text: Human-readable text representation
            metadata: JSONB metadata dictionary
            spatial_key: Optional WKT string for spatial geometry (e.g., "POINT ZM (0.5 0.3 0.8 12345)")

        Returns:
            Atom ID
        """
        if spatial_key:
            # Use atomize_value_spatial() for geometric atoms
            async with conn.cursor() as cur:
                await cur.execute(
                    "SELECT atomize_value_spatial(%s::bytea, %s, %s::jsonb, ST_GeomFromText(%s))",
                    (value, canonical_text, json.dumps(metadata), spatial_key),
                )
                return (await cur.fetchone())[0]
        else:
            # Standard atomize_value() for non-spatial atoms
            async with conn.cursor() as cur:
                await cur.execute(
                    "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                    (value, canonical_text, json.dumps(metadata)),
                )
                return (await cur.fetchone())[0]

    async def create_composition(
        self,
        conn: AsyncConnection,
        parent_id: int,
        component_id: int,
        sequence_idx: int,
    ):
        """Link component to parent via SQL create_composition() function."""
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT create_composition(
                    %s::bigint,
                    %s::bigint,
                    %s::bigint,
                    '{}'::jsonb
                )
                """,
                (parent_id, component_id, sequence_idx),
            )

    async def create_compositions_batch(
        self,
        conn: AsyncConnection,
        parent_id: int,
        component_ids: list,
        sequence_indices: list,
    ):
        """Batch create compositions using UNNEST for massive speedup."""
        if not component_ids:
            return

        import time

        count = len(component_ids)
        start = time.time()
        print(f"  → Inserting {count:,} composition records...", flush=True)

        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                SELECT %s, * FROM UNNEST(%s::bigint[], %s::integer[])
                ON CONFLICT (parent_atom_id, component_atom_id, sequence_index) DO NOTHING
                """,
                (parent_id, component_ids, sequence_indices),
            )

        elapsed = time.time() - start
        rate = count / elapsed if elapsed > 0 else 0
        print(
            f"  → Inserted {count:,} compositions ({elapsed:.2f}s, {rate:,.0f} comps/s)",
            flush=True,
        )
