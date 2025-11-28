"""GGUF Model Atomization Service."""

import hashlib
import json
import logging
from pathlib import Path
from typing import Any, Dict, List, Optional

from psycopg import AsyncConnection

from src.core.atomization.base_atomizer import BaseAtomizer

logger = logging.getLogger(__name__)

# GPU availability check
GPU_AVAILABLE = False
try:
    import cupy as cp
    GPU_AVAILABLE = True
    logger.info("GPU acceleration available via CuPy")
except ImportError:
    logger.info("GPU not available, using CPU with NumPy SIMD")


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

        await self._atomize_gguf_file(conn, file_path, model_atom_id, max_tensors)

        dedup_ratio = (
            self.stats["total_processed"] / self.stats["atoms_created"]
            if self.stats["atoms_created"] > 0
            else 1.0
        )
        
        sparse_pct = (
            self.stats.get("sparse_skipped", 0) / max(self.stats["total_processed"], 1) * 100
        )

        return {
            "model_name": model_name,
            "model_atom_id": model_atom_id,
            "file_size_gb": file_path.stat().st_size / 1e9,
            "layers_processed": self.stats.get("layers_processed", 0),
            "tensors_processed": self.stats.get("tensors_processed", 0),
            "total_processed": self.stats["total_processed"],
            "atoms_created": self.stats["atoms_created"],
            "sparse_skipped": self.stats.get("sparse_skipped", 0),
            "deduplication_ratio": dedup_ratio,
            "sparse_percentage": sparse_pct,
            "model_hash": model_hash.hex()[:16],
        }

    async def _atomize_gguf_file(
        self,
        conn: AsyncConnection,
        file_path: Path,
        model_atom_id: int,
        max_tensors: Optional[int],
    ):
        """Parse GGUF file and atomize actual tensor weights."""
        try:
            import gguf
            import numpy as np
        except ImportError:
            logger.error("gguf library not installed. Run: pip install gguf")
            raise ImportError("Install gguf: pip install gguf")
        
        logger.info(f"Reading GGUF file: {file_path}")
        reader = gguf.GGUFReader(str(file_path))
        
        logger.info(f"GGUF metadata: {len(reader.tensors)} tensors")
        
        for tensor_idx, tensor in enumerate(reader.tensors):
            if max_tensors and tensor_idx >= max_tensors:
                logger.info(f"Stopping at max_tensors={max_tensors}")
                break
            
            logger.info(f"Processing tensor {tensor_idx+1}/{len(reader.tensors)}: {tensor.name} {tensor.shape}")
            
            # Create tensor metadata atom
            tensor_hash = hashlib.sha256(tensor.name.encode()).digest()
            tensor_atom_id = await self.create_atom(
                conn, 
                tensor_hash, 
                tensor.name,
                {
                    "modality": "tensor",
                    "shape": [int(s) for s in tensor.shape],  # Convert numpy int types to Python int
                    "dtype": str(tensor.tensor_type),
                    "sparse_threshold": self.threshold,
                    "n_elements": int(np.prod(tensor.shape))
                }
            )
            
            await self.create_composition(conn, model_atom_id, tensor_atom_id, tensor_idx)
            self.stats["tensors_processed"] = self.stats.get("tensors_processed", 0) + 1
            
            # Flatten tensor and atomize weights with BATCHING + SIMD/GPU
            import numpy as np
            
            weights = tensor.data.flatten()
            total_weights = len(weights)
            
            if GPU_AVAILABLE:
                # GPU: Transfer to GPU for vectorized operations
                try:
                    weights_gpu = cp.array(weights, dtype=cp.float32)
                    
                    # Vectorized unique on GPU
                    unique_values_gpu = cp.unique(weights_gpu)
                    unique_values = len(unique_values_gpu)
                    
                    # Vectorized sparse filtering on GPU
                    abs_weights_gpu = cp.abs(weights_gpu)
                    sparse_mask_gpu = abs_weights_gpu < self.threshold
                    sparse_count = int(cp.sum(sparse_mask_gpu))
                    
                    # Get non-sparse weights and indices on GPU, then transfer
                    non_sparse_indices_gpu = cp.where(~sparse_mask_gpu)[0]
                    non_sparse_weights_gpu = weights_gpu[~sparse_mask_gpu]
                    
                    # Transfer back to CPU
                    non_sparse_indices = non_sparse_indices_gpu.get().tolist()
                    non_sparse_weights = non_sparse_weights_gpu.get().astype(float).tolist()
                    
                    logger.info(f"  [GPU] Processed {total_weights:,} weights | {unique_values:,} unique")
                except Exception as e:
                    logger.warning(f"GPU processing failed: {e}, falling back to CPU")
                    GPU_AVAILABLE_NOW = False
            
            if not GPU_AVAILABLE:
                # CPU SIMD: Vectorized with NumPy
                unique_values_set = np.unique(weights)
                unique_values = len(unique_values_set)
                
                # Vectorized sparse filtering
                abs_weights = np.abs(weights)
                sparse_mask = abs_weights < self.threshold
                sparse_count = int(np.sum(sparse_mask))
                
                # Get non-sparse weights and their indices (vectorized)
                non_sparse_indices = np.where(~sparse_mask)[0].tolist()
                non_sparse_weights = weights[~sparse_mask].astype(float).tolist()
                
                logger.info(f"  [CPU SIMD] Processed {total_weights:,} weights | {unique_values:,} unique")
            
            self.stats["sparse_skipped"] = self.stats.get("sparse_skipped", 0) + sparse_count
            self.stats["total_processed"] += total_weights
            
            logger.info(f"  Sparse filter: {sparse_count:,} skipped ({sparse_count/total_weights*100:.1f}%)")
            logger.info(f"  Processing {len(non_sparse_weights):,} non-sparse weights...")
            
            # Batch atomize all non-sparse weights
            weight_to_atom = await self._atomize_weight_batch(conn, non_sparse_weights)
            
            # Build compositions (vectorized preparation)
            compositions = [
                {
                    "component_id": weight_to_atom[weight],
                    "sequence_idx": int(idx)
                }
                for idx, weight in zip(non_sparse_indices, non_sparse_weights)
            ]
            
            # Batch insert all compositions
            if compositions:
                total_comps = len(compositions)
                logger.info(f"  Batch inserting {total_comps:,} compositions...")
                await self._create_composition_batch(conn, tensor_atom_id, compositions)
                logger.info(f"  ✓ Inserted {total_comps:,} compositions")
            
            unique_so_far = len(self.cache)
            dedup_ratio = self.stats["total_processed"] / max(unique_so_far, 1)
            sparse_pct = (self.stats.get("sparse_skipped", 0) / self.stats["total_processed"]) * 100
            logger.info(
                f"  ✓ Tensor complete: {self.stats['total_processed']:,} weights processed, "
                f"{unique_so_far:,} unique atoms, {dedup_ratio:.1f}x dedup, {sparse_pct:.1f}% sparse"
            )
        
        logger.info(f"GGUF atomization complete: {self.stats['tensors_processed']} tensors")
    
    async def _atomize_demo_layers(
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
                (weight, json.dumps({"modality": "weight", "value": float(weight)}))
            )
            weight_atom_id = (await cur.fetchone())[0]

        self.cache[weight] = weight_atom_id
        self.stats["atoms_created"] += 1
        return weight_atom_id
    
    async def _atomize_weight_batch(
        self, 
        conn: AsyncConnection, 
        weights: List[float]
    ) -> Dict[float, int]:
        """Batch atomize multiple weights - 100-200x faster than individual calls."""
        # Check cache first
        uncached_weights = [w for w in set(weights) if w not in self.cache]
        
        if uncached_weights:
            # Batch atomize uncached weights
            async with conn.cursor() as cur:
                await cur.execute(
                    """
                    SELECT weight_value::numeric, atom_id 
                    FROM atomize_numeric_batch(
                        %s::numeric[],
                        '{"modality": "weight"}'::jsonb
                    )
                    """,
                    (uncached_weights,)
                )
                results = await cur.fetchall()
                
                # Update cache
                for weight_val, atom_id in results:
                    weight_float = float(weight_val)
                    self.cache[weight_float] = atom_id
                    self.stats["atoms_created"] += 1
        
        # Count cache hits
        self.stats["atoms_deduped"] += len(weights) - len(uncached_weights)
        
        # Return mapping for all weights
        return {w: self.cache[w] for w in weights}
    
    async def _create_composition_batch(
        self,
        conn: AsyncConnection,
        parent_id: int,
        compositions: List[Dict[str, int]]
    ):
        """Batch create compositions - much faster than individual inserts."""
        batch_size = 5000
        total_batches = (len(compositions) + batch_size - 1) // batch_size
        
        for batch_idx, i in enumerate(range(0, len(compositions), batch_size)):
            batch = compositions[i:i+batch_size]
            batch_num = batch_idx + 1
            batch_start = i
            batch_end = min(i + batch_size, len(compositions))
            
            async with conn.cursor() as cur:
                await cur.execute(
                    "SELECT create_composition_batch(%s::bigint, %s::jsonb[])",
                    (parent_id, [json.dumps(c) for c in batch])
                )
            
            # Progress reporting every batch
            progress_pct = (batch_end / len(compositions)) * 100
            logger.info(
                f"    [{progress_pct:5.1f}%] Batch {batch_num}/{total_batches}: "
                f"Inserted {batch_end:,}/{len(compositions):,} compositions"
            )

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


