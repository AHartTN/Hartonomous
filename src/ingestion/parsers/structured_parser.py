"""
Structured data parser - handles JSON, CSV, XML, etc.
"""

import csv
import hashlib
import json
from pathlib import Path
from typing import Any, Dict

from ...core.atomization import BaseAtomizer


class StructuredParser(BaseAtomizer):
    """Parse and atomize structured data via SQL functions."""

    async def parse(self, data_path: Path, conn) -> int:
        """
        Parse structured data file into atoms.

        Process:
        1. Detect format (JSON/CSV/XML)
        2. Parse structure
        3. Create parent atom for dataset
        4. Atomize each value
        5. Build composition from structure

        Returns parent atom_id.
        """
        with open(data_path, "rb") as f:
            data_bytes = f.read()

        format_type = self._detect_format(data_path.suffix)

        # Create parent atom
        data_hash = hashlib.sha256(data_bytes).digest()
        parent_atom_id = await self.create_atom(
            conn,
            data_hash,
            str(data_path.name),
            {
                "modality": "structured",
                "format": format_type,
                "file_path": str(data_path),
                "size_bytes": len(data_bytes),
            },
        )

        # Parse based on format
        if format_type == "json":
            await self._parse_json(data_bytes, parent_atom_id, conn)
        elif format_type == "csv":
            await self._parse_csv(data_bytes, parent_atom_id, conn)
        else:
            # Fallback: raw bytes - collect then batch
            chunk_size = 48
            component_ids = []
            sequence_indices = []
            for idx in range(0, len(data_bytes), chunk_size):
                chunk = data_bytes[idx : idx + chunk_size]
                component_id = await self.create_atom(
                    conn, chunk, None, {"raw_bytes": True}
                )
                component_ids.append(component_id)
                sequence_indices.append(idx // chunk_size)
            
            if component_ids:
                await self.create_compositions_batch(
                    conn, parent_atom_id, component_ids, sequence_indices
                )

        return parent_atom_id

    async def _parse_json(self, data_bytes: bytes, parent_atom_id: int, conn):
        """Parse JSON and atomize values."""
        data = json.loads(data_bytes.decode("utf-8"))

        # Flatten JSON to key-value pairs
        def flatten(obj, prefix=""):
            items = []
            if isinstance(obj, dict):
                for k, v in obj.items():
                    new_key = f"{prefix}.{k}" if prefix else k
                    items.extend(flatten(v, new_key))
            elif isinstance(obj, list):
                for i, v in enumerate(obj):
                    new_key = f"{prefix}[{i}]"
                    items.extend(flatten(v, new_key))
            else:
                items.append((prefix, obj))
            return items

        items = flatten(data)

        # Collect all value atoms first
        value_atom_ids = []
        for idx, (key, value) in enumerate(items):
            value_str = str(value)
            value_bytes = value_str.encode("utf-8")[:64]  # Enforce 64 byte limit

            value_atom_id = await self.create_atom(
                conn,
                value_bytes,
                value_str[:255],
                {"json_key": key, "json_value": True},
            )
            value_atom_ids.append(value_atom_id)
            self.stats["atoms_created"] += 1

        # Batch create all compositions
        if value_atom_ids:
            await self.create_compositions_batch(
                conn, parent_atom_id, value_atom_ids, list(range(len(value_atom_ids)))
            )

        self.stats["total_processed"] = len(items)

    async def _parse_csv(self, data_bytes: bytes, parent_atom_id: int, conn):
        """Parse CSV and atomize cells."""
        import io

        text = data_bytes.decode("utf-8")
        reader = csv.reader(io.StringIO(text))

        # Collect all cell atoms first
        cell_atom_ids = []
        composition_idx = 0
        for row in reader:
            for cell in row:
                cell_bytes = cell.encode("utf-8")[:64]

                cell_atom_id = await self.create_atom(
                    conn, cell_bytes, cell[:255], {"csv_cell": True}
                )
                cell_atom_ids.append(cell_atom_id)
                composition_idx += 1
                self.stats["atoms_created"] += 1

        # Batch create all compositions
        if cell_atom_ids:
            await self.create_compositions_batch(
                conn, parent_atom_id, cell_atom_ids, list(range(len(cell_atom_ids)))
            )

        self.stats["total_processed"] = composition_idx

    def _detect_format(self, extension: str) -> str:
        """Detect structured data format from extension."""
        ext_map = {
            ".json": "json",
            ".csv": "csv",
            ".xml": "xml",
            ".yaml": "yaml",
            ".yml": "yaml",
            ".toml": "toml",
        }
        return ext_map.get(extension.lower(), "unknown")
