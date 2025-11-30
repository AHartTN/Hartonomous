"""
Test tensor reconstruction - verify atomization is reversible.

Tests:
1. Create simple tensor
2. Atomize it
3. Reconstruct from atoms
4. Verify bit-perfect match
"""

import asyncio
import logging
import sys

import numpy as np

logging.basicConfig(
    level=logging.INFO, format="%(asctime)s - %(name)s - %(levelname)s - %(message)s"
)
logger = logging.getLogger(__name__)


async def test_reconstruction():
    """Test that we can reconstruct tensors from atoms."""
    from psycopg_pool import AsyncConnectionPool

    from api.config import settings
    from api.dependencies import set_connection_pool

    pool = AsyncConnectionPool(settings.get_connection_string(), min_size=1, max_size=2)
    await pool.open()
    set_connection_pool(pool)

    try:
        logger.info(f"\n{'='*80}")
        logger.info("TENSOR RECONSTRUCTION TEST")
        logger.info(f"{'='*80}\n")

        # Clean test data
        logger.info("Cleaning previous test data...")
        async with pool.connection() as conn:
            async with conn.cursor() as cur:
                await cur.execute(
                    "DELETE FROM atom WHERE canonical_text LIKE '0.%' OR canonical_text = 'test.weight'"
                )
        logger.info("✓ Clean complete\n")

        # Create simple test tensor
        test_tensor = np.array(
            [[0.1, 0.0, 0.3], [0.0, 0.5, 0.0], [0.7, 0.0, 0.9]], dtype=np.float32
        )

        logger.info(f"Original tensor:")
        logger.info(f"  Shape: {test_tensor.shape}")
        logger.info(f"  Non-zero: {np.count_nonzero(test_tensor)}")
        logger.info(f"  Values:\n{test_tensor}")

        # Atomize tensor
        atomizer = TensorAtomizer()
        async with pool.connection() as conn:
            tensor_atom_id, stats = await atomizer.atomize_tensor(
                conn=conn,
                pool=pool,
                tensor_name="test.weight",
                tensor_data=test_tensor,
                model_name="test_model",
            )

        logger.info(f"\n✓ Tensor atomized")
        logger.info(f"  Tensor atom ID: {tensor_atom_id}")
        logger.info(f"  Total weights: {stats['total_weights']}")
        logger.info(f"  Unique weights: {stats['unique_weights']}")
        logger.info(f"  Dedup ratio: {stats['dedup_ratio']:.1f}x")

        # Reconstruct tensor
        reconstructor = TensorReconstructor()
        async with pool.connection() as conn:
            reconstructed, metadata = await reconstructor.reconstruct_tensor(
                conn=conn, tensor_atom_id=tensor_atom_id
            )

        logger.info(f"\n✓ Tensor reconstructed")
        logger.info(f"  Shape: {reconstructed.shape}")
        logger.info(f"  Non-zero: {np.count_nonzero(reconstructed)}")
        logger.info(f"  Values:\n{reconstructed}")

        # Verify match
        async with pool.connection() as conn:
            match = await reconstructor.verify_reconstruction(
                conn=conn, tensor_atom_id=tensor_atom_id, original_tensor=test_tensor
            )

        if match:
            logger.info(f"\n{'='*80}")
            logger.info("✅ RECONSTRUCTION TEST PASSED - BIT-PERFECT MATCH")
            logger.info(f"{'='*80}\n")
            return True
        else:
            logger.error(f"\n{'='*80}")
            logger.error("❌ RECONSTRUCTION TEST FAILED - MISMATCH")
            logger.error(f"{'='*80}\n")
            return False

    except Exception as e:
        logger.exception(f"❌ Test failed: {e}")
        return False

    finally:
        await pool.close()


if __name__ == "__main__":
    if sys.platform == "win32":
        asyncio.set_event_loop_policy(asyncio.WindowsSelectorEventLoopPolicy())

    success = asyncio.run(test_reconstruction())
    sys.exit(0 if success else 1)
