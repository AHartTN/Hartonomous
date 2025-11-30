"""Test GGUF ingestion with cached test model."""

import asyncio
import logging
from pathlib import Path

import pytest
from psycopg_pool import AsyncConnectionPool

from api.config import settings
from api.dependencies import set_connection_pool
from api.services.model_atomization import GGUFAtomizer

pytestmark = [pytest.mark.asyncio, pytest.mark.integration, pytest.mark.gguf]

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


async def test_gguf_ingestion(test_gguf_path):
    """Test ingesting vocabulary, architecture, and weights from TinyLlama test model."""
    model_path = test_gguf_path

    if not model_path.exists():
        raise FileNotFoundError(
            f"Test model not found: {model_path}. Run performance tests to download."
        )

    logger.info(f"Testing GGUF ingestion: {model_path.name}")
    logger.info(f"File size: {model_path.stat().st_size / 1e6:.2f} MB")

    # Get proper database connection string
    db_url = settings.get_connection_string()
    logger.info(
        f"Connecting to: {settings.pghost}:{settings.pgport}/{settings.pgdatabase}"
    )

    # Initialize connection pool with optimized settings
    logger.info(
        f"Initializing connection pool: min={settings.pool_min_size}, max={settings.pool_max_size}"
    )
    pool = AsyncConnectionPool(
        conninfo=db_url,
        min_size=settings.pool_min_size,
        max_size=settings.pool_max_size,
        timeout=settings.pool_timeout,
        max_idle=settings.pool_max_idle,
        max_lifetime=(
            settings.pool_max_lifetime
            if hasattr(settings, "pool_max_lifetime")
            else 3600
        ),
    )
    await pool.open()

    # Wait for pool to initialize
    logger.info("Waiting for connection pool to initialize...")
    await pool.wait(timeout=10.0)
    logger.info(f"✓ Pool initialized: {pool.name}")
    set_connection_pool(pool)

    try:
        # Use optimized atomizer with GPU and parallel processing
        atomizer = GGUFAtomizer(threshold=0.1, parallel_processing=True)

        async with pool.connection() as conn:
            async with conn.transaction():
                logger.info("Starting full model atomization...")
                result = await atomizer.atomize_model(
                    file_path=model_path,
                    model_name="test-tinyllama",
                    conn=conn,
                    pool=pool,
                )

        logger.info("=" * 80)
        logger.info("INGESTION RESULTS:")
        logger.info(f"  Total weights processed: {result['total_processed']:,}")
        logger.info(f"  Unique atoms created: {result['atoms_created']:,}")
        logger.info(f"  Sparse weights skipped: {result['sparse_skipped']:,}")
        logger.info(f"  Deduplication ratio: {result['deduplication_ratio']:.1f}x")
        logger.info(f"  Sparse savings: {result.get('sparse_percentage', 0):.1f}%")
        logger.info("=" * 80)

        # Validate results
        assert result["total_processed"] > 0
        assert result["atoms_created"] > 0
        assert result["deduplication_ratio"] >= 1.0

        # Pool health check
        logger.info(
            f"Pool stats: name={pool.name}, size={pool.size}, closed={pool.closed}"
        )
    finally:
        logger.info("Closing connection pool...")
        await pool.close()
        logger.info("✓ Pool closed")


if __name__ == "__main__":
    # For manual testing, use cached model
    import sys

    sys.path.insert(0, str(Path(__file__).parent.parent.parent))

    class MockFixture:
        def exists(self):
            return Path(
                ".cache/test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf"
            ).exists()

        @property
        def name(self):
            return "tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf"

        def stat(self):
            return Path(
                ".cache/test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf"
            ).stat()

    asyncio.run(
        test_gguf_ingestion(
            Path(".cache/test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf")
        )
    )
