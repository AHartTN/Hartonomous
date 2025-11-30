"""SafeTensors Model Atomization Service."""

import hashlib
import json
import logging
from pathlib import Path
from typing import Any, Dict, List, Optional

import numpy as np
from psycopg import AsyncConnection

from api.config import settings
from api.services.embedding_service import generate_semantic_coordinates
from api.services.spatial_encoding import (
    calculate_architecture_spatial_key,
    calculate_vocabulary_spatial_key,
    calculate_weight_spatial_key,
    spatial_key_to_wkt,
)
from src.core.atomization.base_atomizer import BaseAtomizer
from src.core.compression.encoding import MultiLayerEncoder

logger = logging.getLogger(__name__)

# GPU availability check
GPU_AVAILABLE = False
try:
    pass

    GPU_AVAILABLE = True
    logger.info("✓ GPU acceleration available via CuPy")
except ImportError:
    logger.info("✓ GPU not available, using CPU with NumPy SIMD")


class SafeTensorsAtomizer(BaseAtomizer):
    """SafeTensors model atomizer with hierarchical decomposition."""

    def __init__(self, threshold: float = 1e-6):
        """Initialize atomizer with sparsity threshold."""
        super().__init__()
        self.threshold = threshold
        self.cache: Dict[Any, Any] = {}
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
        config_path: Optional[Path] = None,
        tokenizer_path: Optional[Path] = None,
        max_tensors: Optional[int] = None,
    ) -> Dict[str, Any]:
        """Atomize SafeTensors model following hierarchical composition pattern."""

        use_gpu = settings.use_gpu and GPU_AVAILABLE
        device = "GPU" if use_gpu else "CPU"

        logger.info(
            f"Atomizing SafeTensors model: {model_name} ({file_path.stat().st_size / 1e9:.2f} GB) [Device: {device}]"
        )

        # Calculate file hash for deduplication
        file_hash = hashlib.sha256()
        with open(file_path, "rb") as f:
            while chunk := f.read(8192 * 1024):
                file_hash.update(chunk)
        model_content_hash = file_hash.digest()

        # Check if model already exists
        async with conn.cursor() as cur:
            await cur.execute(
                "SELECT atom_id FROM atom WHERE content_hash = %s",
                (model_content_hash,),
            )
            existing = await cur.fetchone()

            if existing:
                model_atom_id = existing[0]
                logger.info(f"Model atom already exists: {model_atom_id}")
                return {
                    "model_name": model_name,
                    "model_atom_id": model_atom_id,
                    "file_size_gb": file_path.stat().st_size / 1e9,
                    "tensors_processed": 0,
                    "total_processed": 0,
                    "atoms_created": 0,
                    "atoms_deduped": 0,
                    "sparse_skipped": 0,
                    "deduplication_ratio": 0,
                    "sparse_percentage": 0,
                }

        # Create model root atom
        model_atom_id = await self.create_atom(
            conn,
            model_content_hash,
            model_name,
            {
                "modality": "model/safetensors",
                "file_path": str(file_path),
                "file_size_bytes": file_path.stat().st_size,
            },
        )

        logger.info(f"Created model atom: {model_atom_id}")

        # Atomize components
        await self._atomize_safetensors_file(
            conn, file_path, model_atom_id, config_path, tokenizer_path, max_tensors
        )

        # Calculate statistics
        total = self.stats["total_processed"]
        sparse = self.stats.get("sparse_skipped", 0)
        created = self.stats["atoms_created"]
        deduped = self.stats["atoms_deduped"]

        return {
            "model_name": model_name,
            "model_atom_id": model_atom_id,
            "file_size_gb": file_path.stat().st_size / 1e9,
            "tensors_processed": self.stats.get("tensors_processed", 0),
            "total_processed": total,
            "atoms_created": created,
            "atoms_deduped": deduped,
            "sparse_skipped": sparse,
            "deduplication_ratio": (total / created) if created > 0 else 0,
            "sparse_percentage": (sparse / total * 100) if total > 0 else 0,
        }

    async def _atomize_safetensors_file(
        self,
        conn: AsyncConnection,
        file_path: Path,
        model_atom_id: int,
        config_path: Optional[Path],
        tokenizer_path: Optional[Path],
        max_tensors: Optional[int],
    ):
        """Parse SafeTensors file and atomize tensors."""
        try:
            from safetensors import safe_open
        except ImportError:
            logger.error(
                "safetensors library not installed. Run: pip install safetensors"
            )
            raise ImportError("Install safetensors: pip install safetensors")

        logger.info(f"Reading SafeTensors file: {file_path}")

        # Open SafeTensors file
        with safe_open(file_path, framework="np") as f:
            tensor_names = f.keys()
            total_tensors = len(tensor_names)
            logger.info(f"Found {total_tensors} tensors")

            # Phase 1: Atomize tokenizer (if provided)
            if tokenizer_path and tokenizer_path.exists():
                await self._atomize_tokenizer(conn, tokenizer_path, model_atom_id)

            # Phase 2: Atomize config (if provided)
            if config_path and config_path.exists():
                await self._atomize_config(conn, config_path, model_atom_id)

            # Phase 3: Atomize tensors
            tensors_to_process = list(tensor_names)
            if max_tensors:
                tensors_to_process = tensors_to_process[:max_tensors]
                logger.info(f"Processing first {max_tensors} tensors")

            for tensor_idx, tensor_name in enumerate(tensors_to_process):
                logger.info(
                    f"Processing tensor {tensor_idx+1}/{len(tensors_to_process)}: {tensor_name}"
                )

                # Get tensor data
                tensor_data = f.get_tensor(tensor_name)
                shape = tensor_data.shape

                # Create tensor metadata atom
                tensor_hash = hashlib.sha256(tensor_name.encode()).digest()
                tensor_atom_id = await self.create_atom(
                    conn,
                    tensor_hash,
                    tensor_name,
                    {
                        "modality": "tensor",
                        "shape": list(shape),
                        "dtype": str(tensor_data.dtype),
                        "sparse_threshold": self.threshold,
                        "n_elements": int(np.prod(shape)),
                    },
                )

                await self.create_composition(
                    conn, model_atom_id, tensor_atom_id, tensor_idx
                )
                self.stats["tensors_processed"] = (
                    self.stats.get("tensors_processed", 0) + 1
                )

                # Atomize weights
                weights = tensor_data.flatten()
                total_weights = len(weights)

                # Apply compression and sparse encoding
                weights_np = np.array(weights, dtype=np.float32)
                encoded_bytes, encoding_metadata = self.encoder.encode(weights_np)
                compressed_weights = np.frombuffer(encoded_bytes, dtype=np.float32)

                # Sparse filtering
                abs_weights = np.abs(compressed_weights)
                sparse_mask = abs_weights < self.threshold
                sparse_count = int(np.sum(sparse_mask))

                # Get non-sparse weights (keep as float32, no Decimal needed)
                non_sparse_indices = np.where(~sparse_mask)[0].tolist()
                non_sparse_weights = compressed_weights[
                    ~sparse_mask
                ].tolist()  # float32

                unique_values = len(np.unique(compressed_weights))
                rle_note = " (RLE applied)" if encoding_metadata.rle_applied else ""

                logger.info(
                    f"  Processed {total_weights:,} weights | "
                    f"{len(compressed_weights):,} after compression | "
                    f"{unique_values:,} unique{rle_note} | "
                    f"{sparse_count:,} sparse ({sparse_count/total_weights*100:.1f}%)"
                )

                self.stats["total_processed"] += total_weights
                self.stats["sparse_skipped"] = (
                    self.stats.get("sparse_skipped", 0) + sparse_count
                )

                # Batch atomize non-sparse weights
                if non_sparse_weights:
                    weight_atom_map = await self._atomize_weight_batch(
                        conn, non_sparse_weights
                    )

                    # Batch create all compositions using UNNEST
                    import time

                    comp_count = len(non_sparse_indices)
                    print(
                        f"  → Building {comp_count:,} composition records...",
                        flush=True,
                    )
                    comp_start = time.time()

                    parent_ids = [tensor_atom_id] * comp_count
                    component_ids = [
                        weight_atom_map[non_sparse_weights[i]]
                        for i in range(comp_count)
                    ]
                    sequence_idxs = non_sparse_indices

                    print(f"  → Inserting {comp_count:,} compositions...", flush=True)
                    async with conn.cursor() as cur:
                        await cur.execute(
                            """
                            INSERT INTO composition (parent_atom_id, component_atom_id, sequence_idx)
                            SELECT * FROM UNNEST(%s::bigint[], %s::bigint[], %s::integer[])
                            ON CONFLICT (parent_atom_id, component_atom_id) DO NOTHING
                        """,
                            (parent_ids, component_ids, sequence_idxs),
                        )

                    comp_elapsed = time.time() - comp_start
                    comp_rate = comp_count / comp_elapsed if comp_elapsed > 0 else 0
                    print(
                        f"  → Inserted {comp_count:,} compositions ({comp_elapsed:.2f}s, {comp_rate:,.0f} comps/s)",
                        flush=True,
                    )

    async def _atomize_tokenizer(
        self, conn: AsyncConnection, tokenizer_path: Path, model_atom_id: int
    ):
        """Atomize tokenizer vocabulary from tokenizer.json."""
        logger.info("Atomizing tokenizer vocabulary...")

        import time

        with open(tokenizer_path, "r", encoding="utf-8") as f:
            tokenizer_data = json.load(f)

        vocab = tokenizer_data.get("model", {}).get("vocab", {})
        if not vocab:
            logger.warning("No vocabulary found in tokenizer.json")
            return

        vocab_size = len(vocab)
        logger.info(f"  Found {vocab_size:,} tokens in vocabulary")

        # Create vocabulary root atom
        vocab_hash = hashlib.sha256(f"vocabulary:{model_atom_id}".encode()).digest()
        vocab_atom_id = await self.create_atom(
            conn,
            vocab_hash,
            "tokenizer_vocabulary",
            {"modality": "tokenizer/vocabulary_root", "vocab_size": vocab_size},
        )
        await self.create_composition(conn, model_atom_id, vocab_atom_id, 0)

        # Extract tokens
        start_time = time.time()
        all_tokens = list(vocab.keys())

        # Generate semantic embeddings
        logger.info("    Generating semantic embeddings...")
        try:
            semantic_coords = generate_semantic_coordinates(all_tokens, fit_pca=True)
            logger.info(
                f"      Generated semantic embeddings in {time.time() - start_time:.2f}s"
            )
        except Exception as e:
            logger.warning(f"Failed to generate embeddings: {e}")
            semantic_coords = [None] * vocab_size

        # Create token atoms with spatial keys
        token_hashes = [
            hashlib.sha256(token.encode("utf-8", errors="replace")).digest()
            for token in all_tokens
        ]
        token_metadatas = [
            json.dumps(
                {
                    "modality": "tokenizer/vocabulary",
                    "token_id": vocab[token],
                    "char_count": len(token),
                    "vocab_size": vocab_size,
                    "semantic_coords": (
                        list(semantic_coords[i]) if semantic_coords[i] else None
                    ),
                }
            )
            for i, token in enumerate(all_tokens)
        ]

        token_spatial_keys = [
            spatial_key_to_wkt(
                calculate_vocabulary_spatial_key(
                    token_id=vocab[all_tokens[i]],
                    token_text=all_tokens[i],
                    embedding=semantic_coords[i],
                    vocab_size=vocab_size,
                )
            )
            for i in range(vocab_size)
        ]

        # Batch insert tokens
        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom (content_hash, canonical_text, metadata, spatial_key)
                SELECT * FROM unnest(%s::bytea[], %s::text[], %s::jsonb[], %s::geometry[])
                ON CONFLICT (content_hash) DO NOTHING
                RETURNING atom_id
                """,
                (token_hashes, all_tokens, token_metadatas, token_spatial_keys),
            )

            inserted_ids = [row[0] for row in await cur.fetchall()]
            logger.info(f"      Inserted {len(inserted_ids):,} new token atoms")

        logger.info(
            f"  ✓ Vocabulary atomization complete in {time.time() - start_time:.1f}s"
        )

    async def _atomize_config(
        self, conn: AsyncConnection, config_path: Path, model_atom_id: int
    ):
        """Atomize model configuration."""
        logger.info("Atomizing model configuration...")

        with open(config_path, "r", encoding="utf-8") as f:
            config = json.load(f)

        logger.info(f"  Extracted {len(config)} configuration parameters")

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
        )

        await self.create_composition(conn, model_atom_id, arch_atom_id, 1)
        logger.info("  ✓ Configuration atomized")

    async def _atomize_weight_batch(
        self, conn: AsyncConnection, weights: List
    ) -> Dict[Any, int]:
        """Batch atomize multiple weights."""
        import time

        start = time.time()
        unique_weights = list(set(weights))
        dedup_ratio = len(weights) / len(unique_weights) if unique_weights else 0

        logger.info(
            f"    Batch atomizing {len(weights):,} weights → {len(unique_weights):,} unique ({dedup_ratio:.1f}x dedup)"
        )

        # Prepare batch data (simplified - metadata is just for storage)
        weight_hashes = [
            hashlib.sha256(str(w).encode()).digest() for w in unique_weights
        ]
        weight_texts = [str(w) for w in unique_weights]
        # Simplified metadata - just modality field (value is redundant with canonical_text)
        weight_metadatas = ['{"modality": "weight"}'] * len(
            unique_weights
        )  # Constant string, no json.dumps
        weight_spatial_keys = [
            spatial_key_to_wkt(calculate_weight_spatial_key(float(w)))
            for w in unique_weights
        ]

        # Batch insert
        async with conn.cursor() as cur:
            await cur.execute(
                """
                INSERT INTO atom (content_hash, canonical_text, metadata, spatial_key)
                SELECT * FROM unnest(%s::bytea[], %s::text[], %s::jsonb[], %s::geometry[])
                ON CONFLICT (content_hash) DO UPDATE SET reference_count = atom.reference_count + 1
                RETURNING atom_id, content_hash
                """,
                (weight_hashes, weight_texts, weight_metadatas, weight_spatial_keys),
            )

            results = await cur.fetchall()
            hash_to_id = {bytes(row[1]): row[0] for row in results}

        # Build weight -> atom_id map
        weight_atom_map = {}
        for i, weight in enumerate(unique_weights):
            weight_atom_map[weight] = hash_to_id[weight_hashes[i]]

        self.stats["atoms_created"] += len(unique_weights)
        self.stats["atoms_deduped"] += len(weights) - len(unique_weights)

        elapsed = time.time() - start
        logger.info(
            f"    Batch atomized in {elapsed:.2f}s ({len(unique_weights)/elapsed:.0f} atoms/s)"
        )

        return weight_atom_map
