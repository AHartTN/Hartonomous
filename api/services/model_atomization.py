"""GGUF Model Atomization Service."""

import asyncio
import hashlib
import json
import logging
import numpy as np
import sys
from decimal import Decimal
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from psycopg import AsyncConnection
from psycopg_pool import AsyncConnectionPool

from api.config import settings
from api.services.spatial_encoding import (calculate_architecture_spatial_key,
                                           calculate_merge_spatial_key,
                                           calculate_vocabulary_spatial_key,
                                           calculate_weight_spatial_key,
                                           spatial_key_to_wkt)
from api.services.embedding_service import generate_semantic_coordinates
from src.core.atomization.base_atomizer import BaseAtomizer
from src.core.compression.encoding import MultiLayerEncoder

logger = logging.getLogger(__name__)

# GPU availability check
GPU_AVAILABLE = False
try:
    import cupy as cp

    GPU_AVAILABLE = True
    logger.info("✓ GPU acceleration available via CuPy")
except ImportError:
    logger.info("✓ GPU not available, using CPU with NumPy SIMD")


class GGUFAtomizer(BaseAtomizer):
    """GGUF model atomizer with hierarchical decomposition and sparse encoding."""

    def __init__(self, threshold: float = 1e-6, parallel_processing: bool = False):
        """Initialize atomizer with sparsity threshold."""
        super().__init__()
        self.threshold = threshold
        self.parallel_processing = parallel_processing  # Toggle for parallel tensor processing
        self.cache: Dict[Any, Any] = {}  # Weight value -> atom_id cache
        self.cache_lock = None  # Initialized in atomize_gguf when event loop is available
        self.deferred_compositions: List[Dict] = []  # Store compositions to batch create at end
        self.composition_lock = None  # Lock for composition list access
        self.stats: Dict[str, int] = {
            "total_processed": 0,
            "atoms_created": 0,
            "atoms_deduped": 0,
        }
        self.encoder = MultiLayerEncoder(sparse_threshold=threshold)

    async def atomize_model(
        self,
        file_path: Path,
        model_name: str,
        conn: AsyncConnection,
        max_tensors: Optional[int] = None,
        pool: Optional[AsyncConnectionPool] = None,
    ) -> Dict[str, Any]:
        """Atomize GGUF model following hierarchical composition pattern.
        
        Args:
            file_path: Path to GGUF model file
            model_name: Name for the model
            conn: Database connection for metadata operations
            max_tensors: Optional limit on number of tensors to process
            pool: Optional connection pool for parallel tensor processing
        """
        
        # Initialize cache lock now that we're in async context
        if self.cache_lock is None:
            import asyncio
            self.cache_lock = asyncio.Lock()
            self.composition_lock = asyncio.Lock()
        
        # Determine device based on config and availability
        use_gpu = settings.use_gpu and GPU_AVAILABLE
        device = "GPU" if use_gpu else "CPU"
        
        logger.info(
            f"Atomizing GGUF model: {model_name} ({file_path.stat().st_size / 1e9:.2f} GB) [Device: {device}]"
        )
        
        if use_gpu:
            logger.info("✓ GPU acceleration enabled")
        elif settings.use_gpu and not GPU_AVAILABLE:
            logger.warning("⚠ GPU requested but not available, falling back to CPU")
        else:
            logger.info("✓ Using CPU (GPU disabled in config)")

        # Calculate file hash for content-based deduplication
        file_hash = hashlib.sha256()
        with open(file_path, "rb") as f:
            while chunk := f.read(8192 * 1024):  # 8MB chunks
                file_hash.update(chunk)
        model_content_hash = file_hash.digest()

        # Check if model atom already exists
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atom_id FROM atom WHERE content_hash = %s",
                (model_content_hash,),
            )
            existing = await cur.fetchone()
            
            if existing:
                model_atom_id = existing[0]
                logger.info(f"Model atom already exists: {model_atom_id} (skipping re-atomization)")
                
                # Return early with existing model stats
                return {
                    "model_name": model_name,
                    "model_atom_id": model_atom_id,
                    "file_size_gb": file_path.stat().st_size / 1e9,
                    "layers_processed": 0,
                    "tensors_processed": 0,
                    "total_processed": 0,
                    "atoms_created": 0,
                    "sparse_skipped": 0,
                    "deduplication_ratio": 0.0,
                    "sparse_percentage": 0.0,
                    "model_hash": model_content_hash.hex()[:16],
                    "status": "already_atomized",
                }

        model_metadata = {
            "modality": "model",
            "format": "gguf",
            "model_name": model_name,
            "file_path": str(file_path),
            "file_size_bytes": file_path.stat().st_size,
            "content_hash_hex": model_content_hash.hex(),
            "threshold": self.threshold,
        }

        # Check atom count before creation to detect if it's deduplicated
        async with conn.cursor() as cur:
            await cur.execute("SELECT COUNT(*) FROM atom WHERE content_hash = %s", (model_content_hash,))
            existed_before = (await cur.fetchone())[0] > 0

        model_atom_id = await self.create_atom(
            conn, model_content_hash, model_name, model_metadata
        )

        if existed_before:
            logger.info(f"Model atom found (deduplicated): {model_atom_id}")
        else:
            logger.info(f"Model atom created (new): {model_atom_id}")

        await self._atomize_gguf_file(conn, file_path, model_atom_id, max_tensors, pool)

        dedup_ratio = (
            self.stats["total_processed"] / self.stats["atoms_created"]
            if self.stats["atoms_created"] > 0
            else 1.0
        )

        sparse_pct = (
            self.stats.get("sparse_skipped", 0)
            / max(self.stats["total_processed"], 1)
            * 100
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
            "model_hash": model_content_hash.hex()[:16],
        }

    async def _atomize_gguf_file(
        self,
        conn: AsyncConnection,
        file_path: Path,
        model_atom_id: int,
        max_tensors: Optional[int],
        pool: Optional[AsyncConnectionPool] = None,
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

        # Phase 1: Atomize vocabulary (tokenizer landmarks)
        await self._atomize_vocabulary(conn, reader, model_atom_id)

        # Phase 2: Atomize architecture hyperparameters
        await self._atomize_architecture(conn, reader, model_atom_id)

        # Phase 3: Atomize tensor weights (with concurrent processing)
        tensors_to_process = reader.tensors
        if max_tensors:
            tensors_to_process = reader.tensors[:max_tensors]
            logger.info(f"Processing first {max_tensors} tensors [Mode: {'PARALLEL' if self.parallel_processing else 'SYNCHRONOUS'}]")
        
        # PRE-LOAD tensor data in main thread to avoid I/O contention
        # (parallel tasks reading from same GGUF file causes blocking)
        logger.info(f"Pre-loading {len(tensors_to_process)} tensor data arrays...")
        import time
        preload_start = time.time()
        tensor_data_list = []
        for idx, tensor in enumerate(tensors_to_process):
            logger.info(f"  Loading tensor {idx+1}/{len(tensors_to_process)}: {tensor.name} {tensor.shape}")
            weights = tensor.data.flatten()  # Load in main thread (sequential I/O)
            tensor_data_list.append({
                'tensor': tensor,
                'weights': weights,
                'total_weights': len(weights)
            })
        preload_time = time.time() - preload_start
        logger.info(f"✓ Pre-loaded all tensor data in {preload_time:.2f}s")
        
        import numpy as np
        from concurrent.futures import ThreadPoolExecutor
        import asyncio
        
        # Process tensors with controlled concurrency (I/O bound: SQL calls)
        max_workers = 4  # Conservative for database connections
        semaphore = asyncio.Semaphore(max_workers)
        
        # Use pool if provided for parallel processing
        use_pool = pool is not None
        
        async def process_one_tensor(tensor_idx: int, tensor_data: dict):
            """Process single tensor with semaphore control."""
            logger.debug(f"Task {tensor_idx} created, waiting for semaphore...")
            async with semaphore:
                logger.debug(f"Task {tensor_idx} acquired semaphore")
                if use_pool:
                    logger.debug(f"Task {tensor_idx} requesting pool connection...")
                    # Get dedicated connection from pool for this tensor
                    async with pool.connection() as worker_conn:
                        logger.debug(f"Task {tensor_idx} got pool connection, processing...")
                        await _process_tensor(worker_conn, tensor_idx, tensor_data)
                        logger.debug(f"Task {tensor_idx} processing complete")
                else:
                    # Sequential processing with single connection
                    logger.debug(f"Task {tensor_idx} using single connection...")
                    await _process_tensor(conn, tensor_idx, tensor_data)
                    logger.debug(f"Task {tensor_idx} complete")
                logger.debug(f"Task {tensor_idx} releasing semaphore")
        
        async def _process_tensor(worker_conn, tensor_idx: int, tensor_data: dict):
            """Inner function to process a single tensor."""
            tensor = tensor_data['tensor']
            weights = tensor_data['weights']
            total_weights = tensor_data['total_weights']
            
            logger.info(
                f"Processing tensor {tensor_idx+1}/{len(reader.tensors)}: {tensor.name} {tensor.shape}"
            )

            # Create tensor metadata atom
            logger.debug(f"  Task {tensor_idx}: Creating tensor atom...")
            tensor_hash = hashlib.sha256(tensor.name.encode()).digest()
            tensor_atom_id = await self.create_atom(
                worker_conn,
                tensor_hash,
                tensor.name,
                {
                    "modality": "tensor",
                    "shape": [
                        int(s) for s in tensor.shape
                    ],  # Convert numpy int types to Python int
                    "dtype": str(tensor.tensor_type),
                    "sparse_threshold": self.threshold,
                    "n_elements": int(np.prod(tensor.shape)),
                },
            )
            logger.debug(f"  Task {tensor_idx}: Tensor atom created (ID: {tensor_atom_id})")

            # DEFER composition creation to avoid lock contention - will batch create at end
            logger.debug(f"  Task {tensor_idx}: Deferring tensor composition creation...")
            async with self.composition_lock:
                self.deferred_compositions.append({
                    'parent_id': model_atom_id,
                    'component_id': tensor_atom_id,
                    'sequence_idx': tensor_idx
                })
            
            self.stats["tensors_processed"] = self.stats.get("tensors_processed", 0) + 1

            # Use pre-loaded weights (already flattened)
            logger.info(f"  Task {tensor_idx}: Processing {total_weights:,} pre-loaded weights...")
            
            # Initialize variables that will be set by either GPU or CPU path
            sparse_count = 0
            non_sparse_indices = []
            non_sparse_weights = []
            rle_applied = False

            if GPU_AVAILABLE:
                # GPU: Transfer to GPU for vectorized operations
                try:
                    # Apply RLE + sparse encoding
                    logger.info(f"  Converting {total_weights:,} weights to numpy array...")
                    import time
                    convert_start = time.time()
                    weights_np = np.array(weights, dtype=np.float32)
                    convert_time = time.time() - convert_start
                    logger.info(f"  → Converted in {convert_time:.2f}s ({total_weights/convert_time:,.0f} weights/s)")
                    
                    logger.info(f"  Encoding {total_weights:,} weights with RLE + sparse...")
                    encode_start = time.time()
                    encoded_bytes, encoding_metadata = self.encoder.encode(weights_np)
                    encode_time = time.time() - encode_start
                    logger.info(f"  → Encoded in {encode_time:.2f}s ({total_weights/encode_time:,.0f} weights/s)")
                    
                    # Decode to get compressed representation
                    compressed_weights = np.frombuffer(encoded_bytes, dtype=np.float32)
                    
                    # Transfer compressed weights to GPU
                    weights_gpu = cp.array(compressed_weights, dtype=cp.float32)

                    # Hierarchical batched deduplication with progress reporting
                    logger.info(f"  Deduplicating {len(compressed_weights):,} weights on GPU...")
                    import sys
                    sys.stdout.flush()  # Force output before GPU work
                    dedup_start = time.time()
                    
                    # Batch size for hierarchical deduplication (1M weights per batch)
                    DEDUP_BATCH_SIZE = 1_000_000
                    
                    if len(weights_gpu) <= DEDUP_BATCH_SIZE:
                        # Small enough - single pass
                        print(f"  → Running GPU unique operation...", flush=True)
                        unique_values_gpu = cp.unique(weights_gpu)
                        cp.cuda.Stream.null.synchronize()  # Ensure GPU finishes
                        print(f"  → GPU unique complete", flush=True)
                    else:
                        # Large array - hierarchical batching
                        num_batches = (len(weights_gpu) + DEDUP_BATCH_SIZE - 1) // DEDUP_BATCH_SIZE
                        logger.info(f"  → Processing {num_batches} batches of {DEDUP_BATCH_SIZE:,} weights each...")
                        sys.stdout.flush()
                        
                        # Phase 1: Get unique from each batch
                        batch_uniques = []
                        for i in range(num_batches):
                            print(f"    Batch {i+1}/{num_batches}: Starting GPU unique...", end='', flush=True)
                            start_idx = i * DEDUP_BATCH_SIZE
                            end_idx = min((i + 1) * DEDUP_BATCH_SIZE, len(weights_gpu))
                            batch = weights_gpu[start_idx:end_idx]
                            batch_unique = cp.unique(batch)
                            cp.cuda.Stream.null.synchronize()  # Wait for this batch
                            batch_uniques.append(batch_unique)
                            print(f" {len(batch):,} → {len(batch_unique):,} unique", flush=True)
                            
                            # Yield to event loop every few batches
                            if i % 2 == 0:
                                await asyncio.sleep(0)  # Let other tasks run
                        
                        # Phase 2: Concatenate and deduplicate batch uniques
                        print(f"  → Merging {len(batch_uniques)} batch results...", flush=True)
                        all_batch_uniques = cp.concatenate(batch_uniques)
                        print(f"  → Final deduplication of {len(all_batch_uniques):,} candidates...", flush=True)
                        unique_values_gpu = cp.unique(all_batch_uniques)
                        cp.cuda.Stream.null.synchronize()  # Final sync
                        print(f"  → Final deduplication complete", flush=True)
                    
                    unique_values = len(unique_values_gpu)
                    dedup_time = time.time() - dedup_start
                    logger.info(f"  → Deduplicated in {dedup_time:.2f}s: {len(compressed_weights):,} → {unique_values:,} unique ({len(compressed_weights)/unique_values:.1f}x compression)")

                    # Vectorized sparse filtering on GPU (already done by encoder, but check)
                    print(f"  → Applying sparse filter (threshold={self.threshold})...", flush=True)
                    abs_weights_gpu = cp.abs(weights_gpu)
                    sparse_mask_gpu = abs_weights_gpu < self.threshold
                    sparse_count = int(cp.sum(sparse_mask_gpu))
                    print(f"  → Found {sparse_count:,} sparse values to skip", flush=True)

                    # Get non-sparse weights and indices on GPU, then transfer
                    print(f"  → Extracting {len(compressed_weights) - sparse_count:,} non-sparse weights on GPU...", flush=True)
                    non_sparse_indices_gpu = cp.where(~sparse_mask_gpu)[0]
                    non_sparse_weights_gpu = weights_gpu[~sparse_mask_gpu]

                    # Transfer back to CPU, preserve precision as Decimal
                    print(f"  → Transferring {len(non_sparse_weights_gpu):,} weights from GPU to CPU...", flush=True)
                    non_sparse_indices = non_sparse_indices_gpu.get().tolist()
                    print(f"  → Converting {len(non_sparse_weights_gpu):,} weights to Decimal precision...", flush=True)
                    non_sparse_weights = [
                        Decimal(str(float(w)))
                        for w in non_sparse_weights_gpu.get().tolist()
                    ]
                    print(f"  → Transfer and conversion complete", flush=True)

                    rle_applied = encoding_metadata.rle_applied
                    rle_note = " (RLE applied)" if rle_applied else ""
                    logger.info(
                        f"  [GPU] Processed {total_weights:,} weights | {len(compressed_weights):,} after compression | {unique_values:,} unique{rle_note}"
                    )
                    logger.debug(f"  Task {tensor_idx}: GPU processing complete")
                except Exception as e:
                    logger.warning(f"  Task {tensor_idx}: GPU processing failed: {e}, falling back to CPU")
                    GPU_AVAILABLE_NOW = False

            if not GPU_AVAILABLE:
                # CPU SIMD: Vectorized with NumPy + RLE encoding
                # Apply RLE + sparse encoding
                weights_np = np.array(weights, dtype=np.float32)
                encoded_bytes, encoding_metadata = self.encoder.encode(weights_np)
                
                # Decode to get compressed representation
                compressed_weights = np.frombuffer(encoded_bytes, dtype=np.float32)
                
                # Vectorized unique on compressed data
                unique_values_set = np.unique(compressed_weights)
                unique_values = len(unique_values_set)

                # Vectorized sparse filtering (already done by encoder, but check)
                abs_weights = np.abs(compressed_weights)
                sparse_mask = abs_weights < self.threshold
                sparse_count = int(np.sum(sparse_mask))

                # Get non-sparse weights and their indices (vectorized)
                non_sparse_indices = np.where(~sparse_mask)[0].tolist()
                non_sparse_weights = [
                    Decimal(str(float(w))) for w in compressed_weights[~sparse_mask].tolist()
                ]

                rle_applied = encoding_metadata.rle_applied
                rle_note = " (RLE applied)" if rle_applied else ""
                logger.info(
                    f"  [CPU SIMD] Processed {total_weights:,} weights | {len(compressed_weights):,} after compression | {unique_values:,} unique{rle_note}"
                )

            self.stats["sparse_skipped"] = (
                self.stats.get("sparse_skipped", 0) + sparse_count
            )
            self.stats["total_processed"] += total_weights

            logger.info(
                f"  Sparse filter: {sparse_count:,} skipped ({sparse_count/total_weights*100:.1f}%)"
            )
            logger.info(
                f"  Processing {len(non_sparse_weights):,} non-sparse weights..."
            )

            # Batch atomize all non-sparse weights
            print(f"  → Starting weight atomization batch for {len(non_sparse_weights):,} weights...", flush=True)
            logger.debug(f"  Task {tensor_idx}: Starting weight atomization batch...")
            weight_to_atom = await self._atomize_weight_batch(worker_conn, non_sparse_weights)
            print(f"  → Weight atomization complete, got {len(weight_to_atom):,} atom IDs", flush=True)
            logger.debug(f"  Task {tensor_idx}: Weight atomization complete, got {len(weight_to_atom):,} atom IDs")

            # Build compositions (vectorized preparation)
            print(f"  → Building {len(non_sparse_weights):,} composition records...", flush=True)
            compositions = [
                {"component_id": int(weight_to_atom[weight]), "sequence_idx": int(idx)}
                for idx, weight in zip(non_sparse_indices, non_sparse_weights)
            ]
            print(f"  → Composition records built", flush=True)

            # Batch insert all compositions
            if compositions:
                total_comps = len(compositions)
                logger.info(f"  Batch inserting {total_comps:,} compositions...")
                logger.debug(f"  Task {tensor_idx}: Starting composition batch insert...")
                await self._create_composition_batch(worker_conn, tensor_atom_id, compositions)
                logger.debug(f"  Task {tensor_idx}: Composition batch complete")
                logger.info(f"  ✓ Inserted {total_comps:,} compositions")

            unique_so_far = len(self.cache)
            dedup_ratio = self.stats["total_processed"] / max(unique_so_far, 1)
            sparse_pct = (
                self.stats.get("sparse_skipped", 0) / self.stats["total_processed"]
            ) * 100
            logger.info(
                f"  ✓ Tensor complete: {self.stats['total_processed']:,} weights processed, "
                f"{unique_so_far:,} unique atoms, {dedup_ratio:.1f}x dedup, {sparse_pct:.1f}% sparse"
            )
            logger.debug(f"  Task {tensor_idx}: _process_tensor() returning...")
        
        # Process tensors (parallel or synchronous based on config)
        if self.parallel_processing and pool:
            logger.info("Starting PARALLEL tensor processing...")
            tasks = [process_one_tensor(idx, tensor_data) for idx, tensor_data in enumerate(tensor_data_list)]
            await asyncio.gather(*tasks)
        else:
            logger.info("Starting SYNCHRONOUS tensor processing...")
            # Process tensors one at a time (no parallelism)
            for idx, tensor_data in enumerate(tensor_data_list):
                # Use main connection for synchronous mode
                await _process_tensor(conn, idx, tensor_data)

        # BATCH CREATE ALL DEFERRED COMPOSITIONS AT ONCE (no lock contention!)
        if self.deferred_compositions:
            logger.info(f"Creating {len(self.deferred_compositions)} deferred tensor compositions in batch...")
            await self._create_compositions_batch(conn, self.deferred_compositions)
            logger.info(f"✓ All tensor compositions created")
            self.deferred_compositions.clear()  # Reset for next run

        logger.info(
            f"GGUF atomization complete: {self.stats['tensors_processed']} tensors"
        )

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
                {"modality": "layer", "layer_index": layer_idx},
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
            },
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
                (weight, json.dumps({"modality": "weight", "value": float(weight)})),
            )
            weight_atom_id = (await cur.fetchone())[0]

        self.cache[weight] = weight_atom_id
        self.stats["atoms_created"] += 1
        return weight_atom_id

    async def _create_compositions_batch(
        self, conn: AsyncConnection, compositions: List[Dict]
    ):
        """Batch create all compositions in a single efficient operation."""
        if not compositions:
            return
        
        async with conn.cursor() as cur:
            # Use UNNEST for efficient multi-row INSERT
            parent_ids = [c['parent_id'] for c in compositions]
            component_ids = [c['component_id'] for c in compositions]
            sequence_idxs = [c['sequence_idx'] for c in compositions]
            
            await cur.execute("""
                INSERT INTO composition (parent_atom_id, component_atom_id, sequence_idx)
                SELECT * FROM UNNEST(%s::bigint[], %s::bigint[], %s::integer[])
                ON CONFLICT (parent_atom_id, component_atom_id) DO NOTHING
            """, (parent_ids, component_ids, sequence_idxs))

    def _deduplicate_weights_gpu(self, weights: List) -> List:
        """Deduplicate weights using GPU acceleration with batched progress reporting."""
        try:
            import numpy as np
            from tqdm import tqdm
            
            # For very large arrays, process in chunks to show progress
            CHUNK_SIZE = 10_000_000  # 10M weights per chunk
            total_weights = len(weights)
            
            if total_weights <= CHUNK_SIZE:
                # Small enough - single operation
                weights_np = np.array(weights, dtype=np.float32)
                weights_gpu = cp.asarray(weights_np)
                unique_gpu = cp.unique(weights_gpu)
                unique_np = cp.asnumpy(unique_gpu)
                return unique_np.tolist()
            
            # Large array - process in chunks with progress bar
            logger.info(f"    Processing {total_weights:,} weights in chunks of {CHUNK_SIZE:,}...")
            unique_set = set()
            
            num_chunks = (total_weights + CHUNK_SIZE - 1) // CHUNK_SIZE
            for i in tqdm(range(num_chunks), desc="    GPU dedup chunks", unit="chunk"):
                start_idx = i * CHUNK_SIZE
                end_idx = min(start_idx + CHUNK_SIZE, total_weights)
                chunk = weights[start_idx:end_idx]
                
                # GPU-accelerated unique for this chunk
                chunk_np = np.array(chunk, dtype=np.float32)
                chunk_gpu = cp.asarray(chunk_np)
                unique_gpu = cp.unique(chunk_gpu)
                unique_np = cp.asnumpy(unique_gpu)
                
                # Accumulate unique values
                unique_set.update(unique_np.tolist())
            
            return list(unique_set)
        except Exception as e:
            logger.warning(f"GPU deduplication failed, falling back to CPU: {e}")
            return list(set(weights))

    async def _atomize_weight_batch(
        self, conn: AsyncConnection, weights: List
    ) -> Dict[Any, int]:
        """Batch atomize multiple weights - 100-200x faster than individual calls.
        Uses GPU acceleration when available and enabled."""
        import time
        from decimal import Decimal
        
        # Determine device
        use_gpu = settings.use_gpu and GPU_AVAILABLE
        
        start_time = time.time()
        
        # Deduplicate input weights using GPU if available
        # Note: GPU returns float, CPU preserves Decimal - normalize to Decimal
        logger.info(f"    Deduplicating {len(weights):,} weights...")
        if use_gpu:
            import numpy as np
            unique_floats = self._deduplicate_weights_gpu(weights)
            # Convert back to Decimal for consistent cache keys
            unique_weights = [Decimal(str(f)) for f in unique_floats]
            device_used = "GPU"
        else:
            unique_weights = list(set(weights))
            device_used = "CPU"
        
        dedup_time = time.time() - start_time
        logger.info(f"    → {len(unique_weights):,} unique weights [{device_used}] ({dedup_time:.2f}s)")

        # Check cache first (pre-build lookup set, then minimal lock for read)
        logger.info(f"    Checking cache for {len(unique_weights):,} unique weights...")
        cache_start = time.time()
        # Build lookup set outside lock to minimize lock duration
        unique_weights_set = set(unique_weights)
        async with self.cache_lock:
            # Atomic snapshot of cache keys
            cached_keys = set(self.cache.keys())
        # Find uncached weights without holding lock
        uncached_weights = [w for w in unique_weights if w not in cached_keys]
        cache_time = time.time() - cache_start
        cache_hits = len(unique_weights) - len(uncached_weights)
        logger.info(f"    → {cache_hits:,} cache hits, {len(uncached_weights):,} need atomization ({cache_time:.2f}s)")

        if uncached_weights:
            # Use PostgreSQL COPY for 100-200x speedup vs INSERT
            logger.info(f"    Atomizing {len(uncached_weights):,} new weights via COPY (columnar)...")
            db_start = time.time()
            
            import hashlib
            import json
            
            # Build rows for COPY (stay in NumPy/Python - no stored procedure overhead)
            logger.info(f"    → Building {len(uncached_weights):,} hash rows...")
            sys.stdout.flush()
            rows = []
            for idx, weight in enumerate(uncached_weights):
                weight_bytes = str(weight).encode('utf-8')
                content_hash = hashlib.sha256(weight_bytes).digest()
                canonical_text = str(weight)
                metadata = json.dumps({"modality": "weight"})
                rows.append((content_hash, canonical_text, metadata))
                
                # Progress every 50 weights
                if (idx + 1) % 50 == 0 or (idx + 1) == len(uncached_weights):
                    print(f"    → Hashed {idx+1}/{len(uncached_weights)} weights", end='\r', flush=True)
            print()  # New line after progress
            
            # Bulk insert with COPY - bypasses stored procedure overhead
            logger.info(f"    → Writing {len(rows):,} rows via COPY...")
            sys.stdout.flush()
            async with conn.cursor() as cur:
                # Insert atoms via COPY with batched writes for better performance
                async with cur.copy(
                    "COPY atom (content_hash, canonical_text, metadata) FROM STDIN"
                ) as copy:
                    # Write in batches to reduce await overhead
                    WRITE_BATCH = 1000
                    for i in range(0, len(rows), WRITE_BATCH):
                        batch = rows[i:i+WRITE_BATCH]
                        for row in batch:
                            await copy.write_row(row)
                        # Progress every 10k rows
                        if (i + WRITE_BATCH) % 10000 == 0:
                            print(f"    → Wrote {i+WRITE_BATCH:,}/{len(rows):,} rows", flush=True)
                            await asyncio.sleep(0)  # Yield to event loop
                
                logger.info(f"    → Retrieving atom IDs...")
                sys.stdout.flush()
                # Retrieve atom_ids for the inserted atoms
                # Use temp table + JOIN for better performance than ANY array
                hashes = [row[0] for row in rows]
                await cur.execute(
                    "SELECT content_hash, atom_id FROM atom WHERE content_hash = ANY(%s::bytea[])",
                    (hashes,)
                )
                results = await cur.fetchall()
            
            db_time = time.time() - db_start
            logger.info(f"    → COPY atomization complete ({db_time:.2f}s, {len(uncached_weights)/db_time:.0f} weights/s)")

            logger.info(f"    Building cache mappings for {len(results):,} results...")
            cache_build_start = time.time()

            # Build mapping from content_hash to atom_id
            result_map = {
                content_hash: atom_id for content_hash, atom_id in results
            }

            # Update cache using original uncached weights as keys
            # Map weight → hash → atom_id (build dict outside lock, then single batch update)
            cache_updates = {}
            for idx, orig_weight in enumerate(uncached_weights):
                # Get corresponding hash from rows we built
                content_hash = rows[idx][0]
                atom_id = result_map[content_hash]
                cache_updates[orig_weight] = atom_id
            
            # Single lock acquisition for batch update
            async with self.cache_lock:
                self.cache.update(cache_updates)
                self.stats["atoms_created"] += len(cache_updates)
            
            cache_build_time = time.time() - cache_build_start
            logger.info(f"    → Cache updated with {len(uncached_weights):,} new entries ({cache_build_time:.2f}s)")

        # Count cache hits
        self.stats["atoms_deduped"] += len(weights) - len(uncached_weights)
        
        # Log final performance summary
        total_time = time.time() - start_time
        logger.info(
            f"    ✓ Batch complete: {len(weights):,} weights → {len(unique_weights):,} unique "
            f"({len(uncached_weights):,} new) [{device_used}] (total: {total_time:.2f}s)"
        )

        # Return mapping for all weights (cache is stable here, no lock needed for read-only dict comp)
        # Dict reads are atomic in Python, and we only read keys that we know exist
        return {w: self.cache[w] for w in weights}

    async def _create_composition_batch(
        self, conn: AsyncConnection, parent_id: int, compositions: List[Dict[str, int]]
    ):
        """Batch create compositions - much faster than individual inserts."""
        import time
        
        logger.info(f"    Creating {len(compositions):,} compositions in batches...")
        total_start = time.time()
        
        batch_size = 50000  # Increased for better throughput (~400KB per batch)
        total_batches = (len(compositions) + batch_size - 1) // batch_size

        for batch_idx, i in enumerate(range(0, len(compositions), batch_size)):
            batch = compositions[i : i + batch_size]
            batch_num = batch_idx + 1
            batch_start = i
            batch_end = min(i + batch_size, len(compositions))

            batch_insert_start = time.time()
            async with conn.cursor() as cur:
                # Use COPY for 100-200x speedup vs stored procedure INSERT
                async with cur.copy(
                    "COPY atom_composition (parent_atom_id, component_atom_id, sequence_index) FROM STDIN"
                ) as copy:
                    # Write in batches to reduce await overhead
                    WRITE_BATCH = 10000
                    for write_idx in range(0, len(batch), WRITE_BATCH):
                        write_batch = batch[write_idx:write_idx+WRITE_BATCH]
                        for comp in write_batch:
                            await copy.write_row((
                                int(parent_id),
                                int(comp["component_id"]),
                                int(comp["sequence_idx"])
                            ))
                        # Yield to event loop more frequently (every write batch, not every 50k)
                        await asyncio.sleep(0)
            batch_insert_time = time.time() - batch_insert_start

            # Progress reporting every batch
            progress_pct = (batch_end / len(compositions)) * 100
            elapsed = time.time() - total_start
            rate = batch_end / elapsed if elapsed > 0 else 0
            eta = (len(compositions) - batch_end) / rate if rate > 0 else 0
            logger.info(
                f"    [{progress_pct:5.1f}%] Batch {batch_num}/{total_batches}: "
                f"Inserted {batch_end:,}/{len(compositions):,} ({batch_insert_time:.2f}s, "
                f"{rate:.0f} comps/s, ETA: {eta:.0f}s)"
            )
        
        total_time = time.time() - total_start
        logger.info(f"    ✓ All compositions created ({total_time:.2f}s, {len(compositions)/total_time:.0f} comps/s)")

    async def _atomize_vocabulary(
        self, conn: AsyncConnection, reader: Any, model_atom_id: int  # gguf.GGUFReader
    ):
        """
        Atomize tokenizer vocabulary as semantic landmarks.

        Each token becomes an atom with:
        - canonical_text: The token string
        - metadata: {modality: "tokenizer/vocabulary", token_id, frequency_rank}
        - spatial_key: PointZM with semantic positioning

        Cross-model deduplication: Same token_text → same atom_id
        """
        logger.info("Atomizing tokenizer vocabulary...")

        # Extract vocabulary from GGUF
        tokens_field = reader.fields.get("tokenizer.ggml.tokens")
        if not tokens_field:
            logger.warning(
                "No tokenizer.ggml.tokens field found, skipping vocabulary atomization"
            )
            return

        # Parse tokens using official GGUF reader approach:
        # field.data contains indices into field.parts where actual string bytes are stored
        vocab_size = len(tokens_field.data)
        logger.info(f"  Found {vocab_size:,} tokens in vocabulary")

        # Create vocabulary parent atom
        vocab_hash = hashlib.sha256(f"vocabulary:{model_atom_id}".encode()).digest()
        vocab_atom_id = await self.create_atom(
            conn,
            vocab_hash,
            "tokenizer_vocabulary",
            {"modality": "tokenizer/vocabulary_root", "vocab_size": vocab_size},
        )
        await self.create_composition(conn, model_atom_id, vocab_atom_id, 0)

        # Vectorized vocabulary atomization (CLR-style Python bulk operations)
        import time

        # Determine device
        use_gpu = settings.use_gpu and GPU_AVAILABLE
        device = "GPU" if use_gpu else "CPU"

        start_time = time.time()
        logger.info(f"  Atomizing vocabulary: {vocab_size:,} tokens (vectorized) [Device: {device}]")

        # Step 1: Bulk decode all tokens
        logger.info("    Step 1/5: Decoding tokens...")
        decode_start = time.time()
        all_tokens = []
        for token_id in range(vocab_size):
            token_idx = tokens_field.data[token_id]
            token_bytes = tokens_field.parts[token_idx]
            token_text = bytes(token_bytes).decode("utf-8", errors="replace")
            all_tokens.append(token_text)
        decode_time = time.time() - decode_start
        logger.info(
            f"      Decoded {vocab_size:,} tokens in {decode_time:.2f}s ({vocab_size/decode_time:.0f} tok/s)"
        )

        # Step 1.5: Generate semantic embeddings for tokens
        logger.info("    Step 1.5/6: Generating semantic embeddings...")
        embedding_start = time.time()
        try:
            # Generate 3D coordinates from semantic embeddings via trilateration
            semantic_coords = generate_semantic_coordinates(all_tokens, fit_references=True)
            logger.info(f"      Generated semantic embeddings in {time.time() - embedding_start:.2f}s")
        except Exception as e:
            logger.warning(f"Failed to generate embeddings, falling back to hash-based: {e}")
            semantic_coords = [None] * vocab_size

        # Step 2: Extract unique characters and hash in Python
        logger.info("    Step 2/6: Extracting unique characters...")
        extract_start = time.time()
        char_to_hash = {}
        for token in all_tokens:
            for char in token:
                if char not in char_to_hash:
                    char_to_hash[char] = hashlib.sha256(char.encode("utf-8")).digest()

        unique_chars = list(char_to_hash.keys())
        unique_hashes = [char_to_hash[c] for c in unique_chars]
        total_chars = sum(len(token) for token in all_tokens)
        char_dedup_ratio = total_chars / len(unique_chars) if unique_chars else 0
        extract_time = time.time() - extract_start
        logger.info(
            f"      Found {len(unique_chars):,} unique chars from {total_chars:,} total "
            f"({char_dedup_ratio:.1f}x dedup) in {extract_time:.2f}s"
        )

        # Step 3: Batch insert character atoms with graceful conflict handling
        logger.info("    Step 3/6: Inserting character atoms...")
        insert_start = time.time()
        char_metadatas = [
            json.dumps({"modality": "character", "char": c}) for c in unique_chars
        ]

        async with conn.cursor() as cur:
            # Simple INSERT with ON CONFLICT DO NOTHING (graceful failover)
            # Characters don't have spatial keys, so we omit that column
            await cur.execute(
                """
                INSERT INTO atom (content_hash, canonical_text, metadata)
                SELECT * FROM unnest(%s::bytea[], %s::text[], %s::jsonb[])
                ON CONFLICT (content_hash) DO NOTHING
            """,
                (unique_hashes, unique_chars, char_metadatas),
            )

            # Query back all atom IDs (newly inserted + pre-existing)
            await cur.execute(
                """
                SELECT content_hash, atom_id FROM atom WHERE content_hash = ANY(%s)
            """,
                (unique_hashes,),
            )

            hash_to_atom_id = {bytes(row[0]): row[1] for row in await cur.fetchall()}

        char_to_atom_id = {c: hash_to_atom_id[char_to_hash[c]] for c in unique_chars}
        insert_time = time.time() - insert_start
        logger.info(f"      Inserted character atoms in {insert_time:.2f}s")

        # Step 4: Create token atoms with spatial keys and embeddings
        logger.info("    Step 4/6: Creating token atoms with semantic embeddings...")
        token_start = time.time()

        token_hashes = [
            hashlib.sha256(token.encode("utf-8")).digest() for token in all_tokens
        ]
        token_metadatas = [
            json.dumps(
                {
                    "modality": "tokenizer/vocabulary",
                    "token_id": i,
                    "char_count": len(all_tokens[i]),
                    "vocab_size": vocab_size,
                    # Store semantic embedding coordinates for similarity queries
                    "semantic_coords": list(semantic_coords[i]) if semantic_coords[i] else None,
                }
            )
            for i in range(vocab_size)
        ]
        token_spatial_keys = [
            spatial_key_to_wkt(
                calculate_vocabulary_spatial_key(
                    token_id=i,
                    token_text=all_tokens[i],
                    frequency_rank=None,
                    embedding=semantic_coords[i],  # Pass semantic coordinates
                    vocab_size=vocab_size,
                )
            )
            for i in range(vocab_size)
        ]

        async with conn.cursor() as cur:
            # Simple INSERT with ON CONFLICT DO NOTHING
            # Convert WKT strings to geometries using ST_GeomFromText
            await cur.execute(
                """
                INSERT INTO atom (content_hash, canonical_text, metadata, spatial_key)
                SELECT 
                    hash,
                    text,
                    meta,
                    ST_GeomFromText(wkt)
                FROM unnest(%s::bytea[], %s::text[], %s::jsonb[], %s::text[]) 
                AS t(hash, text, meta, wkt)
                ON CONFLICT (content_hash) DO NOTHING
                RETURNING atom_id
            """,
                (token_hashes, all_tokens, token_metadatas, token_spatial_keys),
            )

            returned_ids = [row[0] for row in await cur.fetchall()]

            # If conflicts occurred, query for all token IDs
            if len(returned_ids) < vocab_size:
                await cur.execute(
                    """
                    SELECT content_hash, atom_id FROM atom WHERE content_hash = ANY(%s)
                """,
                    (token_hashes,),
                )
                hash_to_token_id = {
                    bytes(row[0]): row[1] for row in await cur.fetchall()
                }
                token_atom_ids = [hash_to_token_id[h] for h in token_hashes]
            else:
                token_atom_ids = returned_ids

        token_time = time.time() - token_start
        logger.info(f"      Created {vocab_size:,} token atoms in {token_time:.2f}s")

        # Step 5: Build and insert all compositions
        logger.info("    Step 5/6: Building compositions...")
        comp_start = time.time()

        # Token->character compositions
        char_parent_ids = []
        char_component_ids = []
        char_sequence_indices = []
        for token_idx, token_text in enumerate(all_tokens):
            token_atom_id = token_atom_ids[token_idx]
            for seq, char in enumerate(token_text):
                char_parent_ids.append(token_atom_id)
                char_component_ids.append(char_to_atom_id[char])
                char_sequence_indices.append(seq)

        # Vocabulary->token compositions
        vocab_parent_ids = [vocab_atom_id] * vocab_size
        vocab_component_ids = token_atom_ids
        vocab_sequence_indices = list(range(vocab_size))

        async with conn.cursor() as cur:
            # Disable trigger for bulk operations
            await cur.execute(
                "ALTER TABLE atom_composition DISABLE TRIGGER trigger_increment_refcount"
            )

            # Insert token->char compositions
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                SELECT * FROM unnest(%s::bigint[], %s::bigint[], %s::bigint[])
                ON CONFLICT DO NOTHING
            """,
                (char_parent_ids, char_component_ids, char_sequence_indices),
            )

            # Insert vocabulary->token compositions
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                SELECT * FROM unnest(%s::bigint[], %s::bigint[], %s::bigint[])
                ON CONFLICT DO NOTHING
            """,
                (vocab_parent_ids, vocab_component_ids, vocab_sequence_indices),
            )

            # Batch update reference counts for character atoms
            await cur.execute(
                """
                UPDATE atom SET reference_count = reference_count + refcount_delta
                FROM (
                    SELECT component_atom_id, COUNT(*) as refcount_delta
                    FROM unnest(%s::bigint[], %s::bigint[]) AS t(parent_atom_id, component_atom_id)
                    GROUP BY component_atom_id
                ) AS deltas
                WHERE atom.atom_id = deltas.component_atom_id
            """,
                (char_parent_ids, char_component_ids),
            )

            # Batch update reference counts for token atoms
            await cur.execute(
                """
                UPDATE atom SET reference_count = reference_count + 1
                WHERE atom_id = ANY(%s)
            """,
                (vocab_component_ids,),
            )

            await cur.execute(
                "ALTER TABLE atom_composition ENABLE TRIGGER trigger_increment_refcount"
            )

        total_compositions = len(char_parent_ids) + len(vocab_parent_ids)
        comp_time = time.time() - comp_start
        logger.info(
            f"      Inserted {total_compositions:,} compositions in {comp_time:.2f}s"
        )

        # Final summary
        total_time = time.time() - start_time
        logger.info(
            f"  ✓ Vocabulary atomization complete in {total_time:.1f}s:\n"
            f"    • {vocab_size:,} tokens atomized ({vocab_size/total_time:.0f} tok/s)\n"
            f"    • {len(unique_chars):,} unique character atoms ({char_dedup_ratio:.1f}x dedup)\n"
            f"    • {total_compositions:,} compositions created\n"
            f"    • Average {total_chars/vocab_size:.1f} chars/token"
        )

    async def _atomize_architecture(
        self, conn: AsyncConnection, reader: Any, model_atom_id: int  # gguf.GGUFReader
    ):
        """
        Atomize architecture hyperparameters as geometric manifold constraints.

        Each hyperparameter becomes an atom that defines the model's geometric space.
        Models with same architecture share these atoms → cross-model deduplication.
        """
        logger.info("Atomizing architecture hyperparameters...")

        # Extract architecture config from GGUF fields
        config = {}
        for field_name, field in reader.fields.items():
            if any(
                prefix in field_name for prefix in ["general.", "qwen3moe.", "llama."]
            ):
                # Extract scalar values
                if hasattr(field, "parts") and len(field.parts) > 0:
                    value = field.parts[field.data[0]]
                    # Convert numpy types to Python types
                    if hasattr(value, "item"):
                        try:
                            value = value.item()
                        except ValueError:
                            # Array field - convert to list
                            value = (
                                value.tolist()
                                if hasattr(value, "tolist")
                                else list(value)
                            )
                    config[
                        field_name.replace("general.", "")
                        .replace("qwen3moe.", "")
                        .replace("llama.", "")
                    ] = value

        logger.info(f"  Extracted {len(config)} architecture parameters")

        # Calculate spatial coordinates for architecture
        x, y, z, m = calculate_architecture_spatial_key(config)

        # Create architecture atom
        arch_hash = hashlib.sha256(json.dumps(config, sort_keys=True).encode()).digest()
        arch_atom_id = await self.create_atom(
            conn,
            arch_hash,
            "model_architecture",
            {
                "modality": "architecture/config",
                **{
                    k: v
                    for k, v in config.items()
                    if isinstance(v, (int, float, str, bool))
                },
            },
            spatial_key=spatial_key_to_wkt((x, y, z, m)),
        )

        # Compose under model atom
        await self.create_composition(conn, model_atom_id, arch_atom_id, 1)

        logger.info(
            f"  ✓ Atomized architecture: embedding_dim={config.get('embedding_length', 'N/A')}, "
            f"layers={config.get('block_count', 'N/A')}, heads={config.get('attention.head_count', 'N/A')}"
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
