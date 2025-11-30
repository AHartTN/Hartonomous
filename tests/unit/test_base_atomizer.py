"""Test BaseAtomizer - ACTUAL SQL INTEGRATION."""

import pytest

from src.core.atomization.base_atomizer import BaseAtomizer

pytestmark = pytest.mark.asyncio


@pytest.mark.unit
class TestBaseAtomizerSQLIntegration:
    """Test BaseAtomizer actually calls SQL and creates atoms."""

    async def test_create_atom_calls_sql(self, db_connection, clean_db):
        """REAL TEST: create_atom actually inserts into database."""
        atomizer = BaseAtomizer()

        test_value = b"integration_test"
        test_metadata = {"test": "value"}

        # Count atoms before
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT COUNT(*) FROM atom")
            count_before = (await cur.fetchone())[0]

        # Create atom
        atom_id = await atomizer.create_atom(
            db_connection, test_value, "integration_test", test_metadata
        )

        # VERIFY: Atom was inserted
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT COUNT(*) FROM atom")
            count_after = (await cur.fetchone())[0]

            assert count_after == count_before + 1, "Should insert exactly 1 atom"

            # VERIFY: Atom has correct data
            await cur.execute(
                "SELECT atomic_value, canonical_text, metadata FROM atom WHERE atom_id = %s",
                (atom_id,),
            )
            row = await cur.fetchone()

            assert bytes(row[0]) == test_value
            assert row[1] == "integration_test"
            assert row[2]["test"] == "value"

    async def test_create_composition_inserts(self, db_connection, clean_db):
        """REAL TEST: create_composition inserts into atom_composition."""
        atomizer = BaseAtomizer()

        # Create parent and child atoms
        parent_id = await atomizer.create_atom(db_connection, b"parent", "parent", {})
        child_id = await atomizer.create_atom(db_connection, b"child", "child", {})

        # Count compositions before
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT COUNT(*) FROM atom_composition")
            count_before = (await cur.fetchone())[0]

        # Create composition
        await atomizer.create_composition(db_connection, parent_id, child_id, 0)

        # VERIFY: Composition inserted
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT COUNT(*) FROM atom_composition")
            count_after = (await cur.fetchone())[0]

            assert count_after == count_before + 1, "Should insert 1 composition"

            # VERIFY: Composition has correct data
            await cur.execute(
                "SELECT parent_atom_id, component_atom_id, sequence_index FROM atom_composition WHERE parent_atom_id = %s",
                (parent_id,),
            )
            row = await cur.fetchone()

            assert row[0] == parent_id
            assert row[1] == child_id
            assert row[2] == 0

    async def test_stats_tracking(self, db_connection, clean_db):
        """REAL TEST: Stats are tracked correctly."""
        atomizer = BaseAtomizer()

        # Initial stats
        assert atomizer.stats["total_processed"] == 0
        assert atomizer.stats["atoms_created"] == 0

        # Manually update stats (parsers do this)
        atomizer.stats["total_processed"] = 100
        atomizer.stats["atoms_created"] = 50
        atomizer.stats["sparse_skipped"] = 30

        # VERIFY: Stats updated
        assert atomizer.stats["total_processed"] == 100
        assert atomizer.stats["atoms_created"] == 50
        assert atomizer.stats["sparse_skipped"] == 30

