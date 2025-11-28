"""Test GPU acceleration functions."""

import pytest

pytestmark = pytest.mark.asyncio


class TestGPUFunctions:
    """Test GPU acceleration SQL functions."""

    async def test_gpu_available(self, db_connection):
        """Test gpu_available SQL function."""
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT gpu_available()")
            result = (await cur.fetchone())[0]

            # Should return boolean
            assert isinstance(result, bool)

    async def test_gpu_info(self, db_connection):
        """Test gpu_info SQL function."""
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT gpu_info()")
            result = (await cur.fetchone())[0]

            # Should return string
            assert isinstance(result, str)
            assert len(result) > 0

    @pytest.mark.skipif(True, reason="Requires GPU")
    async def test_compute_attention_gpu(self, db_connection, clean_db):
        """Test GPU-accelerated attention (if GPU available)."""
        async with db_connection.cursor() as cur:
            # Check if GPU available
            await cur.execute("SELECT gpu_available()")
            has_gpu = (await cur.fetchone())[0]

            if not has_gpu:
                pytest.skip("No GPU available")

            # Create test atoms
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b"query", "query", "{}"),
            )
            query_id = (await cur.fetchone())[0]

            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b"context", "context", "{}"),
            )
            ctx_id = (await cur.fetchone())[0]

            # Test GPU attention
            await cur.execute(
                "SELECT * FROM compute_attention_gpu(%s, ARRAY[%s]::bigint[], 1)",
                (query_id, ctx_id),
            )
            result = await cur.fetchone()

            assert result is not None
