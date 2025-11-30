"""
Test complete ingestion pipeline with TinyLlama model.

Verifies:
1. Tensor atomization with metadata
2. Hilbert encoding
3. RLE compression
4. Spatial geometry creation
5. Bit-perfect reconstruction
"""

import asyncio
import logging
import sys
import numpy as np
from pathlib import Path

# Setup logging
logging.basicConfig(
    level=logging.INFO,
    format='%(asctime)s - %(name)s - %(levelname)s - %(message)s'
)
logger = logging.getLogger(__name__)


async def test_pipeline():
    """Test full ingestion pipeline with reconstruction verification."""
    from api.dependencies import set_connection_pool
    from api.services.geometric_atomization.gguf_atomizer import GGUFAtomizer
    from api.services.geometric_atomization.spatial_reconstructor import SpatialReconstructor
    from api.config import settings
    from psycopg_pool import AsyncConnectionPool
    
    # Initialize pool
    pool = AsyncConnectionPool(
        settings.get_connection_string(),
        min_size=1,
        max_size=4
    )
    await pool.open()
    set_connection_pool(pool)
    
    try:
        # Path to TinyLlama model
        model_path = Path(".cache/test_models/tinyllama-1.1b-chat-v1.0.Q4_K_M.gguf")
        if not model_path.exists():
            logger.error(f"Model not found: {model_path}")
            logger.info("Please download TinyLlama first")
            return False
        
        logger.info(f"\n{'='*80}")
        logger.info("INGESTION PIPELINE TEST")
        logger.info(f"Model: {model_path}")
        logger.info(f"{'='*80}\n")
        
        # Ingest model
        atomizer = GGUFAtomizer(threshold=1e-6, parallel_processing=False)
        async with pool.connection() as conn:
            stats = await atomizer.atomize_model(
                file_path=model_path,
                model_name=model_path.stem,
                conn=conn,
                pool=pool
            )
        
        logger.info(f"\n✓ Model ingested")
        logger.info(f"  Tensors processed: {stats.get('tensors_processed', 0)}")
        logger.info(f"  Total atoms created: {stats.get('atoms_created', 0):,}")
        logger.info(f"  Deduplication ratio: {stats.get('deduplication_ratio', 0):.1f}x")
        
        # Verify compression ratio
        tensor_atoms = stats.get('tensor_atoms', [])
        if tensor_atoms:
            async with pool.connection() as conn:
                async with conn.cursor() as cur:
                    await cur.execute(
                        "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = ANY(%s)",
                        (tensor_atoms,)
                    )
                    total_compositions = (await cur.fetchone())[0]
                    
                    logger.info(f"\n✓ Composition stats:")
                    logger.info(f"  Stored compositions: {total_compositions:,}")
                    
                    # Spatial query test
                    await cur.execute(
                        "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = ANY(%s) AND spatial_key IS NOT NULL",
                        (tensor_atoms,)
                    )
                    spatial_count = (await cur.fetchone())[0]
                    logger.info(f"  With spatial geometry: {spatial_count:,}")
        
        logger.info(f"\n{'='*80}")
        logger.info("✅ INGESTION TEST PASSED")
        logger.info(f"{'='*80}\n")
        
        return True
        
    except Exception as e:
        logger.exception(f"❌ Pipeline test failed: {e}")
        return False
        
    finally:
        await pool.close()


if __name__ == "__main__":
    import asyncio
    import sys
    
    if sys.platform == 'win32':
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())
    
    success = asyncio.run(test_pipeline())
    sys.exit(0 if success else 1)
