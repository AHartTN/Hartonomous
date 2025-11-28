"""Test BaseAtomizer class."""
import pytest
import hashlib
from src.core.atomization.base_atomizer import BaseAtomizer

pytestmark = pytest.mark.asyncio

class TestBaseAtomizer:
    """Test BaseAtomizer functionality."""
    
    async def test_create_atom(self, db_connection, clean_db):
        """Test creating atom via BaseAtomizer."""
        atomizer = BaseAtomizer()
        
        test_value = b'test'
        test_metadata = {'modality': 'test', 'foo': 'bar'}
        
        atom_id = await atomizer.create_atom(
            db_connection,
            test_value,
            'test',
            test_metadata
        )
        
        assert atom_id is not None
        assert isinstance(atom_id, int)
        
        # Verify atom in database
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT atomic_value, canonical_text, metadata FROM atom WHERE atom_id = %s",
                (atom_id,)
            )
            row = await cur.fetchone()
            
            assert row[0] == test_value
            assert row[1] == 'test'
            assert row[2]['modality'] == 'test'
            assert row[2]['foo'] == 'bar'
    
    async def test_create_composition(self, db_connection, clean_db):
        """Test creating composition via BaseAtomizer."""
        atomizer = BaseAtomizer()
        
        parent_id = await atomizer.create_atom(db_connection, b'parent', 'parent', {})
        child_id = await atomizer.create_atom(db_connection, b'child', 'child', {})
        
        await atomizer.create_composition(db_connection, parent_id, child_id, 0)
        
        # Verify composition
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT component_atom_id, sequence_index FROM atom_composition WHERE parent_atom_id = %s",
                (parent_id,)
            )
            row = await cur.fetchone()
            
            assert row[0] == child_id
            assert row[1] == 0
    
    async def test_atomizer_stats(self, db_connection, clean_db):
        """Test stats tracking in atomizer."""
        atomizer = BaseAtomizer(threshold=0.01)
        
        assert atomizer.stats['total_processed'] == 0
        assert atomizer.stats['atoms_created'] == 0
        
        # Atomizer maintains stats as parsers use it
        atomizer.stats['total_processed'] = 100
        atomizer.stats['atoms_created'] = 50
        atomizer.stats['sparse_skipped'] = 30
        
        assert atomizer.stats['total_processed'] == 100
        assert atomizer.stats['atoms_created'] == 50
        assert atomizer.stats['sparse_skipped'] == 30
