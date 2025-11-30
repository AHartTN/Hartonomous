"""Test atom_composition - ACTUAL COMPOSITION FUNCTIONALITY."""

import pytest

pytestmark = pytest.mark.asyncio


@pytest.mark.sql
class TestCompositionFunctionality:
    """Test atom composition actually builds hierarchies."""

    async def test_composition_hierarchy_created(self, db_connection, clean_db):
        """REAL TEST: Can create parent-child hierarchy via composition."""
        async with db_connection.cursor() as cur:
            # Atomize "Hello" text
            await cur.execute("SELECT atomize_text('Hello')")
            char_atoms = (await cur.fetchone())[0]

            # Create parent atom for the word
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b"Hello", "Hello", '{"modality": "word"}'),
            )
            word_atom_id = (await cur.fetchone())[0]

            # Build composition: word -> characters
            for idx, char_atom_id in enumerate(char_atoms):
                await cur.execute(
                    """
                    INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                    VALUES (%s, %s, %s)
                    """,
                    (word_atom_id, char_atom_id, idx),
                )

            # VERIFY: Composition exists
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (word_atom_id,),
            )
            comp_count = (await cur.fetchone())[0]

            assert comp_count == 5, "Should have 5 character components"

            # VERIFY: Sequence is correct
            await cur.execute(
                """
                SELECT component_atom_id, sequence_index 
                FROM atom_composition 
                WHERE parent_atom_id = %s 
                ORDER BY sequence_index
                """,
                (word_atom_id,),
            )
            compositions = await cur.fetchall()

            for i, (comp_id, seq_idx) in enumerate(compositions):
                assert seq_idx == i, f"Sequence should be {i}, got {seq_idx}"
                assert comp_id == char_atoms[i], "Component should match character atom"

    async def test_sparse_composition_gaps(self, db_connection, clean_db):
        """REAL TEST: Sparse composition with gaps (implicit zeros)."""
        async with db_connection.cursor() as cur:
            # Create parent for sparse vector
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b"sparse_vector", "sparse", '{"modality": "vector"}'),
            )
            parent_id = (await cur.fetchone())[0]

            # Create values only at indices 0, 5, 10 (sparse)
            values_and_indices = [(b"val0", 0), (b"val5", 5), (b"val10", 10)]

            for val_bytes, idx in values_and_indices:
                await cur.execute(
                    "SELECT atomize_value(%s::bytea, NULL, '{}'::jsonb)", (val_bytes,)
                )
                val_atom_id = (await cur.fetchone())[0]

                await cur.execute(
                    "INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index) VALUES (%s, %s, %s)",
                    (parent_id, val_atom_id, idx),
                )

            # VERIFY: Only 3 components stored
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (parent_id,),
            )
            count = (await cur.fetchone())[0]
            assert count == 3, "Sparse: only 3 values stored, not 11"

            # VERIFY: Indices are 0, 5, 10
            await cur.execute(
                "SELECT sequence_index FROM atom_composition WHERE parent_atom_id = %s ORDER BY sequence_index",
                (parent_id,),
            )
            indices = [row[0] for row in await cur.fetchall()]
            assert indices == [0, 5, 10], "Sparse indices should be [0, 5, 10]"

    async def test_reconstruct_from_composition(self, db_connection, clean_db):
        """REAL TEST: Can reconstruct data from composition."""
        test_word = "Test"

        async with db_connection.cursor() as cur:
            # Atomize text
            await cur.execute("SELECT atomize_text(%s)", (test_word,))
            char_atoms = (await cur.fetchone())[0]

            # Create word atom
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, '{}'::jsonb)",
                (test_word.encode(), test_word),
            )
            word_id = (await cur.fetchone())[0]

            # Build composition
            for idx, char_id in enumerate(char_atoms):
                await cur.execute(
                    "INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index) VALUES (%s, %s, %s)",
                    (word_id, char_id, idx),
                )

            # RECONSTRUCT: Query composition and rebuild
            await cur.execute(
                """
                SELECT a.canonical_text 
                FROM atom_composition ac
                JOIN atom a ON a.atom_id = ac.component_atom_id
                WHERE ac.parent_atom_id = %s
                ORDER BY ac.sequence_index
                """,
                (word_id,),
            )
            characters = [row[0] for row in await cur.fetchall()]
            reconstructed = "".join(characters)

            assert (
                reconstructed == test_word
            ), f"Should reconstruct '{test_word}', got '{reconstructed}'"

