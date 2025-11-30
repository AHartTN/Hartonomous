"""
Tensor Atomization - Process and atomize model tensors.

Single Responsibility: Handle tensor-specific atomization logic.
"""

import logging
import time
from typing import Dict, List, Tuple, Any
from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool

from .weight_processor import WeightProcessor
from .composition_builder import CompositionBuilder
from api.services.db_bulk_operations import bulk_insert_atoms

logger = logging.getLogger(__name__)


def parse_tensor_name(tensor_name: str) -> Dict[str, int]:
    """
    Parse tensor name to extract layer, head, dimension info.
    
    Args:
        tensor_name: Name of the tensor (e.g., "model.layers.0.self_attn.q_proj.weight")
        
    Returns:
        Dict with layer, head, and dimension indices
    """
    parts = tensor_name.split(".")
    indices = {}
    
    for i, part in enumerate(parts):
        if part.isdigit():
            if i > 0:
                prefix = parts[i - 1]
                indices[f"{prefix}_idx"] = int(part)
    
    return indices


class TensorAtomizer:
    """Handles tensor atomization with weight processing and composition."""
    
    def __init__(self, threshold: float = 1e-6, parallel_processing: bool = False):
        """
        Initialize tensor atomizer.
        
        Args:
            threshold: Sparsity threshold
            parallel_processing: Enable parallel composition creation
        """
        self.weight_processor = WeightProcessor(threshold)
        self.composition_builder = CompositionBuilder()
        self.parallel_processing = parallel_processing
        
        self.stats = {
            "total_processed": 0,
            "atoms_created": 0,
            "sparse_skipped": 0,
            "deduplication_ratio": 0.0,
        }
    
    async def atomize_tensor(
        self,
        conn: AsyncConnection,
        pool: AsyncConnectionPool,
        tensor_name: str,
        tensor_data: Any,
        model_name: str
    ) -> Tuple[int, Dict[str, Any]]:
        """
        Atomize a single tensor with all its weights.
        
        Args:
            conn: Database connection
            pool: Connection pool for parallel ops
            tensor_name: Name of the tensor
            tensor_data: Tensor data (numpy array)
            model_name: Model identifier
            
        Returns:
            Tuple of (tensor_atom_id, atomization_stats)
        """
        import numpy as np
        
        start_time = time.time()
        logger.info(f"\n{'='*80}")
        logger.info(f"Atomizing tensor: {tensor_name}")
        logger.info(f"  Shape: {tensor_data.shape}")
        logger.info(f"  Size: {tensor_data.size:,} elements")
        logger.info(f"  Dtype: {tensor_data.dtype}")
        
        # Flatten and convert to float32
        weights = tensor_data.flatten().astype(np.float32).tolist()
        original_count = len(weights)
        
        # Filter sparse weights
        non_sparse_weights = [
            w for w in weights 
            if abs(w) >= self.weight_processor.threshold
        ]
        sparse_skipped = original_count - len(non_sparse_weights)
        
        if sparse_skipped > 0:
            logger.info(
                f"  Filtered {sparse_skipped:,} sparse weights "
                f"({sparse_skipped/original_count*100:.1f}%)"
            )
        
        # GPU deduplication
        logger.info(f"  Deduplicating {len(non_sparse_weights):,} weights...")
        dedup_start = time.time()
        unique_weights = self.weight_processor.deduplicate_gpu(non_sparse_weights)
        dedup_time = time.time() - dedup_start
        
        dedup_ratio = len(non_sparse_weights) / len(unique_weights) if unique_weights else 1.0
        logger.info(
            f"  ✓ Deduplication: {len(non_sparse_weights):,} → {len(unique_weights):,} "
            f"({dedup_ratio:.1f}x reduction in {dedup_time:.2f}s)"
        )
        
        # Atomize unique weights - returns sorted arrays for vectorized lookup
        logger.info(f"  Atomizing {len(unique_weights):,} unique weights...")
        atomize_start = time.time()
        sorted_weights, sorted_atom_ids = await self.weight_processor.atomize_weight_batch(conn, unique_weights)
        atomize_time = time.time() - atomize_start
        logger.info(f"  ✓ Weights atomized in {atomize_time:.2f}s")
        
        # Create tensor atom
        indices = parse_tensor_name(tensor_name)
        metadata = {
            "model_name": model_name,
            "tensor_name": tensor_name,
            "shape": list(tensor_data.shape),
            "dtype": str(tensor_data.dtype),
            "modality": "tensor",
            **indices
        }
        
        tensor_hash = f"tensor:{model_name}:{tensor_name}".encode()
        
        # bulk_insert_atoms expects (content_hash, canonical_text, metadata_json_str) for COPY
        # TEXT format COPY requires JSON string, not dict (psycopg3 limitation)
        import json
        rows = [(tensor_hash, tensor_name, json.dumps(metadata))]
        _, results = await bulk_insert_atoms(conn, rows, include_spatial=False)
        tensor_atom_id = results[0][1]
        
        logger.info(f"  Created tensor atom: ID={tensor_atom_id}")
        
        # Phase 3: Vectorized weight→atom mapping with np.searchsorted() (NO dict lookup)
        logger.info(f"  Mapping {len(non_sparse_weights):,} weights to atoms (vectorized)...")
        map_start = time.time()
        
        non_sparse_weights_np = np.array(non_sparse_weights, dtype=np.float32)
        
        # Binary search: O(log n) vectorized across all weights
        indices = np.searchsorted(sorted_weights, non_sparse_weights_np)
        component_atom_ids = sorted_atom_ids[indices]
        
        map_time = time.time() - map_start
        logger.info(
            f"  ✓ Weight mapping complete in {map_time:.2f}s "
            f"({len(non_sparse_weights)/map_time:,.0f} lookups/s)"
        )
        
        # Phase 4: Build columnar compositions (NumPy arrays, NO dict lists)
        logger.info(f"  Building {len(non_sparse_weights):,} composition arrays...")
        comp_start = time.time()
        
        # Get non-sparse indices (positions in original flattened tensor)
        non_sparse_indices = np.where(
            np.abs(np.array(weights, dtype=np.float32)) >= self.weight_processor.threshold
        )[0].astype(np.int32)
        
        comp_build_time = time.time() - comp_start
        logger.info(
            f"  ✓ Composition arrays built in {comp_build_time:.2f}s "
            f"({len(non_sparse_indices)/comp_build_time:,.0f} arrays/s)"
        )
        
        # Insert compositions with NumPy arrays (single COPY operation)
        await self.composition_builder.create_batch_sequential(
            conn, tensor_atom_id, component_atom_ids, non_sparse_indices
        )
        
        total_time = time.time() - start_time
        
        tensor_stats = {
            "tensor_atom_id": tensor_atom_id,
            "total_weights": original_count,
            "unique_weights": len(unique_weights),
            "sparse_skipped": sparse_skipped,
            "dedup_ratio": dedup_ratio,
            "time_seconds": total_time,
        }
        
        logger.info(f"  ✓ Tensor atomization complete in {total_time:.2f}s")
        logger.info(f"{'='*80}\n")
        
        # Update global stats
        self.stats["total_processed"] += original_count
        self.stats["sparse_skipped"] += sparse_skipped
        
        return tensor_atom_id, tensor_stats
    
    def get_stats(self) -> Dict[str, Any]:
        """Get atomization statistics."""
        stats = self.stats.copy()
        stats.update(self.weight_processor.get_stats())
        
        if stats["atoms_created"] > 0:
            stats["deduplication_ratio"] = (
                stats["total_processed"] - stats["sparse_skipped"]
            ) / stats["atoms_created"]
        else:
            stats["deduplication_ratio"] = 1.0
        
        if stats["total_processed"] > 0:
            stats["sparse_percentage"] = (
                stats["sparse_skipped"] / stats["total_processed"] * 100
            )
        else:
            stats["sparse_percentage"] = 0.0
        
        return stats
