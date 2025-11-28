"""GGUF Model Atomization Service."""

import hashlib
import logging
from pathlib import Path
from typing import Any, Dict, List, Optional

from psycopg import AsyncConnection
from psycopg.types.json import Json

from src.core.atomization.base_atomizer import BaseAtomizer

logger = logging.getLogger(__name__)


class GGUFAtomizer(BaseAtomizer):
    """GGUF model atomizer with hierarchical decomposition and sparse encoding."""

    async def atomize_model(
        self,
        file_path: Path,
        model_name: str,
        conn: AsyncConnection,
        max_tensors: Optional[int] = None,
    ) -> Dict[str, Any]:
        """Atomize GGUF model following hierarchical composition pattern."""
        logger.info(f"Atomizing GGUF model: {model_name} ({file_path.stat().st_size / 1e9:.2f} GB)")

        model_metadata = {
            "modality": "model",
            "format": "gguf",
            "file_path": str(file_path),
            "file_size_bytes": file_path.stat().st_size,
            "threshold": self.threshold,
        }
        
        model_hash = hashlib.sha256(model_name.encode()).digest()
        model_atom_id = await self.create_atom(conn, model_hash, model_name, model_metadata)
        
        logger.info(f"Model atom created: {model_atom_id}")

        await self._atomize_sample_layers(conn, model_atom_id, model_name, max_tensors)

        dedup_ratio = (
            self.stats["total_processed"] / self.stats["atoms_created"]
            if self.stats["atoms_created"] > 0
            else 1.0
        )

        return {
            "model_name": model_name,
            "model_atom_id": model_atom_id,
            "file_size_gb": file_path.stat().st_size / 1e9,
            "layers_processed": self.stats.get("layers_processed", 0),
            "tensors_processed": self.stats.get("tensors_processed", 0),
            "total_weights": self.stats["total_processed"],
            "total_atoms": len(self.cache),
            "unique_atoms": len(self.cache),
            "deduplication_ratio": dedup_ratio,
            "sparse_savings": f"{self.stats['sparse_skipped'] / max(self.stats['total_processed'], 1) * 100:.1f}%",
            "model_hash": model_hash.hex()[:16],
        }

    async def _atomize_sample_layers(
        self,
        conn: AsyncConnection,
        model_atom_id: int,
        model_name: str,
        max_tensors: Optional[int],
    ):
        """Demonstrate hierarchical atomization with sample layers."""
        sample_layers = [
            {
                "name": "layer_0.attention.wq",
                "shape": [128, 128],
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

            layer_hash = hashlib.sha256(layer_data["name"].encode()).digest()
            layer_atom_id = await self.create_atom(
                conn,
                layer_hash,
                layer_data["name"],
                {"modality": "layer", "layer_index": layer_idx}
            )

            await self.create_composition(conn, model_atom_id, layer_atom_id, layer_idx)

            await self._atomize_tensor(
                conn,
                layer_atom_id,
                layer_data["name"],
                layer_data["shape"],
                layer_data["weights"],
                0,
            )

            self.stats["layers_processed"] = self.stats.get("layers_processed", 0) + 1

    async def _atomize_tensor(
        self,
        conn: AsyncConnection,
        parent_atom_id: int,
        tensor_name: str,
        shape: List[int],
        weights: List[float],
        tensor_idx: int,
    ):
        """Atomize tensor with sparse composition."""
        tensor_hash = hashlib.sha256(tensor_name.encode()).digest()
        tensor_atom_id = await self.create_atom(
            conn,
            tensor_hash,
            tensor_name,
            {
                "modality": "tensor",
                "shape": shape,
                "sparse_threshold": self.threshold,
            }
        )

        await self.create_composition(conn, parent_atom_id, tensor_atom_id, tensor_idx)

        for idx, weight in enumerate(weights):
            self.stats["total_processed"] += 1

            if abs(weight) < self.threshold:
                self.stats["sparse_skipped"] += 1
                continue

            weight_atom_id = await self._atomize_weight(conn, weight)
            await self.create_composition(conn, tensor_atom_id, weight_atom_id, idx)

        self.stats["tensors_processed"] = self.stats.get("tensors_processed", 0) + 1

    async def _atomize_weight(self, conn: AsyncConnection, weight: float) -> int:
        """Atomize single weight value with caching."""
        if weight in self.cache:
            self.stats["atoms_deduped"] += 1
            return self.cache[weight]

        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atomize_numeric(%s::numeric, %s::jsonb)",
                (weight, Json({"modality": "weight", "value": float(weight)}))
            )
            weight_atom_id = (await cur.fetchone())[0]

        self.cache[weight] = weight_atom_id
        self.stats["atoms_created"] += 1
        return weight_atom_id

    def _generate_sample_weights(self, count: int) -> List[float]:
        """Generate sample weight distribution for demonstration."""
        import random
        random.seed(42)

        weights = []
        for i in range(count):
            if random.random() < 0.7:
                weights.append(0.0)
            elif random.random() < 0.3:
                weights.append(random.choice([0.5, -0.5, 1.0, -1.0, 0.25]))
            else:
                weights.append(random.uniform(-2.0, 2.0))

        return weights


__all__ = ["GGUFAtomizer"]

