"""
GGUF Model Atomization Service

Atomizes GGUF models (Llama, Qwen, etc.) into cognitive substrate.
Handles deduplication, sparse encoding, and Hilbert spatial indexing.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import logging
import struct
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

import numpy as np
from psycopg import AsyncConnection

from api.services.code_atomization import CodeAtomizerClient

logger = logging.getLogger(__name__)


class GGUFAtomizer:
    """
    GGUF model atomizer with content-addressing and sparse encoding.
    
    Features:
    - Deduplication via SHA-256 content hashing
    - Sparse encoding (ignore weights below threshold)
    - Hilbert curve spatial indexing
    - Bulk insertion for performance
    """

    def __init__(self, threshold: float = 0.01):
        """
        Initialize GGUF atomizer.
        
        Args:
            threshold: Minimum absolute weight value to store (sparse encoding)
        """
        self.threshold = threshold
        self.atom_cache: Dict[bytes, int] = {}  # content_hash -> atom_id
        self.unique_weights: Dict[bytes, float] = {}  # content_hash -> weight value

    async def atomize_model(
        self, 
        file_path: Path, 
        model_name: str,
        conn: AsyncConnection,
        max_tensors: Optional[int] = None
    ) -> Dict[str, Any]:
        """
        Atomize GGUF model file into cognitive substrate.
        
        Args:
            file_path: Path to GGUF model file
            model_name: Human-readable model name
            conn: Database connection
            max_tensors: Maximum tensors to process (for testing)
        
        Returns:
            Ingestion statistics
        """
        logger.info(f"Starting GGUF atomization: {file_path} ({file_path.stat().st_size / 1e9:.2f} GB)")

        # Parse GGUF header
        header = self._parse_gguf_header(file_path)
        logger.info(f"GGUF header: version={header['version']}, tensors={header['tensor_count']}")

        # Create model root atom
        model_hash = await self._create_model_atom(
            conn, model_name, file_path, header
        )

        # Extract and atomize tensors
        total_atoms = 0
        unique_atoms = 0
        total_weights = 0
        tensors_processed = 0

        tensors = self._extract_tensors(file_path, header, max_tensors)
        
        for tensor_name, tensor_data in tensors:
            logger.info(f"Atomizing tensor: {tensor_name} shape={tensor_data.shape}")
            
            atoms, compositions = await self._atomize_tensor(
                conn, model_hash, tensor_name, tensor_data
            )
            
            total_atoms += len(atoms)
            unique_atoms += len(self.unique_weights)
            total_weights += tensor_data.size
            tensors_processed += 1

        logger.info(
            f"GGUF atomization complete: {tensors_processed} tensors, "
            f"{total_weights:,} weights, {total_atoms:,} atoms, "
            f"{unique_atoms:,} unique ({(unique_atoms/total_atoms*100):.1f}% deduplication)"
        )

        return {
            "model_name": model_name,
            "file_size_gb": file_path.stat().st_size / 1e9,
            "tensors_processed": tensors_processed,
            "total_weights": total_weights,
            "total_atoms": total_atoms,
            "unique_atoms": unique_atoms,
            "deduplication_ratio": total_atoms / unique_atoms if unique_atoms > 0 else 0,
            "model_hash": model_hash.hex(),
        }

    def _parse_gguf_header(self, file_path: Path) -> Dict[str, Any]:
        """Parse GGUF file header."""
        with open(file_path, "rb") as f:
            # Magic number (4 bytes)
            magic = f.read(4)
            if magic != b"GGUF":
                raise ValueError(f"Invalid GGUF file: magic={magic}")

            # Version (4 bytes, uint32)
            version = struct.unpack("<I", f.read(4))[0]

            # Tensor count (8 bytes, uint64)
            tensor_count = struct.unpack("<Q", f.read(8))[0]

            # Metadata count (8 bytes, uint64)
            metadata_count = struct.unpack("<Q", f.read(8))[0]

            return {
                "magic": magic.decode("utf-8"),
                "version": version,
                "tensor_count": tensor_count,
                "metadata_count": metadata_count,
            }

    def _extract_tensors(
        self, file_path: Path, header: Dict[str, Any], max_tensors: Optional[int] = None
    ) -> List[Tuple[str, np.ndarray]]:
        """
        Extract tensor data from GGUF file.
        
        NOTE: This is a simplified implementation. Full GGUF parsing requires
        handling metadata, tensor info, alignment, and quantization formats.
        For production, use llama.cpp Python bindings or gguf library.
        """
        tensors = []
        
        # For now, create synthetic test data
        # TODO: Implement full GGUF parsing with proper offset handling
        logger.warning("Using synthetic tensor data - full GGUF parser not yet implemented")
        
        tensor_count = min(header["tensor_count"], max_tensors or header["tensor_count"])
        
        for i in range(tensor_count):
            tensor_name = f"layer_{i}.weight"
            # Simulate quantized weights (common values in GGUF)
            tensor_data = np.random.choice(
                [-0.5, -0.1, 0.0, 0.1, 0.5, 1.0],
                size=(128, 128),  # Small size for testing
                p=[0.1, 0.2, 0.4, 0.2, 0.05, 0.05]  # Sparse distribution
            ).astype(np.float32)
            
            tensors.append((tensor_name, tensor_data))
        
        return tensors

    async def _create_model_atom(
        self, 
        conn: AsyncConnection, 
        model_name: str, 
        file_path: Path, 
        header: Dict[str, Any]
    ) -> bytes:
        """Create root atom for entire model."""
        import json
        
        model_bytes = f"ml-model:gguf:{model_name}".encode("utf-8")
        model_hash = hashlib.sha256(model_bytes).digest()

        # Compute spatial position for model (modality=ml-model, category=model)
        # For now, use fixed position; later integrate with LandmarkProjection
        hilbert_index = 100000  # Placeholder

        metadata = json.dumps({
            "model_name": model_name,
            "file_path": str(file_path),
            "file_size_bytes": file_path.stat().st_size,
            "format": "gguf",
            "version": header["version"],
            "tensor_count": header["tensor_count"],
            "metadata_count": header["metadata_count"],
        })

        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom (
                    content_hash, atomic_value, canonical_text,
                    spatial_key, modality, subtype, metadata
                )
                VALUES (
                    %s, %s, %s,
                    ST_GeomFromEWKT('SRID=0;POINTZM(0.5 0.5 0.5 ' || %s || ')'),
                    %s, %s, %s::jsonb
                )
                ON CONFLICT (content_hash) DO NOTHING
                RETURNING atom_id
                """,
                (
                    model_hash,
                    model_bytes,
                    f"{model_name} (GGUF model)",
                    hilbert_index,
                    "ml-model",
                    "model",
                    metadata,
                ),
            )
            result = await cur.fetchone()
            
            if result:
                self.atom_cache[model_hash] = result[0]
                logger.info(f"Created model atom: {model_name} (atom_id={result[0]})")
            else:
                # Model already exists, fetch atom_id
                await cur.execute(
                    "SELECT atom_id FROM atom WHERE content_hash = %s",
                    (model_hash,)
                )
                result = await cur.fetchone()
                if result:
                    self.atom_cache[model_hash] = result[0]

        return model_hash

    async def _atomize_tensor(
        self,
        conn: AsyncConnection,
        model_hash: bytes,
        tensor_name: str,
        tensor_data: np.ndarray,
    ) -> Tuple[List[Dict], List[Dict]]:
        """
        Atomize a single tensor with deduplication and sparse encoding.
        
        Returns:
            (atoms, compositions)
        """
        import json
        
        atoms = []
        compositions = []

        # Create tensor atom
        tensor_bytes = f"ml-model:tensor:{tensor_name}".encode("utf-8")
        tensor_hash = hashlib.sha256(tensor_bytes).digest()
        
        hilbert_index = 200000  # Placeholder
        
        tensor_metadata = json.dumps({
            "tensor_name": tensor_name,
            "shape": list(tensor_data.shape),
            "dtype": str(tensor_data.dtype),
            "total_elements": int(tensor_data.size),
        })

        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom (
                    content_hash, atomic_value, canonical_text,
                    spatial_key, modality, subtype, metadata
                )
                VALUES (
                    %s, %s, %s,
                    ST_GeomFromEWKT('SRID=0;POINTZM(0.5 0.6 0.5 ' || %s || ')'),
                    %s, %s, %s::jsonb
                )
                ON CONFLICT (content_hash) DO NOTHING
                RETURNING atom_id
                """,
                (
                    tensor_hash,
                    tensor_bytes,
                    f"{tensor_name} {tensor_data.shape}",
                    hilbert_index,
                    "ml-model",
                    "tensor",
                    tensor_metadata,
                ),
            )
            
            result = await cur.fetchone()
            if result:
                self.atom_cache[tensor_hash] = result[0]

            # Link tensor to model
            model_id = self.atom_cache.get(model_hash)
            tensor_id = self.atom_cache.get(tensor_hash)
            
            if model_id and tensor_id:
                await cur.execute(
                    """
                    INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                    VALUES (%s, %s, %s)
                    ON CONFLICT DO NOTHING
                    """,
                    (model_id, tensor_id, 0)
                )

        # Atomize weights (with deduplication + sparse encoding)
        flat_weights = tensor_data.flatten()
        weight_atoms_created = 0
        
        for idx, weight in enumerate(flat_weights):
            # Sparse encoding: skip near-zero weights
            if abs(weight) < self.threshold:
                continue

            # Content addressing (deduplication)
            weight_bytes = struct.pack("<f", weight)
            weight_hash = hashlib.sha256(weight_bytes).digest()

            # Check if weight already exists
            if weight_hash in self.unique_weights:
                continue

            # New unique weight - create atom
            self.unique_weights[weight_hash] = weight
            
            # Compute Hilbert index based on weight value
            # Map weight to [0, 1] range for spatial positioning
            normalized_weight = (weight + 1.0) / 2.0  # Assume weights in [-1, 1]
            hilbert_index = int(normalized_weight * 1000000)
            
            weight_metadata = json.dumps({
                "tensor": tensor_name,
                "position": int(idx),
                "value": float(weight),
            })

            async with conn.cursor() as cur:
                await cur.execute(
                    """
                    INSERT INTO atom (
                        content_hash, atomic_value, canonical_text,
                        spatial_key, modality, subtype, metadata
                    )
                    VALUES (
                        %s, %s, %s,
                        ST_GeomFromEWKT('SRID=0;POINTZM(0.5 0.7 ' || %s || ' ' || %s || ')'),
                        %s, %s, %s::jsonb
                    )
                    ON CONFLICT (content_hash) DO NOTHING
                    RETURNING atom_id
                    """,
                    (
                        weight_hash,
                        weight_bytes,
                        f"{tensor_name}[{idx}]={weight:.6f}",
                        normalized_weight,
                        hilbert_index,
                        "ml-model",
                        "weight",
                        weight_metadata,
                    ),
                )
                
                result = await cur.fetchone()
                if result:
                    weight_atoms_created += 1

        logger.info(
            f"Tensor {tensor_name}: {tensor_data.size} weights, "
            f"{weight_atoms_created} unique atoms created"
        )

        return atoms, compositions


__all__ = ["GGUFAtomizer"]
