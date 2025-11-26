"""
Neo4j Provenance Sync Worker

Background worker that listens for PostgreSQL NOTIFY events
and syncs atom_composition changes to Neo4j provenance graph.

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


class Neo4jProvenanceWorker:
    """
    Background worker for syncing PostgreSQL ? Neo4j provenance graph.

    Listens on channels:
    - 'atom_created': When new atoms are created
    - 'composition_created': When atoms are composed (PROVENANCE)

    Builds Neo4j graph:
    (:Atom)-[:DERIVED_FROM]->(:Atom)

    Usage:
        worker = Neo4jProvenanceWorker(connection_pool)
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
        self._neo4j_driver = None

    async def start(self) -> None:
        """
        Start worker (runs forever until cancelled).

        Raises:
            asyncio.CancelledError: When shutdown requested
        """
        if not settings.neo4j_enabled:
            logger.info("Neo4j sync disabled in configuration")
            return

        self.running = True
        logger.info("Neo4j provenance worker starting...")

        try:
            # Initialize Neo4j driver
            await self._init_neo4j()

            # Get dedicated connection for LISTEN
            async with self.pool.connection() as conn:
                self._conn = conn

                # Enable autocommit for LISTEN/NOTIFY
                await conn.set_autocommit(True)

                # Listen on channels
                await conn.execute("LISTEN atom_created;")
                await conn.execute("LISTEN composition_created;")
                logger.info("Listening on channels: atom_created, composition_created")

                # Process notifications
                while self.running:
                    try:
                        # Wait for notification (with timeout)
                        async for notify in conn.notifies():
                            channel = notify.channel
                            payload = notify.payload

                            logger.debug(
                                f"Received notification on {channel}: {payload}"
                            )

                            # Process in background to avoid blocking listener
                            if channel == "atom_created":
                                asyncio.create_task(self._process_atom_created(payload))
                            elif channel == "composition_created":
                                asyncio.create_task(
                                    self._process_composition_created(payload)
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
            await self._close_neo4j()
            logger.info("Neo4j provenance worker stopped")

    async def _init_neo4j(self) -> None:
        """Initialize Neo4j driver."""
        try:
            from neo4j import AsyncGraphDatabase

            self._neo4j_driver = AsyncGraphDatabase.driver(
                settings.neo4j_uri, auth=(settings.neo4j_user, settings.neo4j_password)
            )

            # Test connection
            await self._neo4j_driver.verify_connectivity()

            logger.info(f"Connected to Neo4j at {settings.neo4j_uri}")

            # Create constraints/indexes
            await self._setup_neo4j_schema()

        except ImportError:
            logger.error("neo4j driver not installed: pip install neo4j")
            raise
        except Exception as e:
            logger.error(f"Failed to connect to Neo4j: {e}")
            raise

    async def _setup_neo4j_schema(self) -> None:
        """Create Neo4j constraints and indexes."""
        try:
            async with self._neo4j_driver.session(
                database=settings.neo4j_database
            ) as session:
                # Create constraint on Atom.atom_id (unique)
                await session.run(
                    """
                    CREATE CONSTRAINT atom_id_unique IF NOT EXISTS
                    FOR (a:Atom) REQUIRE a.atom_id IS UNIQUE
                """
                )

                # Create index on content_hash
                await session.run(
                    """
                    CREATE INDEX atom_content_hash IF NOT EXISTS
                    FOR (a:Atom) ON (a.content_hash)
                """
                )

                logger.info("Neo4j schema initialized")

        except Exception as e:
            logger.warning(f"Failed to setup Neo4j schema: {e}")
            # Non-fatal - continue anyway

    async def _close_neo4j(self) -> None:
        """Close Neo4j driver."""
        if self._neo4j_driver:
            await self._neo4j_driver.close()
            logger.info("Neo4j driver closed")

    async def _process_atom_created(self, payload: str) -> None:
        """
        Process atom_created notification.

        Creates atom node in Neo4j.

        Args:
            payload: JSON payload from NOTIFY
        """
        try:
            import json

            data = json.loads(payload)

            atom_id = data.get("atom_id")
            content_hash = data.get("content_hash")

            logger.debug(f"Processing atom creation: {atom_id}")

            # Get atom details from PostgreSQL
            async with self.pool.connection() as conn:
                async with conn.cursor(row_factory=dict_row) as cur:
                    await cur.execute(
                        """
                        SELECT 
                            atom_id,
                            content_hash,
                            canonical_text,
                            metadata
                        FROM atom
                        WHERE atom_id = %s;
                    """,
                        (atom_id,),
                    )

                    atom = await cur.fetchone()

                    if not atom:
                        logger.warning(f"Atom {atom_id} not found")
                        return

                    # Create node in Neo4j
                    await self._create_atom_node(atom)

        except json.JSONDecodeError as e:
            logger.error(f"Invalid JSON payload: {payload} - {e}")
        except Exception as e:
            logger.error(f"Failed to process atom_created: {e}", exc_info=True)

    async def _process_composition_created(self, payload: str) -> None:
        """
        Process composition_created notification.

        Creates DERIVED_FROM relationships in Neo4j (PROVENANCE).

        Args:
            payload: JSON payload from NOTIFY
        """
        try:
            import json

            data = json.loads(payload)

            parent_id = data.get("parent_atom_id")
            child_id = data.get("child_atom_id")
            position = data.get("position", 0)

            logger.info(
                f"Processing composition: {parent_id} ? {child_id} (pos: {position})"
            )

            # Create relationship in Neo4j
            await self._create_provenance_edge(parent_id, child_id, position)

        except json.JSONDecodeError as e:
            logger.error(f"Invalid JSON payload: {payload} - {e}")
        except Exception as e:
            logger.error(f"Failed to process composition_created: {e}", exc_info=True)

    async def _create_atom_node(self, atom: dict) -> None:
        """
        Create atom node in Neo4j.

        Args:
            atom: Atom data dictionary
        """
        try:
            async with self._neo4j_driver.session(
                database=settings.neo4j_database
            ) as session:
                await session.run(
                    """
                    MERGE (a:Atom {atom_id: $atom_id})
                    SET a.content_hash = $content_hash,
                        a.canonical_text = $canonical_text,
                        a.metadata = $metadata,
                        a.created_at = datetime()
                """,
                    atom_id=atom["atom_id"],
                    content_hash=atom["content_hash"],
                    canonical_text=atom.get("canonical_text"),
                    metadata=atom.get("metadata"),
                )

                logger.debug(f"Created Neo4j node for atom {atom['atom_id']}")

        except Exception as e:
            logger.error(f"Failed to create Neo4j node for atom {atom['atom_id']}: {e}")
            raise

    async def _create_provenance_edge(
        self, parent_id: int, child_id: int, position: int
    ) -> None:
        """
        Create DERIVED_FROM relationship in Neo4j.

        This is the core provenance tracking:
        (parent:Atom)-[:DERIVED_FROM {position}]->(child:Atom)

        Args:
            parent_id: Parent atom ID (the new composed atom)
            child_id: Child atom ID (the source atom)
            position: Position in composition
        """
        try:
            async with self._neo4j_driver.session(
                database=settings.neo4j_database
            ) as session:
                # Ensure both nodes exist first
                await session.run(
                    """
                    MERGE (parent:Atom {atom_id: $parent_id})
                    MERGE (child:Atom {atom_id: $child_id})
                """,
                    parent_id=parent_id,
                    child_id=child_id,
                )

                # Create provenance relationship
                await session.run(
                    """
                    MATCH (parent:Atom {atom_id: $parent_id})
                    MATCH (child:Atom {atom_id: $child_id})
                    MERGE (parent)-[r:DERIVED_FROM {position: $position}]->(child)
                    SET r.created_at = datetime()
                """,
                    parent_id=parent_id,
                    child_id=child_id,
                    position=position,
                )

                logger.info(
                    f"Created provenance edge: "
                    f"Atom({parent_id})-[:DERIVED_FROM {{position: {position}}}]->"
                    f"Atom({child_id})"
                )

        except Exception as e:
            logger.error(
                f"Failed to create provenance edge " f"{parent_id} ? {child_id}: {e}"
            )
            raise


__all__ = ["Neo4jProvenanceWorker"]
