"""Test Hilbert curve SQL functions."""

import pytest

pytestmark = pytest.mark.asyncio


class TestHilbertSQL:
    """Test Hilbert curve SQL implementation."""

    async def test_hilbert_encode_3d(self, db_connection):
        """Test hilbert_encode_3d SQL function."""
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT hilbert_encode_3d(0.5, 0.3, 0.8, 21)")
            result = (await cur.fetchone())[0]

            assert result is not None
            assert result > 0

    async def test_hilbert_decode_3d(self, db_connection):
        """Test hilbert_decode_3d SQL function."""
        index = 1852276087621110197

        async with db_connection.cursor() as cur:
            await cur.execute("SELECT * FROM hilbert_decode_3d(%s, 21)", (index,))
            x, y, z = await cur.fetchone()

            assert 0 <= x <= 1
            assert 0 <= y <= 1
            assert 0 <= z <= 1

    async def test_hilbert_roundtrip_sql(self, db_connection):
        """Test encode/decode roundtrip in SQL."""
        async with db_connection.cursor() as cur:
            await cur.execute(
                """
                WITH encoded AS (
                    SELECT hilbert_encode_3d(0.5, 0.3, 0.8, 15) as idx
                ),
                decoded AS (
                    SELECT * FROM hilbert_decode_3d((SELECT idx FROM encoded), 15)
                )
                SELECT x, y, z FROM decoded
                """
            )
            x, y, z = await cur.fetchone()

            assert abs(x - 0.5) < 0.001
            assert abs(y - 0.3) < 0.001
            assert abs(z - 0.8) < 0.001
