"""
Tensor Atomization - Process and atomize model tensors.

Single Responsibility: Handle tensor-specific atomization logic.
"""

import logging
import time
import json
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
        
        # Create tensor atom with complete metadata (REQUIRED for reconstruction)
        indices = parse_tensor_name(tensor_name)
        shape_tuple = tensor_data.shape
        
        metadata = {
            "model_name": model_name,
            "tensor_name": tensor_name,
            "shape": list(shape_tuple),  # CRITICAL: Needed to reconstruct tensor dimensions
            "dtype": str(tensor_data.dtype),
            "modality": "model-tensor",
            "parameter_type": "weight" if "weight" in tensor_name else "bias" if "bias" in tensor_name else "unknown",
            "total_elements": int(np.prod(shape_tuple)),
            **indices
        }
        
        tensor_hash = f"tensor:{model_name}:{tensor_name}".encode()
        
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
        weight_indices = np.searchsorted(sorted_weights, non_sparse_weights_np)
        component_atom_ids = sorted_atom_ids[weight_indices]
        
        map_time = time.time() - map_start
        logger.info(
            f"  ✓ Weight mapping complete in {map_time:.2f}s "
            f"({len(non_sparse_weights)/map_time:,.0f} lookups/s)"
        )
        
        # Phase 4: Hilbert encoding with integer coordinates
        logger.info(f"  Encoding positions with Hilbert curves...")
        hilbert_start = time.time()
        
        # Get non-sparse indices and convert to multi-dimensional positions
        weights_array = np.array(weights, dtype=np.float32)
        non_sparse_mask = np.abs(weights_array) >= self.weight_processor.threshold
        flat_indices = np.where(non_sparse_mask)[0]
        
        # Convert flat indices to (i, j, layer) integer positions
        if len(tensor_data.shape) == 1:
            # 1D tensor (bias)
            positions = np.column_stack([
                flat_indices,
                np.zeros_like(flat_indices),
                np.zeros_like(flat_indices)
            ]).astype(np.int64)
            bits_needed = max(1, int(np.ceil(np.log2(tensor_data.shape[0]))))
        elif len(tensor_data.shape) == 2:
            # 2D tensor (most weights)
            rows, cols = tensor_data.shape
            i_pos = flat_indices // cols
            j_pos = flat_indices % cols
            positions = np.column_stack([
                i_pos, j_pos, np.zeros_like(i_pos)
            ]).astype(np.int64)
            bits_needed = max(1, int(np.ceil(np.log2(max(rows, cols)))))
        else:
            # 3D+ tensor
            idx_tuple = np.unravel_index(flat_indices, tensor_data.shape)
            if len(idx_tuple) >= 3:
                positions = np.column_stack(idx_tuple[:3]).astype(np.int64)
            else:
                pad_cols = [np.zeros(len(flat_indices), dtype=np.int64)] * (3 - len(idx_tuple))
                positions = np.column_stack(list(idx_tuple) + pad_cols)
            bits_needed = max(1, int(np.ceil(np.log2(max(tensor_data.shape)))))
        
        # Encode integer positions to Hilbert indices (lossless)
        from .hilbert_encoder import HilbertEncoder
        encoder = HilbertEncoder(bits=bits_needed)
        hilbert_indices = encoder.encode_batch(positions)
        
        hilbert_time = time.time() - hilbert_start
        logger.info(f"  ✓ Hilbert encoding complete in {hilbert_time:.2f}s (bits={bits_needed})")
        
        # Phase 5: Group into runs (RLE compression via LINESTRING geometries)
        logger.info(f"  Detecting runs in {len(hilbert_indices):,} positions...")
        stream_start = time.time()
        
        # Sort by (atom_id, hilbert_index) to group consecutive runs
        sort_order = np.lexsort((hilbert_indices, component_atom_ids))
        sorted_atoms = component_atom_ids[sort_order]
        sorted_hilbert = hilbert_indices[sort_order]
        
        # Detect runs: consecutive hilbert indices with same atom_id
        composition_rows = []
        i = 0
        while i < len(sorted_atoms):
            current_atom = sorted_atoms[i]
            run_start = sorted_hilbert[i]
            run_end = run_start
            j = i + 1
            
            # Extend run while atom_id matches and hilbert indices are consecutive
            while j < len(sorted_atoms) and sorted_atoms[j] == current_atom and sorted_hilbert[j] == run_end + 1:
                run_end = sorted_hilbert[j]
                j += 1
            
            run_length = run_end - run_start + 1
            
            if run_length == 1:
                # Single position - use POINTZM (x, y, z, m) where m=hilbert_index
                geometry_wkt = f"POINTZM(0 0 0 {run_start})"
                metadata_json = None
            else:
                # Run of positions - use LINESTRINGZM
                geometry_wkt = f"LINESTRINGZM(0 0 0 {run_start}, 0 0 0 {run_end})"
                metadata_json = json.dumps({
                    "run_length": run_length,
                    "encoding": "rle"
                })
            
            composition_rows.append((
                tensor_atom_id,
                int(current_atom),
                int(run_start),  # sequence_index = start of run
                geometry_wkt,
                metadata_json
            ))
            
            i = j
        
        stream_time = time.time() - stream_start
        logger.info(f"  ✓ Compressed to {len(composition_rows):,} compositions (from {len(hilbert_indices):,} positions) in {stream_time:.2f}s")
        logger.info(f"  ✓ Compression ratio: {len(hilbert_indices)/len(composition_rows):.1f}x")
        
        # Insert compositions with geometry
        await self.composition_builder.create_batch_with_geometry(
            conn, composition_rows
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
