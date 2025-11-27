"""
Code Atomization Service Client

Calls the C# Roslyn/Tree-sitter microservice for deep code AST atomization.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import logging
from typing import Any, Dict, List, Optional

import httpx
from psycopg import AsyncConnection

logger = logging.getLogger(__name__)


class CodeAtomizerClient:
    """Client for Hartonomous Code Atomizer microservice (C# + Roslyn)."""

    def __init__(self, base_url: str = "http://localhost:8001"):
        self.base_url = base_url.rstrip("/")
        self.client = httpx.AsyncClient(timeout=30.0)

    async def atomize_csharp(
        self, code: str, filename: str = "code.cs", metadata: Optional[str] = None
    ) -> Dict[str, Any]:
        """
        Atomize C# code using Roslyn semantic analysis.

        Args:
            code: C# source code
            filename: Filename for metadata
            metadata: Optional JSON metadata

        Returns:
            Atomization result with atoms, compositions, relations
        """
        try:
            response = await self.client.post(
                f"{self.base_url}/api/v1/atomize/csharp",
                json={"code": code, "fileName": filename, "metadata": metadata},
            )
            response.raise_for_status()
            return response.json()
        except httpx.HTTPError as e:
            logger.error(f"Code atomization failed: {e}")
            raise

    async def atomize_file(
        self, file_path: str, language: str = "csharp"
    ) -> Dict[str, Any]:
        """
        Atomize code file via microservice.

        Args:
            file_path: Path to code file
            language: Language (csharp, python, etc.)

        Returns:
            Atomization result
        """
        with open(file_path, "rb") as f:
            files = {"file": (file_path, f, "text/plain")}

            try:
                response = await self.client.post(
                    f"{self.base_url}/api/v1/atomize/{language}/file", files=files
                )
                response.raise_for_status()
                return response.json()
            except httpx.HTTPError as e:
                logger.error(f"File atomization failed: {e}")
                raise

    async def health_check(self) -> bool:
        """
        Check if code atomizer service is healthy.

        Returns:
            True if healthy
        """
        try:
            response = await self.client.get(
                f"{self.base_url}/api/v1/atomize/health"
            )
            return response.status_code == 200
        except httpx.HTTPError:
            return False

    async def close(self):
        """Close HTTP client."""
        await self.client.aclose()


class CodeAtomizationService:
    """
    Service for atomizing code files using external C# microservice,
    then inserting results into PostgreSQL.
    """

    def __init__(self, atomizer_url: str = "http://localhost:8001"):
        self.client = CodeAtomizerClient(atomizer_url)

    async def atomize_and_insert(
        self,
        conn: AsyncConnection,
        code: str,
        filename: str,
        language: str = "csharp",
        metadata: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """
        Atomize code via microservice, then insert into database.

        Args:
            conn: Database connection
            code: Source code
            filename: Filename
            language: Programming language
            metadata: Optional metadata

        Returns:
            Insertion result
        """
        # Call microservice
        logger.info(
            f"Atomizing {language} code via microservice: {filename} ({len(code)} bytes)"
        )

        result = await self.client.atomize_csharp(code, filename)

        if not result.get("success"):
            raise ValueError("Atomization failed")

        # Extract atoms, compositions, relations
        atoms = result["atoms"]
        compositions = result["compositions"]
        relations = result["relations"]

        logger.info(
            f"Atomization complete: {len(atoms)} atoms, "
            f"{len(compositions)} compositions, {len(relations)} relations"
        )

        # Bulk insert atoms
        atom_id_map = await self._bulk_insert_atoms(conn, atoms)

        # Bulk insert compositions
        await self._bulk_insert_compositions(conn, compositions, atom_id_map)

        # Bulk insert relations
        await self._bulk_insert_relations(conn, relations, atom_id_map)

        return {
            "total_atoms": len(atoms),
            "unique_atoms": result["uniqueAtoms"],
            "compositions": len(compositions),
            "relations": len(relations),
        }

    async def _bulk_insert_atoms(
        self, conn: AsyncConnection, atoms: List[Dict[str, Any]]
    ) -> Dict[str, int]:
        """
        Bulk insert atoms into database.

        Returns:
            Mapping of content_hash (base64) to atom_id
        """
        import base64

        atom_id_map = {}

        async with conn.cursor() as cur:
            for atom in atoms:
                # Decode base64 hash
                content_hash = base64.b64decode(atom["contentHash"])

                # Check if atom exists (deduplication)
                await cur.execute(
                    "SELECT atom_id FROM atom WHERE content_hash = %s", (content_hash,)
                )
                existing = await cur.fetchone()

                if existing:
                    atom_id_map[atom["contentHash"]] = existing[0]
                    continue

                # Insert new atom
                spatial_key = f"SRID=0;POINTZ({atom['spatialKey']['x']} {atom['spatialKey']['y']} {atom['spatialKey']['z']})"

                await cur.execute(
                    """
                    INSERT INTO atom (
                        content_hash,
                        atomic_value,
                        canonical_text,
                        spatial_key,
                        modality,
                        subtype,
                        metadata
                    ) VALUES (
                        %s, %s, %s, ST_GeomFromEWKT(%s), %s, %s, %s::jsonb
                    ) RETURNING atom_id
                    """,
                    (
                        content_hash,
                        b"",  # AtomicValue not sent back from microservice (optimization)
                        atom["canonicalText"],
                        spatial_key,
                        atom["modality"],
                        atom.get("subtype"),
                        atom["metadata"],
                    ),
                )

                result = await cur.fetchone()
                atom_id_map[atom["contentHash"]] = result[0]

        logger.info(f"Inserted {len(atoms)} atoms")
        return atom_id_map

    async def _bulk_insert_compositions(
        self,
        conn: AsyncConnection,
        compositions: List[Dict[str, Any]],
        atom_id_map: Dict[str, int],
    ):
        """Bulk insert compositions."""
        async with conn.cursor() as cur:
            for comp in compositions:
                parent_id = atom_id_map.get(comp["parentHash"])
                component_id = atom_id_map.get(comp["componentHash"])

                if not parent_id or not component_id:
                    logger.warning("Skipping composition with missing atom IDs")
                    continue

                await cur.execute(
                    """
                    INSERT INTO atom_composition (
                        parent_atom_id,
                        component_atom_id,
                        sequence_index
                    ) VALUES (%s, %s, %s)
                    ON CONFLICT DO NOTHING
                    """,
                    (parent_id, component_id, comp["sequenceIndex"]),
                )

        logger.info(f"Inserted {len(compositions)} compositions")

    async def _bulk_insert_relations(
        self,
        conn: AsyncConnection,
        relations: List[Dict[str, Any]],
        atom_id_map: Dict[str, int],
    ):
        """Bulk insert relations."""
        async with conn.cursor() as cur:
            for rel in relations:
                source_id = atom_id_map.get(rel["sourceHash"])
                target_id = atom_id_map.get(rel["targetHash"])

                if not source_id or not target_id:
                    logger.warning("Skipping relation with missing atom IDs")
                    continue

                await cur.execute(
                    """
                    INSERT INTO atom_relation (
                        source_atom_id,
                        target_atom_id,
                        relation_type,
                        weight,
                        metadata
                    ) VALUES (%s, %s, %s, %s, %s::jsonb)
                    ON CONFLICT DO NOTHING
                    """,
                    (
                        source_id,
                        target_id,
                        rel["relationType"],
                        rel["weight"],
                        rel.get("metadata"),
                    ),
                )

        logger.info(f"Inserted {len(relations)} relations")


__all__ = ["CodeAtomizerClient", "CodeAtomizationService"]
