"""
AGE Sync Worker (EXPERIMENTAL - Not Recommended for Production)

DEPRECATED: Apache AGE development slowed significantly after October 2024
when Bitnine/AGEDB dismissed the entire development team. Use Neo4jProvenanceWorker
for production deployments instead.

This worker is kept for experimental purposes only.

Background worker that listens for PostgreSQL NOTIFY events
and syncs atom_relation changes to Apache AGE graph.

Uses LISTEN/NOTIFY for zero-latency async updates.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import asyncio
import logging
from typing import Optional

from psycopg import AsyncConnection
from psycopg.rows import dict_row
from psycopg_pool import AsyncConnectionPool

from api.config import settings

logger = logging.getLogger(__name__)


class AGESyncWorker:
    """
    Background worker for syncing PostgreSQL ? Apache AGE.

    Listens on channel: 'atom_created'
    Payload format: JSON with atom_id, content_hash, etc.

    Usage:
        worker = AGESyncWorker(connection_pool)
        await worker.start()  # Runs forever
    """

    def __init__(self, pool: AsyncConnectionPool):
        """
        Initialize worker.

        Args:
            pool: Database connection pool
        """
        self.pool = pool
        self.running = False
        self._conn: Optional[AsyncConnection] = None

    async def start(self) -> None:
        """
        Start worker (runs forever until cancelled).

        Raises:
            asyncio.CancelledError: When shutdown requested
        """
        self.running = True
        logger.info("AGE sync worker starting...")

        try:
            # Get dedicated connection for LISTEN
            async with self.pool.connection() as conn:
                self._conn = conn

                # Enable autocommit for LISTEN/NOTIFY
                await conn.set_autocommit(True)

                # Listen on channel
                await conn.execute("LISTEN atom_created;")
                logger.info("Listening on channel: atom_created")

                # Process notifications
                while self.running:
                    try:
                        # Wait for notification (with timeout)
                        async for notify in conn.notifies():
                            logger.debug(f"Received notification: {notify.payload}")

                            # Process in background to avoid blocking listener
                            asyncio.create_task(
                                self._process_notification(notify.payload)
                            )

                        # Sleep briefly if no notifications
                        await asyncio.sleep(settings.age_worker_poll_interval)

                    except asyncio.CancelledError:
                        logger.info("Worker cancelled, shutting down...")
                        break
                    except Exception as e:
                        logger.error(f"Error in worker loop: {e}", exc_info=True)
                        # Continue running despite errors
                        await asyncio.sleep(1)

        except asyncio.CancelledError:
            logger.info("Worker shutdown requested")
            raise
        except Exception as e:
            logger.error(f"Worker failed: {e}", exc_info=True)
            raise
        finally:
            self.running = False
            logger.info("AGE sync worker stopped")

    async def _process_notification(self, payload: str) -> None:
        """
        Process a single notification.

        Args:
            payload: JSON payload from NOTIFY
        """
        try:
            import json

            data = json.loads(payload)

            atom_id = data.get("atom_id")
            content_hash = data.get("content_hash")

            logger.info(f"Processing atom: {atom_id} (hash: {content_hash[:16]}...)")

            # Get separate connection from pool for processing
            async with self.pool.connection() as conn:
                async with conn.cursor(row_factory=dict_row) as cur:
                    # Fetch atom details
                    await cur.execute(
                        """
                        SELECT 
                            atom_id,
                            content_hash,
                            canonical_text,
                            metadata,
                            ST_AsText(spatial_key) as spatial_position
                        FROM atom
                        WHERE atom_id = %s;
                    """,
                        (atom_id,),
                    )

                    atom = await cur.fetchone()

                    if not atom:
                        logger.warning(f"Atom {atom_id} not found")
                        return

                    # Sync to AGE graph
                    await self._sync_to_age(conn, atom)

                    logger.debug(f"Successfully synced atom {atom_id} to AGE")

        except json.JSONDecodeError as e:
            logger.error(f"Invalid JSON payload: {payload} - {e}")
        except Exception as e:
            logger.error(f"Failed to process notification: {e}", exc_info=True)

    async def _sync_to_age(self, conn: AsyncConnection, atom: dict) -> None:
        """
        Sync atom to Apache AGE graph.

        Args:
            conn: Database connection
            atom: Atom data dictionary
        """
        try:
            # TODO: Implement actual AGE sync logic
            # For now, log the sync operation

            # Example AGE query (when AGE is properly configured):
            # await conn.execute("""
            #     SELECT * FROM ag_catalog.cypher('hartonomous', $$
            #         MERGE (a:Atom {atom_id: $atom_id})
            #         ON CREATE SET
            #             a.content_hash = $content_hash,
            #             a.canonical_text = $canonical_text,
            #             a.metadata = $metadata,
            #             a.spatial_position = $spatial_position
            #     $$) as (result agtype);
            # """, {
            #     "atom_id": atom["atom_id"],
            #     "content_hash": atom["content_hash"],
            #     "canonical_text": atom["canonical_text"],
            #     "metadata": atom["metadata"],
            #     "spatial_position": atom["spatial_position"]
            # })

            logger.info(
                f"[AGE SYNC] Atom {atom['atom_id']} "
                f"(text: {atom.get('canonical_text', 'N/A')[:50]}...)"
            )

            # Also sync related atoms if needed
            await self._sync_relations(conn, atom["atom_id"])

        except Exception as e:
            logger.error(f"AGE sync failed for atom {atom['atom_id']}: {e}")
            raise

    async def _sync_relations(self, conn: AsyncConnection, atom_id: int) -> None:
        """
        Sync atom relations to AGE.

        Args:
            conn: Database connection
            atom_id: Source atom ID
        """
        try:
            async with conn.cursor(row_factory=dict_row) as cur:
                # Fetch outgoing relations
                await cur.execute(
                    """
                    SELECT 
                        source_atom_id,
                        target_atom_id,
                        relation_type,
                        weight,
                        last_accessed
                    FROM atom_relation
                    WHERE source_atom_id = %s;
                """,
                    (atom_id,),
                )

                relations = await cur.fetchall()

                logger.debug(f"Found {len(relations)} relations for atom {atom_id}")

                # TODO: Implement actual AGE relation sync
                # For now, just log
                for rel in relations:
                    logger.debug(
                        f"[AGE SYNC] Relation: {rel['source_atom_id']} "
                        f"? {rel['target_atom_id']} "
                        f"(type: {rel['relation_type']}, weight: {rel['weight']})"
                    )

        except Exception as e:
            logger.error(f"Relation sync failed for atom {atom_id}: {e}")
            # Don't raise - relations are less critical than atoms


__all__ = ["AGESyncWorker"]
