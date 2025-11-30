"""Test SQL spatial functions."""

import pytest

pytestmark = pytest.mark.asyncio


@pytest.mark.sql
@pytest.mark.spatial
class TestSpatialFunctions:
    """Test PostGIS spatial query functions."""

    async def test_compute_attention(self, db_connection, clean_db):
        """Test compute_attention SQL function."""
        # Create query atom
        async with db_connection.cursor() as cur:
            await cur.execute(
                """
                SELECT atomize_value(
                    %s::bytea, 
                    %s, 
                    %s::jsonb
                )
                """,
                (b"query", "query", '{"modality": "text"}'),
            )
            query_atom_id = (await cur.fetchone())[0]

            # Add spatial position
            await cur.execute(
                """
                UPDATE atom 
                SET spatial_key = ST_MakePoint(0.5, 0.5, 0.5)
                WHERE atom_id = %s
                """,
                (query_atom_id,),
            )

            # Create context atoms
            context_ids = []
            for i in range(5):
                await cur.execute(
                    "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                    (f"ctx{i}".encode(), f"ctx{i}", "{}"),
                )
                ctx_id = (await cur.fetchone())[0]
                context_ids.append(ctx_id)

                # Add spatial positions (varying distances)
                await cur.execute(
                    """
                    UPDATE atom 
                    SET spatial_key = ST_MakePoint(%s, %s, %s)
                    WHERE atom_id = %s
                    """,
                    (0.5 + i * 0.1, 0.5, 0.5, ctx_id),
                )

            # Test compute_attention
            await cur.execute(
                "SELECT * FROM compute_attention(%s, %s, 3)",
                (query_atom_id, context_ids),
            )

            results = await cur.fetchall()

            assert len(results) <= 3  # Top-K = 3

            # Results should be ordered by attention weight
            if len(results) > 1:
                for i in range(len(results) - 1):
                    assert results[i][1] >= results[i + 1][1]  # Descending weights
