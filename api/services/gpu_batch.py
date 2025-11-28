"""
GPU Batch Processing Service

Provides GPU-accelerated batch operations for:
- Text embedding generation (1000+ texts at once)
- Hash computation (batched SHA-256)
- Tensor operations (weights, activations)

Uses PostgreSQL PL/Python + PyTorch for in-database GPU acceleration.
"""

import asyncio
import logging
from typing import Any, Dict, List, Optional

from psycopg import AsyncConnection

logger = logging.getLogger(__name__)


class GPUBatchService:
    """GPU-accelerated batch operations using PostgreSQL PL/Python."""

    @staticmethod
    async def batch_hash_atoms(
        conn: AsyncConnection, texts: List[str], batch_size: int = 1000
    ) -> List[bytes]:
        """
        Batch hash text chunks using GPU acceleration.

        Args:
            conn: Database connection
            texts: List of text strings to hash
            batch_size: Number of texts per GPU batch

        Returns:
            List of SHA-256 hashes (32 bytes each)
        """
        if not texts:
            return []

        # Call GPU batch hashing function in PostgreSQL
        result = await conn.execute(
            """
            SELECT gpu_batch_hash_texts(
                $1::text[],
                $2::integer
            )
            """,
            (texts, batch_size),
        )

        hashes = await result.fetchone()
        return hashes[0] if hashes else []

    @staticmethod
    async def batch_generate_embeddings(
        conn: AsyncConnection,
        texts: List[str],
        model_name: str = "sentence-transformers/all-MiniLM-L6-v2",
        batch_size: int = 32,
    ) -> List[List[float]]:
        """
        Generate embeddings for multiple texts using GPU.

        Uses sentence-transformers in PostgreSQL PL/Python with GPU.
        Processes in batches to manage GPU memory.

        Args:
            conn: Database connection
            texts: List of text strings to embed
            model_name: HuggingFace model name
            batch_size: Batch size for GPU processing

        Returns:
            List of embedding vectors (384-dim for MiniLM)
        """
        if not texts:
            return []

        logger.info(
            f"Generating embeddings for {len(texts)} texts (batch_size={batch_size})"
        )

        # Call GPU embedding function
        result = await conn.execute(
            """
            SELECT gpu_batch_generate_embeddings(
                $1::text[],
                $2::text,
                $3::integer
            )
            """,
            (texts, model_name, batch_size),
        )

        embeddings = await result.fetchone()
        return embeddings[0] if embeddings else []

    @staticmethod
    async def batch_atomize_with_embeddings(
        conn: AsyncConnection,
        text: str,
        metadata: Optional[Dict[str, Any]] = None,
        generate_embeddings: bool = True,
    ) -> Dict[str, Any]:
        """
        Atomize text and generate embeddings in one GPU batch.

        1. Atomize text into characters/words/sentences
        2. Batch generate embeddings for all atoms
        3. Store with spatial positioning

        Args:
            conn: Database connection
            text: Text to atomize
            metadata: Optional metadata
            generate_embeddings: Whether to generate embeddings

        Returns:
            {
                'atom_count': int,
                'root_atom_id': int,
                'embedding_time_ms': float,
                'atoms': List[int]
            }
        """
        import time

        start = time.time()

        # Step 1: Basic atomization (characters)
        atom_ids = await conn.fetchval(
            "SELECT atomize_text($1, $2::jsonb)", text, metadata or {}
        )

        atomization_time = (time.time() - start) * 1000

        if not generate_embeddings or not atom_ids:
            return {
                "atom_count": len(atom_ids),
                "root_atom_id": atom_ids[0] if atom_ids else None,
                "embedding_time_ms": 0,
                "atoms": atom_ids,
            }

        # Step 2: Get atom contents for embedding
        start_embed = time.time()

        rows = await conn.execute(
            """
            SELECT atom_id, content_data
            FROM atom
            WHERE atom_id = ANY($1)
            ORDER BY array_position($1, atom_id)
            """,
            (atom_ids,),
        )

        atom_contents = await rows.fetchall()
        texts_to_embed = [
            row[1].decode("utf-8") if isinstance(row[1], bytes) else row[1]
            for row in atom_contents
        ]

        # Step 3: Batch generate embeddings
        embeddings = await GPUBatchService.batch_generate_embeddings(
            conn, texts_to_embed, batch_size=128
        )

        # Step 4: Update atoms with embeddings
        if embeddings:
            for atom_id, embedding in zip(atom_ids, embeddings):
                await conn.execute(
                    """
                    UPDATE atom
                    SET embedding = $1::vector(384)
                    WHERE atom_id = $2
                    """,
                    (embedding, atom_id),
                )

        embedding_time = (time.time() - start_embed) * 1000

        logger.info(
            f"Atomized {len(atom_ids)} atoms with embeddings: "
            f"{atomization_time:.2f}ms atomization + {embedding_time:.2f}ms embeddings"
        )

        return {
            "atom_count": len(atom_ids),
            "root_atom_id": atom_ids[0] if atom_ids else None,
            "atomization_time_ms": atomization_time,
            "embedding_time_ms": embedding_time,
            "total_time_ms": atomization_time + embedding_time,
            "atoms": atom_ids,
        }

    @staticmethod
    async def benchmark_gpu_performance(
        conn: AsyncConnection, text_samples: List[str], iterations: int = 10
    ) -> Dict[str, Any]:
        """
        Benchmark GPU batch processing performance.

        Tests:
        - Single vs batch hashing
        - Single vs batch embedding
        - CPU vs GPU comparison

        Returns benchmark results.
        """
        import time

        results = {
            "sample_count": len(text_samples),
            "iterations": iterations,
            "tests": {},
        }

        # Test 1: Batch hashing
        start = time.time()
        for _ in range(iterations):
            await GPUBatchService.batch_hash_atoms(conn, text_samples)
        batch_hash_time = (time.time() - start) * 1000

        results["tests"]["batch_hash"] = {
            "total_ms": batch_hash_time,
            "per_sample_ms": batch_hash_time / (len(text_samples) * iterations),
            "throughput": (len(text_samples) * iterations) / (batch_hash_time / 1000),
        }

        # Test 2: Batch embeddings
        start = time.time()
        for _ in range(iterations):
            await GPUBatchService.batch_generate_embeddings(
                conn, text_samples, batch_size=32
            )
        batch_embed_time = (time.time() - start) * 1000

        results["tests"]["batch_embeddings"] = {
            "total_ms": batch_embed_time,
            "per_sample_ms": batch_embed_time / (len(text_samples) * iterations),
            "throughput": (len(text_samples) * iterations) / (batch_embed_time / 1000),
        }

        logger.info(f"GPU Benchmark Results: {results}")
        return results


__all__ = ["GPUBatchService"]
