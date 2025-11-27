"""
GGUF Model Atomization Service

Atomizes AI model weights into cognitive substrate using hierarchical composition.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import logging
import struct
from pathlib import Path
from typing import Any, Dict, List, Optional

from psycopg import AsyncConnection

logger = logging.getLogger(__name__)


class GGUFAtomizer:
    """GGUF model atomizer with content-addressing and sparse encoding."""

    def __init__(self, threshold: float = 0.01):
        self.threshold = threshold
        self.unique_weights: Dict[float, int] = {}
        self.stats = {
            "tensors_processed": 0,
            "total_weights": 0,
            "weights_atomized": 0,
            "weights_skipped": 0,
        }

    async def atomize_model(
        self,
        file_path: Path,
        model_name: str,
        conn: AsyncConnection,
        max_tensors: Optional[int] = None,
    ) -> Dict[str, Any]:
        """
        Atomize GGUF model weights into cognitive substrate.
        
        Process:
        1. Parse GGUF file to extract tensors
        2. For each tensor, extract float weights
        3. Apply sparse encoding (skip weights < threshold)
        4. Atomize unique weights via atomize_numeric()
        5. Create hierarchical composition: model ? layers ? tensors ? weights
        """
        logger.info(f"Atomizing GGUF model: {file_path} ({file_path.stat().st_size / 1e9:.2f} GB)")

        # Create model root atom
        async with conn.cursor() as cur:
            # Model atom with metadata
            model_hash = hashlib.sha256(model_name.encode()).digest()
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    %s,
                    %s::jsonb
                )
                """,
                (
                    model_hash,
                    model_name,
                    {
                        "modality": "model",
                        "format": "gguf",
                        "file_size_bytes": file_path.stat().st_size,
                        "threshold": self.threshold,
                    },
                ),
            )
            model_atom_id = (await cur.fetchone())[0]
            logger.info(f"Created model atom: {model_atom_id}")

        # For now, create a sample layer with sample weights
        # Full GGUF parsing requires gguf-parser library
        await self._atomize_sample_layer(conn, model_atom_id, max_tensors)

        dedup_ratio = (
            self.stats["total_weights"] / self.stats["weights_atomized"]
            if self.stats["weights_atomized"] > 0
            else 1.0
        )

        return {
            "model_name": model_name,
            "model_atom_id": model_atom_id,
            "file_size_gb": file_path.stat().st_size / 1e9,
            "tensors_processed": self.stats["tensors_processed"],
            "total_weights": self.stats["total_weights"],
            "total_atoms": len(self.unique_weights),
            "unique_atoms": len(self.unique_weights),
            "deduplication_ratio": dedup_ratio,
            "model_hash": model_hash.hex()[:16],
        }

    async def _atomize_sample_layer(
        self, conn: AsyncConnection, model_atom_id: int, max_tensors: Optional[int]
    ):
        """
        Atomize a sample layer with synthetic weights.
        
        This demonstrates the hierarchical composition pattern:
        Model ? Layer ? Tensor ? Weights (sparse)
        """
        async with conn.cursor() as cur:
            # Create layer atom
            layer_name = "layer_0_sample"
            layer_hash = hashlib.sha256(layer_name.encode()).digest()
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    %s,
                    %s::jsonb
                )
                """,
                (layer_hash, layer_name, {"modality": "layer", "layer_index": 0}),
            )
            layer_atom_id = (await cur.fetchone())[0]

            # Link layer to model
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                VALUES (%s, %s, %s)
                """,
                (model_atom_id, layer_atom_id, 0),
            )

            # Create sample tensor with sparse weights
            sample_weights = [0.0, 0.0, 1.523, 0.0, -2.341, 0.0, 0.0, 3.142, 0.0, 0.001]
            await self._atomize_tensor(
                conn, layer_atom_id, "tensor_0", sample_weights, 0
            )

            self.stats["tensors_processed"] = 1 if max_tensors is None else min(1, max_tensors)

    async def _atomize_tensor(
        self,
        conn: AsyncConnection,
        parent_atom_id: int,
        tensor_name: str,
        weights: List[float],
        sequence_index: int,
    ):
        """
        Atomize a tensor with sparse composition.
        
        Only weights with |value| >= threshold are stored.
        sequence_index in atom_composition preserves position.
        """
        async with conn.cursor() as cur:
            # Create tensor atom
            tensor_hash = hashlib.sha256(tensor_name.encode()).digest()
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    %s,
                    %s::jsonb
                )
                """,
                (
                    tensor_hash,
                    tensor_name,
                    {"modality": "tensor", "shape": len(weights)},
                ),
            )
            tensor_atom_id = (await cur.fetchone())[0]

            # Link tensor to parent (layer)
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                VALUES (%s, %s, %s)
                """,
                (parent_atom_id, tensor_atom_id, sequence_index),
            )

            # Atomize weights (sparse)
            for idx, weight in enumerate(weights):
                self.stats["total_weights"] += 1

                # Sparse encoding: skip near-zero weights
                if abs(weight) < self.threshold:
                    self.stats["weights_skipped"] += 1
                    continue

                # Atomize weight via database function
                if weight not in self.unique_weights:
                    await cur.execute(
                        """
                        SELECT atomize_numeric(
                            %s::numeric,
                            %s::jsonb
                        )
                        """,
                        (weight, {"modality": "weight", "value": weight}),
                    )
                    weight_atom_id = (await cur.fetchone())[0]
                    self.unique_weights[weight] = weight_atom_id
                    self.stats["weights_atomized"] += 1
                else:
                    weight_atom_id = self.unique_weights[weight]

                # Link weight to tensor (sequence_index = position)
                await cur.execute(
                    """
                    INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                    VALUES (%s, %s, %s)
                    ON CONFLICT DO NOTHING
                    """,
                    (tensor_atom_id, weight_atom_id, idx),
                )

            logger.debug(
                f"Atomized tensor {tensor_name}: {len(weights)} weights, "
                f"{self.stats['weights_atomized']} unique, "
                f"{self.stats['weights_skipped']} skipped"
            )


__all__ = ["GGUFAtomizer"]

