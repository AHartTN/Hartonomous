"""Test SQL atomize_value function - the core atomization interface."""
import pytest
import hashlib

pytestmark = pytest.mark.asyncio

class TestAtomizeValue:
    """Test atomize_value SQL function."""
    
    async def test_atomize_simple_value(self, db_connection, clean_db):
        """Test atomizing a simple byte value."""
        test_value = b'Hello'
        expected_hash = hashlib.sha256(test_value).digest()
        
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (test_value, 'Hello', '{"modality": "text"}')
            )
            atom_id = (await cur.fetchone())[0]
            
            assert atom_id is not None
            assert isinstance(atom_id, int)
            
            # Verify atom was created
            await cur.execute(
                "SELECT content_hash, atomic_value, canonical_text FROM atom WHERE atom_id = %s",
                (atom_id,)
            )
            row = await cur.fetchone()
            
            assert row[0] == expected_hash
            assert row[1] == test_value
            assert row[2] == 'Hello'
    
    async def test_atomize_value_deduplication(self, db_connection, clean_db):
        """Test that same value returns same atom_id (deduplication)."""
        test_value = b'Duplicate'
        
        async with db_connection.cursor() as cur:
            # Insert first time
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (test_value, 'Duplicate', '{}')
            )
            atom_id_1 = (await cur.fetchone())[0]
            
            # Insert second time
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (test_value, 'Duplicate', '{}')
            )
            atom_id_2 = (await cur.fetchone())[0]
            
            assert atom_id_1 == atom_id_2, "Same value should return same atom_id"
            
            # Verify reference count incremented
            await cur.execute(
                "SELECT reference_count FROM atom WHERE atom_id = %s",
                (atom_id_1,)
            )
            ref_count = (await cur.fetchone())[0]
            assert ref_count == 2
    
    async def test_atomize_value_size_limit(self, db_connection, clean_db):
        """Test that values >64 bytes are rejected."""
        large_value = b'x' * 65
        
        with pytest.raises(Exception) as exc_info:
            async with db_connection.cursor() as cur:
                await cur.execute(
                    "SELECT atomize_value(%s::bytea, NULL, '{}'::jsonb)",
                    (large_value,)
                )
        
        assert "64-byte limit" in str(exc_info.value)
    
    async def test_atomize_numeric(self, db_connection, clean_db):
        """Test atomize_numeric function."""
        test_number = 3.14159
        
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomize_numeric(%s::numeric, %s::jsonb)",
                (test_number, '{"modality": "weight"}')
            )
            atom_id = (await cur.fetchone())[0]
            
            assert atom_id is not None
            
            # Verify numeric was stored
            await cur.execute(
                "SELECT metadata->>'value' FROM atom WHERE atom_id = %s",
                (atom_id,)
            )
            stored_value = (await cur.fetchone())[0]
            assert float(stored_value) == pytest.approx(test_number)
    
    async def test_atomize_text(self, db_connection, clean_db):
        """Test atomize_text function."""
        test_text = "Hello"
        
        async with db_connection.cursor() as cur:
            await cur.execute("SELECT atomize_text(%s)", (test_text,))
            atom_ids = (await cur.fetchone())[0]
            
            assert len(atom_ids) == 5  # H, e, l, l, o
            
            # Verify 'l' was deduplicated (appears twice but stored once)
            unique_atoms = set(atom_ids)
            assert len(unique_atoms) == 4  # H, e, l, o
