"""GGUF Model Atomization Service."""

import hashlib
import json
import logging
from pathlib import Path
from typing import Any, Dict, List, Optional, Tuple

from psycopg import AsyncConnection

from api.config import settings
from api.services.spatial_encoding import (calculate_architecture_spatial_key,
                                           calculate_merge_spatial_key,
                                           calculate_vocabulary_spatial_key,
                                           calculate_weight_spatial_key,
                                           spatial_key_to_wkt)
from src.core.atomization.base_atomizer import BaseAtomizer

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

    async def atomize_model(
        self,
        file_path: Path,
        model_name: str,
        conn: AsyncConnection,
        max_tensors: Optional[int] = None,
    ) -> Dict[str, Any]:
        """Atomize GGUF model following hierarchical composition pattern."""
        
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

        model_atom_id = await self.create_atom(
            conn, model_content_hash, model_name, model_metadata
        )

        logger.info(f"Model atom created (new): {model_atom_id}")

        await self._atomize_gguf_file(conn, file_path, model_atom_id, max_tensors)

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

        # Phase 3: Atomize tensor weights
        tensors_to_process = reader.tensors
        if max_tensors:
            tensors_to_process = reader.tensors[:max_tensors]
            logger.info(f"Processing first {max_tensors} tensors")
        
        import numpy as np
        
        for tensor_idx, tensor in enumerate(tensors_to_process):
            logger.info(
                f"Processing tensor {tensor_idx+1}/{len(reader.tensors)}: {tensor.name} {tensor.shape}"
            )

            # Create tensor metadata atom
            tensor_hash = hashlib.sha256(tensor.name.encode()).digest()
            tensor_atom_id = await self.create_atom(
                conn,
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

            await self.create_composition(
                conn, model_atom_id, tensor_atom_id, tensor_idx
            )
            self.stats["tensors_processed"] = self.stats.get("tensors_processed", 0) + 1

            # Flatten tensor and atomize weights with BATCHING + SIMD/GPU
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

                    # Transfer back to CPU, preserve precision as Decimal
                    from decimal import Decimal

                    non_sparse_indices = non_sparse_indices_gpu.get().tolist()
                    non_sparse_weights = [
                        Decimal(str(float(w)))
                        for w in non_sparse_weights_gpu.get().tolist()
                    ]

                    logger.info(
                        f"  [GPU] Processed {total_weights:,} weights | {unique_values:,} unique"
                    )
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
                from decimal import Decimal

                non_sparse_indices = np.where(~sparse_mask)[0].tolist()
                non_sparse_weights = [
                    Decimal(str(float(w))) for w in weights[~sparse_mask].tolist()
                ]

                logger.info(
                    f"  [CPU SIMD] Processed {total_weights:,} weights | {unique_values:,} unique"
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
            weight_to_atom = await self._atomize_weight_batch(conn, non_sparse_weights)

            # Build compositions (vectorized preparation)
            compositions = [
                {"component_id": weight_to_atom[weight], "sequence_idx": int(idx)}
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
            sparse_pct = (
                self.stats.get("sparse_skipped", 0) / self.stats["total_processed"]
            ) * 100
            logger.info(
                f"  ✓ Tensor complete: {self.stats['total_processed']:,} weights processed, "
                f"{unique_so_far:,} unique atoms, {dedup_ratio:.1f}x dedup, {sparse_pct:.1f}% sparse"
            )

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

    def _deduplicate_weights_gpu(self, weights: List) -> List:
        """Deduplicate weights using GPU acceleration (5-10x faster for large batches)."""
        try:
            import numpy as np
            
            # Convert to numpy array, then to GPU
            weights_np = np.array(weights, dtype=np.float32)
            weights_gpu = cp.asarray(weights_np)
            
            # GPU-accelerated unique operation
            unique_gpu = cp.unique(weights_gpu)
            
            # Transfer back to CPU and convert to list
            unique_np = cp.asnumpy(unique_gpu)
            return unique_np.tolist()
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

        # Check cache first
        uncached_weights = [w for w in unique_weights if w not in self.cache]

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
                    (uncached_weights,),
                )
                results = await cur.fetchall()

                # Build mapping from returned numeric to atom_id
                result_map = {
                    float(weight_val): atom_id for weight_val, atom_id in results
                }

                # Update cache using original uncached weights as keys
                # (handles floating-point precision issues)
                for orig_weight in uncached_weights:
                    # Find closest match in results (should be exact or very close)
                    if orig_weight in result_map:
                        self.cache[orig_weight] = result_map[orig_weight]
                    else:
                        # Fallback: find by minimum distance (for floating-point precision)
                        # Convert to Decimal for comparison
                        from decimal import Decimal
                        orig_dec = Decimal(str(float(orig_weight))) if not isinstance(orig_weight, Decimal) else orig_weight
                        closest = min(
                            result_map.keys(), key=lambda x: abs(Decimal(str(float(x))) - orig_dec)
                        )
                        self.cache[orig_weight] = result_map[closest]
                    self.stats["atoms_created"] += 1

        # Count cache hits
        self.stats["atoms_deduped"] += len(weights) - len(uncached_weights)
        
        # Log performance
        if len(uncached_weights) > 0:
            total_time = time.time() - start_time
            logger.debug(
                f"    Weight batch: {len(weights):,} weights → {len(unique_weights):,} unique "
                f"({len(uncached_weights):,} new) [{device_used}] "
                f"(dedup: {dedup_time*1000:.1f}ms, total: {total_time*1000:.1f}ms)"
            )

        # Return mapping for all weights
        return {w: self.cache[w] for w in weights}

    async def _create_composition_batch(
        self, conn: AsyncConnection, parent_id: int, compositions: List[Dict[str, int]]
    ):
        """Batch create compositions - much faster than individual inserts."""
        batch_size = 50000  # Increased for better throughput (~400KB per batch)
        total_batches = (len(compositions) + batch_size - 1) // batch_size

        for batch_idx, i in enumerate(range(0, len(compositions), batch_size)):
            batch = compositions[i : i + batch_size]
            batch_num = batch_idx + 1
            batch_start = i
            batch_end = min(i + batch_size, len(compositions))

            async with conn.cursor() as cur:
                await cur.execute(
                    "SELECT create_composition_batch(%s::bigint, %s::jsonb[])",
                    (parent_id, [json.dumps(c) for c in batch]),
                )

            # Progress reporting every batch
            progress_pct = (batch_end / len(compositions)) * 100
            logger.info(
                f"    [{progress_pct:5.1f}%] Batch {batch_num}/{total_batches}: "
                f"Inserted {batch_end:,}/{len(compositions):,} compositions"
            )

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

        # Step 2: Extract unique characters and hash in Python
        logger.info("    Step 2/5: Extracting unique characters...")
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
        logger.info("    Step 3/5: Inserting character atoms...")
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

        # Step 4: Create token atoms with spatial keys
        logger.info("    Step 4/5: Creating token atoms...")
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
                    embedding=None,
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
        logger.info("    Step 5/5: Building compositions...")
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
