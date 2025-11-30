"""
Weight Processing - GPU-accelerated deduplication and caching.

Single Responsibility: Deduplicate and process weight values efficiently.
"""

import logging
from typing import List, Dict
import json
from psycopg import AsyncConnection

logger = logging.getLogger(__name__)

try:
    import cupy as cp
    GPU_AVAILABLE = True
except ImportError:
    GPU_AVAILABLE = False
    logger.warning("CuPy not available - GPU acceleration disabled")


class WeightProcessor:
    """Handles weight deduplication and atomization with GPU acceleration."""
    
    def __init__(self, threshold: float = 1e-6):
        """
        Initialize weight processor.
        
        Args:
            threshold: Sparsity threshold for weight filtering
        """
        self.threshold = threshold
        self.cache: Dict[float, int] = {}
        self.stats = {
            "atoms_created": 0,
            "atoms_deduped": 0,
        }
    
    def deduplicate_gpu(self, weights: List[float]) -> List[float]:
        """
        Deduplicate weights using GPU acceleration with progress reporting.
        
        Args:
            weights: List of weight values
            
        Returns:
            List of unique weight values
        """
        if not GPU_AVAILABLE:
            return list(set(weights))
        
        try:
            import numpy as np
            from tqdm import tqdm
            
            CHUNK_SIZE = 10_000_000  # 10M weights per chunk
            total_weights = len(weights)
            
            if total_weights <= CHUNK_SIZE:
                # Single operation for small arrays
                weights_np = np.array(weights, dtype=np.float32)
                weights_gpu = cp.asarray(weights_np)
                unique_gpu = cp.unique(weights_gpu)
                return cp.asnumpy(unique_gpu).tolist()
            
            # Process in chunks with progress bar
            logger.info(f"Deduplicating {total_weights:,} weights in GPU chunks...")
            all_unique = set()
            
            for i in tqdm(range(0, total_weights, CHUNK_SIZE), 
                         desc="GPU deduplication", 
                         unit="chunk"):
                chunk = weights[i:i + CHUNK_SIZE]
                chunk_np = np.array(chunk, dtype=np.float32)
                chunk_gpu = cp.asarray(chunk_np)
                unique_gpu = cp.unique(chunk_gpu)
                unique_np = cp.asnumpy(unique_gpu)
                all_unique.update(unique_np.tolist())
            
            result = list(all_unique)
            logger.info(f"✓ GPU deduplication: {total_weights:,} → {len(result):,} unique "
                       f"({len(result)/total_weights*100:.1f}% unique)")
            return result
            
        except Exception as e:
            logger.warning(f"GPU deduplication failed: {e}, falling back to CPU")
            return list(set(weights))
    
    async def atomize_weight(self, conn: AsyncConnection, weight: float) -> int:
        """
        Atomize single weight value with caching.
        
        Args:
            conn: Database connection
            weight: Weight value
            
        Returns:
            Atom ID for the weight
        """
        if weight in self.cache:
            self.stats["atoms_deduped"] += 1
            return self.cache[weight]
        
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atomize_numeric(%s::numeric, %s::jsonb)",
                (weight, json.dumps({"modality": "weight", "value": float(weight)})),
            )
            weight_atom_id = (await cur.fetchone())[0]
        
        self.cache[weight] = weight_atom_id
        self.stats["atoms_created"] += 1
        return weight_atom_id
    
    async def atomize_weight_batch(
        self, 
        conn: AsyncConnection, 
        weights: List[float]
    ) -> tuple:
        """
        Atomize batch of weights using columnar NumPy arrays (Phase 2).
        
        Args:
            conn: Database connection
            weights: List of weight values
            
        Returns:
            Tuple of (sorted_weights, sorted_atom_ids) as NumPy arrays
        """
        import hashlib
        import numpy as np
        from api.services.db_bulk_operations import bulk_insert_atoms
        
        # Deduplicate - convert to NumPy array immediately
        unique_weights = np.unique(np.array(weights, dtype=np.float32))
        logger.info(f"Atomizing {len(unique_weights):,} unique weights (from {len(weights):,} total)...")
        
        # Phase 2: Build columnar arrays for atom data (NO dict cache, NO nested loops)
        atom_hashes = np.array([
            hashlib.sha256(str(w).encode()).digest() 
            for w in unique_weights
        ])  # bytea[]
        
        atom_texts = np.array([str(w) for w in unique_weights])  # text[]
        
        # Metadata as JSON strings (TEXT format COPY requirement)
        atom_metadata = np.array([
            json.dumps({"modality": "weight", "value": float(w)})
            for w in unique_weights
        ])
        
        # Build rows for COPY: (content_hash, canonical_text, metadata)
        rows = list(zip(atom_hashes, atom_texts, atom_metadata))
        
        # Single COPY operation for all atoms
        _, results = await bulk_insert_atoms(conn, rows, include_spatial=False, show_progress=True)
        
        # Query atom_ids as NumPy array (NO dict reconstruction)
        async with conn.cursor() as cur:
            # Results from COPY are (content_hash, atom_id)
            # Query in same order as hashes to preserve alignment
            await cur.execute(
                "SELECT atom_id FROM atom WHERE content_hash = ANY(%s::bytea[]) ORDER BY content_hash",
                (list(atom_hashes),)
            )
            atom_ids = np.array([row[0] for row in await cur.fetchall()], dtype=np.int64)
        
        self.stats["atoms_created"] += len(atom_ids)
        
        # Return sorted arrays for Phase 3 (vectorized mapping)
        sorted_indices = np.argsort(unique_weights)
        sorted_weights = unique_weights[sorted_indices]
        sorted_atom_ids = atom_ids[sorted_indices]
        
        return sorted_weights, sorted_atom_ids
    
    def get_stats(self) -> Dict[str, int]:
        """Get processing statistics."""
        return self.stats.copy()
    
    def clear_cache(self):
        """Clear weight cache."""
        self.cache.clear()
