"""Test GGUF ingestion with real Ollama model."""

import asyncio
import logging
import sys
from pathlib import Path
from api.services.model_atomization import GGUFAtomizer
from api.dependencies import set_connection_pool
from psycopg_pool import AsyncConnectionPool
from api.config import settings

# Fix for Windows ProactorEventLoop issue with psycopg
if sys.platform == "win32":
    asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)


async def test_gguf_ingestion():
    """Test ingesting vocabulary, architecture, and weights from qwen3-coder:30b."""
    # Ollama blob path from `ollama show qwen3-coder:30b --modelfile`
    model_path = Path(
        r"D:\Models\blobs\sha256-1194192cf2a187eb02722edcc3f77b11d21f537048ce04b67ccf8ba78863006a"
    )

    if not model_path.exists():
        logger.error(f"Model file not found: {model_path}")
        return

    logger.info(f"Testing GGUF FULL ingestion: {model_path.name}")
    logger.info(f"File size: {model_path.stat().st_size / 1e9:.2f} GB")

    # Get proper database connection string
    db_url = settings.get_connection_string()
    logger.info(
        f"Connecting to: {settings.pghost}:{settings.pgport}/{settings.pgdatabase}"
    )

    # Initialize connection pool
    pool = AsyncConnectionPool(
        conninfo=db_url,
        min_size=1,
        max_size=10,
    )
    await pool.open()
    set_connection_pool(pool)

    try:
        atomizer = GGUFAtomizer(threshold=1e-6)

        async with pool.connection() as conn:
            async with conn.transaction():
                result = await atomizer.atomize_model(
                    file_path=model_path,
                    model_name="qwen3-coder:30b",
                    conn=conn,
                    # Full ingestion - no max_tensors limit
                )

        logger.info("=" * 80)
        logger.info("INGESTION RESULTS:")
        logger.info(f"  Total weights processed: {result['total_processed']:,}")
        logger.info(f"  Unique atoms created: {result['atoms_created']:,}")
        logger.info(f"  Sparse weights skipped: {result['sparse_skipped']:,}")
        logger.info(f"  Deduplication ratio: {result['deduplication_ratio']:.1f}x")
        logger.info(f"  Sparse savings: {result.get('sparse_percentage', 0):.1f}%")
        logger.info("=" * 80)
    finally:
        await pool.close()


if __name__ == "__main__":
    asyncio.run(test_gguf_ingestion())
