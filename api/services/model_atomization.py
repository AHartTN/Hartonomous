"""
GGUF Model Atomization Service

Atomizes AI model weights into cognitive substrate following the same hierarchical
pattern as text atomization: model ? layers ? tensors ? weights

Pattern:
  Text:   document ? paragraphs ? sentences ? words ? characters (atoms)
  Image:  image ? patches ? pixels (atoms)
  Model:  model ? layers ? tensors ? weights (atoms)

Each weight value ?64 bytes becomes a content-addressed atom with deduplication.
Sparse encoding: gaps in sequence_index indicate zeros (below threshold).
Relations: semantic transformations between input/output concepts.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import json
import logging
import struct
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from psycopg import AsyncConnection
from psycopg.types.json import Json

logger = logging.getLogger(__name__)


class GGUFAtomizer:
    """
    GGUF model atomizer with hierarchical decomposition and sparse encoding.
    
    Follows the same pattern as atomize_text():
    - Each weight value becomes an atom (?64 bytes: float32 = 4 bytes ?)
    - Content-addressed via SHA-256 (automatic deduplication)
    - Hierarchical composition: model ? layers ? tensors ? weights
    - Sparse storage: only store weights where |value| > threshold
    - sequence_index gaps indicate implicit zeros
    """

    def __init__(self, threshold: float = 0.01):
        self.threshold = threshold
        self.weight_cache: Dict[float, int] = {}  # weight_value ? atom_id
        self.stats = {
            "layers_processed": 0,
            "tensors_processed": 0,
            "total_weights": 0,
            "weights_atomized": 0,
            "weights_deduped": 0,
            "weights_sparse_skipped": 0,
        }

    async def atomize_model(
        self,
        file_path: Path,
        model_name: str,
        conn: AsyncConnection,
        max_tensors: Optional[int] = None,
    ) -> Dict[str, Any]:
        """
        Atomize GGUF model following hierarchical composition pattern.
        
        Process:
        1. Create model root atom (metadata: name, format, size)
        2. For each layer: create layer atom
        3. For each tensor in layer: create tensor atom
        4. For each weight in tensor: atomize as numeric atom (?64 bytes)
        5. Link via atom_composition with sequence_index (sparse: gaps = zeros)
        
        This is IDENTICAL to text atomization:
          atomize_text("Hello") ? [H, e, l, l, o] atoms + composition
          atomize_model(gguf) ? [w1, w2, ..., wN] atoms + composition
        """
        logger.info(f"Atomizing GGUF model: {model_name} ({file_path.stat().st_size / 1e9:.2f} GB)")

        async with conn.cursor() as cur:
            # Step 1: Create model root atom (like "Hello World" sentence atom)
            model_metadata = {
                "modality": "model",
                "format": "gguf",
                "file_path": str(file_path),
                "file_size_bytes": file_path.stat().st_size,
                "threshold": self.threshold,
            }
            
            model_hash = hashlib.sha256(model_name.encode()).digest()
            
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    %s,
                    %s::jsonb
                )
                """,
                (model_hash, model_name, Json(model_metadata)),
            )
            model_atom_id = (await cur.fetchone())[0]
            logger.info(f"? Model atom created: {model_atom_id}")

            # Step 2-4: Atomize layers ? tensors ? weights
            # TODO: Full GGUF parsing requires gguf-parser library
            # For now, demonstrate with sample data showing the pattern
            await self._atomize_sample_layers(
                conn, model_atom_id, model_name, max_tensors
            )

        dedup_ratio = (
            self.stats["total_weights"] / self.stats["weights_atomized"]
            if self.stats["weights_atomized"] > 0
            else 1.0
        )

        return {
            "model_name": model_name,
            "model_atom_id": model_atom_id,
            "file_size_gb": file_path.stat().st_size / 1e9,
            "layers_processed": self.stats["layers_processed"],
            "tensors_processed": self.stats["tensors_processed"],
            "total_weights": self.stats["total_weights"],
            "total_atoms": len(self.weight_cache),
            "unique_atoms": len(self.weight_cache),
            "deduplication_ratio": dedup_ratio,
            "sparse_savings": f"{self.stats['weights_sparse_skipped'] / max(self.stats['total_weights'], 1) * 100:.1f}%",
            "model_hash": model_hash.hex()[:16],
        }

    async def _atomize_sample_layers(
        self,
        conn: AsyncConnection,
        model_atom_id: int,
        model_name: str,
        max_tensors: Optional[int],
    ):
        """
        Demonstrate hierarchical atomization with sample layers.
        
        Pattern mirrors text atomization:
          "Hello World" ? ["Hello", "World"] word atoms ? [H,e,l,l,o], [W,o,r,l,d] char atoms
          model ? [layer_0, layer_1] layer atoms ? [tensor_q, tensor_k] tensor atoms ? [w1, w2, ...] weight atoms
        """
        # Sample: 2 layers, each with 1 attention tensor
        sample_layers = [
            {
                "name": "layer_0.attention.wq",
                "shape": [128, 128],  # Small for demo
                "weights": self._generate_sample_weights(128 * 128),
            },
            {
                "name": "layer_1.attention.wq",
                "shape": [128, 128],
                "weights": self._generate_sample_weights(128 * 128),
            },
        ]

        for layer_idx, layer_data in enumerate(sample_layers):
            if max_tensors and layer_idx >= max_tensors:
                break

            # Create layer atom (like "Hello" word atom)
            layer_atom_id = await self._create_layer_atom(
                conn, layer_data["name"], layer_idx
            )

            # Link layer to model (composition)
            await self._link_composition(
                conn, model_atom_id, layer_atom_id, layer_idx
            )

            # Atomize tensor within layer (like characters within word)
            await self._atomize_tensor(
                conn,
                layer_atom_id,
                layer_data["name"],
                layer_data["shape"],
                layer_data["weights"],
                0,  # tensor index within layer
            )

            self.stats["layers_processed"] += 1

    async def _create_layer_atom(
        self, conn: AsyncConnection, layer_name: str, layer_idx: int
    ) -> int:
        """Create atom representing a layer (like creating 'Hello' word atom)."""
        async with conn.cursor() as cur:
            layer_hash = hashlib.sha256(layer_name.encode()).digest()
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea,
                    %s,
                    %s::jsonb
                )
                """,
                (
                    layer_hash,
                    layer_name,
                    Json({"modality": "layer", "layer_index": layer_idx}),
                ),
            )
            return (await cur.fetchone())[0]

    async def _atomize_tensor(
        self,
        conn: AsyncConnection,
        parent_atom_id: int,
        tensor_name: str,
        shape: List[int],
        weights: List[float],
        tensor_idx: int,
    ):
        """
        Atomize tensor with sparse composition (CRITICAL: follows atomize_text pattern).
        
        Just like "Hello" ? [H, e, l, l, o] with l_atom reused twice,
        tensor ? [w1, w2, w1, w3] with w1_atom reused via content-addressing.
        
        Sparse: Only store weights where |value| > threshold.
        sequence_index preserves position, gaps indicate zeros.
        """
        async with conn.cursor() as cur:
            # Create tensor atom (like creating 'H' character atom)
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
                    Json({
                        "modality": "tensor",
                        "shape": shape,
                        "sparse_threshold": self.threshold,
                    }),
                ),
            )
            tensor_atom_id = (await cur.fetchone())[0]

            # Link tensor to parent layer
            await self._link_composition(
                conn, parent_atom_id, tensor_atom_id, tensor_idx
            )

            # Atomize each weight (like atomizing each character in "Hello")
            for idx, weight in enumerate(weights):
                self.stats["total_weights"] += 1

                # Sparse encoding: skip near-zero weights (like skipping spaces)
                if abs(weight) < self.threshold:
                    self.stats["weights_sparse_skipped"] += 1
                    continue  # Gap in sequence_index = implicit zero

                # Content-addressed atomization (deduplication via SHA-256)
                weight_atom_id = await self._atomize_weight(conn, weight)

                # Link weight to tensor with sequence_index
                # Gaps in sequence_index indicate implicit zeros (sparse)
                await self._link_composition(
                    conn, tensor_atom_id, weight_atom_id, idx
                )

            self.stats["tensors_processed"] += 1
            logger.debug(
                f"? Tensor {tensor_name}: {len(weights)} weights, "
                f"{self.stats['weights_atomized']} unique atoms, "
                f"{self.stats['weights_sparse_skipped']} sparse zeros"
            )

    async def _atomize_weight(self, conn: AsyncConnection, weight: float) -> int:
        """
        Atomize single weight value (?64 bytes: float32 = 4 bytes ?).
        
        Uses content-addressing via SHA-256 for automatic deduplication.
        Same weight value across entire model ? same atom_id (reference_count++)
        
        This is IDENTICAL to how 'l' in "Hello" is reused: same character ? same atom.
        """
        # Check cache first (avoid redundant DB queries)
        if weight in self.weight_cache:
            self.stats["weights_deduped"] += 1
            return self.weight_cache[weight]

        # Atomize weight via atomize_numeric (uses atomize_value internally)
        async with conn.cursor() as cur:
            await cur.execute(
                """
                SELECT atomize_numeric(
                    %s::numeric,
                    %s::jsonb
                )
                """,
                (weight, Json({"modality": "weight", "value": float(weight)})),
            )
            weight_atom_id = (await cur.fetchone())[0]

        self.weight_cache[weight] = weight_atom_id
        self.stats["weights_atomized"] += 1
        return weight_atom_id

    async def _link_composition(
        self,
        conn: AsyncConnection,
        parent_id: int,
        component_id: int,
        sequence_idx: int,
    ):
        """
        Link component to parent via atom_composition.
        
        sequence_index preserves order and enables sparse encoding:
        - Consecutive indices: dense data
        - Gaps in indices: implicit zeros (sparse)
        
        Example:
          tensor[0]=0.5, tensor[1]=0.0, tensor[2]=0.0, tensor[3]=0.3
          ? Store only: (tensor, w0.5, 0), (tensor, w0.3, 3)
          ? Indices 1,2 missing = implicit zeros
        """
        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                VALUES (%s, %s, %s)
                ON CONFLICT DO NOTHING
                """,
                (parent_id, component_id, sequence_idx),
            )

    def _generate_sample_weights(self, count: int) -> List[float]:
        """
        Generate sample weight distribution for demonstration.
        
        Real implementation would parse GGUF file tensors.
        This shows the atomization pattern with:
        - Repeated values (test deduplication)
        - Near-zero values (test sparse encoding)
        - Varied values (test content addressing)
        """
        import random
        random.seed(42)  # Reproducible

        weights = []
        for i in range(count):
            if random.random() < 0.7:  # 70% sparse (common in neural networks)
                weights.append(0.0)
            elif random.random() < 0.3:  # 30% repeated common values
                weights.append(random.choice([0.5, -0.5, 1.0, -1.0, 0.25]))
            else:  # Unique values
                weights.append(random.uniform(-2.0, 2.0))

        return weights


__all__ = ["GGUFAtomizer"]

