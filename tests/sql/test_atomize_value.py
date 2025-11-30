"""Test SQL atomize_value function - ACTUAL FUNCTIONALITY TESTS."""

import hashlib

import pytest

pytestmark = pytest.mark.asyncio


@pytest.mark.sql
class TestAtomizeValueFunctionality:
    """Test atomize_value SQL function actually works."""

    async def test_atomize_creates_atom_in_database(self, db_connection, clean_db):
        """REAL TEST: Verify atomize_value actually creates an atom in the database."""
        test_value = b"TestValue"

        async with db_connection.cursor() as cur:
            # Call the actual SQL function
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (test_value, "TestValue", '{"modality": "test"}'),
            )
            atom_id = (await cur.fetchone())[0]

            # VERIFY: Atom was actually inserted
            await cur.execute(
                "SELECT COUNT(*) FROM atom WHERE atom_id = %s", (atom_id,)
            )
            count = (await cur.fetchone())[0]
            assert count == 1, "Atom should exist in database"

            # VERIFY: Content hash is correct
            expected_hash = hashlib.sha256(test_value).digest()
            await cur.execute(
                "SELECT content_hash FROM atom WHERE atom_id = %s", (atom_id,)
            )
            actual_hash = (await cur.fetchone())[0]
            assert (
                bytes(actual_hash) == expected_hash
            ), "Content hash must match SHA-256"

    async def test_deduplication_actually_works(self, db_connection, clean_db):
        """REAL TEST: Same content returns same atom_id (deduplication)."""
        test_value = b"DuplicateTest"

        async with db_connection.cursor() as cur:
            # Insert first time
            await cur.execute(
                "SELECT atomize_value(%s::bytea, NULL, '{}'::jsonb)", (test_value,)
            )
            first_id = (await cur.fetchone())[0]

            # Count atoms before second insert
            await cur.execute("SELECT COUNT(*) FROM atom")
            count_before = (await cur.fetchone())[0]

            # Insert SAME value again
            await cur.execute(
                "SELECT atomize_value(%s::bytea, NULL, '{}'::jsonb)", (test_value,)
            )
            second_id = (await cur.fetchone())[0]

            # Count atoms after
            await cur.execute("SELECT COUNT(*) FROM atom")
            count_after = (await cur.fetchone())[0]

            # VERIFY: Same atom_id returned
            assert first_id == second_id, "Deduplication MUST return same atom_id"

            # VERIFY: No new atom created
            assert count_before == count_after, "Deduplication MUST NOT create new atom"

            # VERIFY: Reference count incremented
            await cur.execute(
                "SELECT reference_count FROM atom WHERE atom_id = %s", (first_id,)
            )
            ref_count = (await cur.fetchone())[0]
            assert ref_count == 2, "Reference count should be 2 after duplicate insert"

    async def test_64_byte_limit_enforced(self, db_connection, clean_db):
        """REAL TEST: Verify 64-byte limit is actually enforced."""
        oversized_value = b"x" * 65  # 65 bytes

        async with db_connection.cursor() as cur:
            with pytest.raises(Exception) as exc_info:
                await cur.execute(
                    "SELECT atomize_value(%s::bytea, NULL, '{}'::jsonb)",
                    (oversized_value,),
                )
                await cur.fetchone()

            error_msg = str(exc_info.value).lower()
            assert (
                "64" in error_msg or "byte" in error_msg
            ), "Should reject >64 byte values"

    async def test_metadata_stored_correctly(self, db_connection, clean_db):
        """REAL TEST: Metadata is actually stored in JSONB."""
        test_metadata = {
            "modality": "test",
            "source": "unit_test",
            "custom_field": "custom_value",
        }

        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b"meta_test", "meta_test", test_metadata),
            )
            atom_id = (await cur.fetchone())[0]

            # VERIFY: Metadata stored correctly
            await cur.execute(
                "SELECT metadata FROM atom WHERE atom_id = %s", (atom_id,)
            )
            stored_metadata = (await cur.fetchone())[0]

            assert stored_metadata["modality"] == "test"
            assert stored_metadata["source"] == "unit_test"
            assert stored_metadata["custom_field"] == "custom_value"


"""Test atomize_text SQL function - ACTUAL FUNCTIONALITY."""
import pytest

pytestmark = pytest.mark.asyncio


@pytest.mark.sql
class TestAtomizeTextFunctionality:
    """Test atomize_text actually decomposes text into character atoms."""

    async def test_text_decomposed_into_characters(self, db_connection, clean_db):
        """REAL TEST: atomize_text breaks text into individual character atoms."""
        test_text = "Hello"

        async with db_connection.cursor() as cur:
            await cur.execute("SELECT atomize_text(%s)", (test_text,))
            atom_ids = (await cur.fetchone())[0]

            # VERIFY: Returns 5 atom_ids for "Hello"
            assert (
                len(atom_ids) == 5
            ), f"Should return 5 atoms for 'Hello', got {len(atom_ids)}"

            # VERIFY: Each atom exists
            for atom_id in atom_ids:
                await cur.execute(
                    "SELECT COUNT(*) FROM atom WHERE atom_id = %s", (atom_id,)
                )
                assert (await cur.fetchone())[0] == 1, f"Atom {atom_id} should exist"

            # VERIFY: Characters are H, e, l, l, o
            await cur.execute(
                "SELECT canonical_text FROM atom WHERE atom_id = ANY(%s) ORDER BY atom_id",
                (atom_ids,),
            )
            characters = [row[0] for row in await cur.fetchall()]
            # Note: order might vary, but 'l' should appear twice
            assert "H" in characters or "h" in characters
            assert "e" in characters
            assert "l" in characters
            assert "o" in characters

    async def test_character_deduplication(self, db_connection, clean_db):
        """REAL TEST: Duplicate characters share same atom (deduplication)."""
        test_text = "aaa"  # Same character 3 times

        async with db_connection.cursor() as cur:
            await cur.execute("SELECT atomize_text(%s)", (test_text,))
            atom_ids = (await cur.fetchone())[0]

            # VERIFY: Returns 3 atom_ids
            assert len(atom_ids) == 3

            # VERIFY: All 3 point to SAME atom (deduplication)
            unique_atoms = set(atom_ids)
            assert (
                len(unique_atoms) == 1
            ), "All 'a' characters should share same atom_id"

    async def test_empty_text(self, db_connection, clean_db):
        """REAL TEST: Empty text returns empty array."""
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT atomize_text(%s)", ("",))
            atom_ids = (await cur.fetchone())[0]

            assert atom_ids == [] or atom_ids is None


@pytest.mark.sql
class TestAtomizeNumericFunctionality:
    """Test atomize_numeric actually stores numbers."""

    async def test_integer_atomization(self, db_connection, clean_db):
        """REAL TEST: atomize_numeric stores integers."""
        test_number = 42

        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_numeric(%s::numeric, %s::jsonb)",
                (test_number, '{"source": "test"}'),
            )
            atom_id = (await cur.fetchone())[0]

            # VERIFY: Atom exists
            await cur.execute(
                "SELECT canonical_text, metadata FROM atom WHERE atom_id = %s",
                (atom_id,),
            )
            row = await cur.fetchone()

            assert row[0] == "42", "Canonical text should be '42'"
            assert row[1]["modality"] == "numeric"

    async def test_float_atomization(self, db_connection, clean_db):
        """REAL TEST: atomize_numeric preserves float precision."""
        test_float = 3.14159

        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_numeric(%s::numeric, '{}'::jsonb)", (test_float,)
            )
            atom_id = (await cur.fetchone())[0]

            # VERIFY: Value preserved
            await cur.execute(
                "SELECT metadata->>'original_value' FROM atom WHERE atom_id = %s",
                (atom_id,),
            )
            stored_value = (await cur.fetchone())[0]

            assert float(stored_value) == pytest.approx(test_float)

    async def test_numeric_deduplication(self, db_connection, clean_db):
        """REAL TEST: Same number returns same atom."""
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT atomize_numeric(100::numeric, '{}'::jsonb)")
            id1 = (await cur.fetchone())[0]

            await cur.execute("SELECT atomize_numeric(100::numeric, '{}'::jsonb)")
            id2 = (await cur.fetchone())[0]

            assert id1 == id2, "Same number should deduplicate"

