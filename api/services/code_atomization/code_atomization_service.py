"""Code atomization service for database insertion."""

import logging
import json
import base64
from typing import Any, Dict, List, Optional

from psycopg import AsyncConnection

from .code_atomizer_client import CodeAtomizerClient

logger = logging.getLogger(__name__)


class CodeAtomizationService:
    """
    Service for atomizing code files using external C# microservice,
    then inserting results into PostgreSQL.
    """

    def __init__(self):
        self.client = CodeAtomizerClient()

    async def atomize_and_insert(
        self,
        conn: AsyncConnection,
        code: str,
        filename: str,
        language: str = "csharp",
        metadata: Optional[Dict[str, Any]] = None,
    ) -> Dict[str, Any]:
        """Atomize code via microservice, then insert into database."""
        logger.info(
            f"Atomizing {language} code via microservice: {filename} ({len(code)} bytes)"
        )

        result = await self.client.atomize_csharp(code, filename)

        if not result.get("success"):
            raise ValueError("Atomization failed")

        atoms = result["atoms"]
        compositions = result["compositions"]
        relations = result["relations"]

        logger.info(
            f"Atomization complete: {len(atoms)} atoms, "
            f"{len(compositions)} compositions, {len(relations)} relations"
        )

        atom_id_map = await self._bulk_insert_atoms(conn, atoms)
        await self._bulk_insert_compositions(conn, compositions, atom_id_map)
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
        """Bulk insert atoms into database."""
        atom_id_map = {}

        async with conn.cursor() as cur:
            for atom in atoms:
                content_hash = base64.b64decode(atom["contentHash"])

                await cur.execute(
                    "SELECT atom_id FROM atom WHERE content_hash = %s", (content_hash,)
                )
                existing = await cur.fetchone()

                if existing:
                    atom_id_map[atom["contentHash"]] = existing[0]
                    continue

                metadata = json.loads(atom["metadata"])
                hilbert_index = metadata.get("hilbertIndex", 0)

                spatial_key = (
                    f"SRID=0;POINTZM("
                    f"{atom['spatialKey']['x']} "
                    f"{atom['spatialKey']['y']} "
                    f"{atom['spatialKey']['z']} "
                    f"{hilbert_index})"
                )

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
                        b"",
                        atom["canonicalText"],
                        spatial_key,
                        atom["modality"],
                        atom.get("subtype"),
                        atom["metadata"],
                    ),
                )

                result = await cur.fetchone()
                atom_id_map[atom["contentHash"]] = result[0]

        logger.info(f"Inserted {len(atoms)} atoms with Hilbert spatial indexing")
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
