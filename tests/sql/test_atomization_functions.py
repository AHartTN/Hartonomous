"""Test all SQL atomization functions."""

import pytest

pytestmark = pytest.mark.asyncio


class TestAtomizationFunctions:
    """Test all atomize_* SQL functions."""

    async def test_atomize_image(self, db_connection, clean_db):
        """Test atomize_image SQL function."""
        # Create sample image bytea
        import numpy as np

        img_data = np.random.rand(10, 10, 3).astype(np.float32).tobytes()

        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_image(%s::bytea, 10, 10, %s::jsonb)",
                (img_data, '{"format": "rgb"}'),
            )
            result = await cur.fetchone()
            assert result is not None

    async def test_atomize_audio_sample(self, db_connection, clean_db):
        """Test atomize_audio_sample SQL function."""
        import struct

        # Create audio sample (float32)
        sample = struct.pack("f", 0.5)

        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_audio_sample(%s::bytea, %s::jsonb)",
                (sample, '{"sample_rate": 44100}'),
            )
            atom_id = (await cur.fetchone())[0]
            assert atom_id is not None

    async def test_atomize_pixel(self, db_connection, clean_db):
        """Test atomize_pixel SQL function."""
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_pixel(128, 64, 255, %s::jsonb)",
                ('{"position": [10, 20]}',),
            )
            atom_id = (await cur.fetchone())[0]
            assert atom_id is not None

    async def test_atomize_with_spatial_key(self, db_connection, clean_db):
        """Test atomize_with_spatial_key SQL function."""
        async with db_connection.cursor() as cur:
            await cur.execute(
                """
                SELECT atomize_with_spatial_key(
                    %s::bytea,
                    %s,
                    ST_MakePoint(0.5, 0.5, 0.5),
                    %s::jsonb
                )
                """,
                (b"test", "test", "{}"),
            )
            atom_id = (await cur.fetchone())[0]

            # Verify spatial_key was set
            await cur.execute(
                "SELECT ST_X(spatial_key), ST_Y(spatial_key), ST_Z(spatial_key) FROM atom WHERE atom_id = %s",
                (atom_id,),
            )
            coords = await cur.fetchone()

            assert coords[0] == pytest.approx(0.5)
            assert coords[1] == pytest.approx(0.5)
            assert coords[2] == pytest.approx(0.5)
