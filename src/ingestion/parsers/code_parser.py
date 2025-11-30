"""
Code parser - handles code with AST-level atomization."""

import base64
import hashlib
import json
import os
from pathlib import Path
from typing import Any, Dict, Optional

import httpx

from src.core.atomization import BaseAtomizer


class CodeParser(BaseAtomizer):
    """Parse and atomize source code via C# atomizer service."""

    def __init__(self, atomizer_service_url: Optional[str] = None):
        super().__init__()
        self.service_url = atomizer_service_url or os.getenv(
            "CODE_ATOMIZER_URL", "http://localhost:8001"  # Local development default
        )
        self._health_checked = False

    async def _check_health(self) -> bool:
        """Check if C# CodeAtomizer service is available."""
        if self._health_checked:
            return True

        try:
            async with httpx.AsyncClient(timeout=5.0) as client:
                response = await client.get(f"{self.service_url}/api/v1/atomize/health")
                self._health_checked = response.status_code == 200
                return self._health_checked
        except Exception:
            return False

    async def parse(self, code_path: Path, conn) -> int:
        """
        Parse code file into atoms via C# CodeAtomizer service.

        Process:
        1. Check C# service health
        2. Read code file
        3. Call C# service for AST decomposition
        4. Insert atoms with proper spatial coordinates
        5. Insert compositions and relations

        Returns parent atom_id (root file atom).
        """
        # Check service health
        if not await self._check_health():
            raise RuntimeError(
                f"Code Atomizer service unavailable at {self.service_url}. "
                f"Ensure the C# service is running (dotnet run or docker-compose up code-atomizer)."
            )

        # Read code file
        with open(code_path, "r", encoding="utf-8") as f:
            code = f.read()

        language = self._detect_language(code_path.suffix)

        # Call C# atomizer service
        async with httpx.AsyncClient(timeout=30.0) as client:
            response = await client.post(
                f"{self.service_url}/api/v1/atomize/{language}",
                json={"code": code, "fileName": str(code_path.name)},
            )
            response.raise_for_status()
            result = response.json()

        if not result.get("success"):
            raise RuntimeError(
                f"Atomization failed: {result.get('error', 'Unknown error')}"
            )

        # Parse response
        atoms = result["atoms"]
        compositions = result["compositions"]
        relations = result["relations"]

        self.stats["total_processed"] = len(atoms)

        # Insert atoms with proper base64 decoding and spatial coordinates
        hash_to_id = {}

        async with conn.cursor() as cur:
            for atom in atoms:
                # Decode content hash from base64 (not hex!)
                content_hash = base64.b64decode(atom["contentHash"])

                # Check if atom already exists (deduplication)
                await cur.execute(
                    "SELECT atom_id FROM atom WHERE content_hash = %s", (content_hash,)
                )
                existing = await cur.fetchone()

                if existing:
                    hash_to_id[atom["contentHash"]] = existing[0]
                    continue

                # Parse metadata to extract Hilbert index
                metadata = (
                    json.loads(atom["metadata"])
                    if isinstance(atom["metadata"], str)
                    else atom["metadata"]
                )
                hilbert_index = metadata.get("hilbertIndex", 0)

                # Add modality and subtype to metadata
                metadata["modality"] = atom.get("modality", "code")
                if "subtype" in atom:
                    metadata["subtype"] = atom["subtype"]

                # Build POINTZM geometry with Hilbert M coordinate
                spatial_wkt = (
                    f"SRID=0;POINTZM("
                    f"{atom['spatialKey']['x']} "
                    f"{atom['spatialKey']['y']} "
                    f"{atom['spatialKey']['z']} "
                    f"{hilbert_index})"
                )

                # Insert atom with spatial coordinates
                await cur.execute(
                    """
                    INSERT INTO atom (
                        content_hash,
                        atomic_value,
                        canonical_text,
                        spatial_key,
                        metadata
                    ) VALUES (
                        %s, %s, %s,
                        ST_GeomFromEWKT(%s),
                        %s::jsonb
                    ) RETURNING atom_id
                    """,
                    (
                        content_hash,
                        b"",  # atomic_value is empty for AST nodes
                        atom["canonicalText"],
                        spatial_wkt,
                        json.dumps(metadata),
                    ),
                )

                result_row = await cur.fetchone()
                atom_id = result_row[0]
                hash_to_id[atom["contentHash"]] = atom_id
                self.stats["atoms_created"] += 1

        # Batch insert compositions
        comp_parent_ids = []
        comp_component_ids = []
        comp_sequence_indices = []

        for comp in compositions:
            parent_id = hash_to_id.get(comp["parentHash"])
            component_id = hash_to_id.get(comp["componentHash"])

            if parent_id and component_id:
                comp_parent_ids.append(parent_id)
                comp_component_ids.append(component_id)
                comp_sequence_indices.append(comp["sequenceIndex"])

        if comp_parent_ids:
            async with conn.cursor() as cur:
                await cur.execute(
                    """
                    INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                    SELECT * FROM UNNEST(%s::bigint[], %s::bigint[], %s::integer[])
                    ON CONFLICT (parent_atom_id, component_atom_id, sequence_index) DO NOTHING
                    """,
                    (comp_parent_ids, comp_component_ids, comp_sequence_indices),
                )
                self.stats["compositions_created"] = len(comp_parent_ids)

        # Insert relations using SQL function
        for rel in relations:
            source_id = hash_to_id.get(rel["sourceHash"])
            target_id = hash_to_id.get(rel["targetHash"])

            if source_id and target_id:
                async with conn.cursor() as cur:
                    await cur.execute(
                        "SELECT create_relation(%s, %s, %s, %s, '{}'::jsonb)",
                        (source_id, target_id, rel["relationType"], rel["weight"]),
                    )
                    self.stats["relations_created"] += 1

        # Return root file atom ID
        file_atom_hash = next(
            (
                hash_key
                for hash_key, atom in zip(hash_to_id.keys(), atoms)
                if json.loads(atom["metadata"]).get("nodeType") == "file"
            ),
            None,
        )

        return (
            hash_to_id.get(file_atom_hash)
            if file_atom_hash
            else list(hash_to_id.values())[0]
        )

    def _detect_language(self, extension: str) -> str:
        """Detect programming language from file extension."""
        ext_map = {
            ".py": "python",
            ".cs": "csharp",
            ".js": "javascript",
            ".ts": "typescript",
            ".java": "java",
            ".cpp": "cpp",
            ".c": "c",
            ".go": "go",
            ".rs": "rust",
            ".rb": "ruby",
            ".php": "php",
        }
        return ext_map.get(extension.lower(), "text")
