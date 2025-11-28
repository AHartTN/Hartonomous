"""Test atom_composition table and composition functions."""
import pytest

pytestmark = pytest.mark.asyncio

class TestAtomComposition:
    """Test atom composition functionality."""
    
    async def test_create_composition(self, db_connection, clean_db):
        """Test creating parent-child composition."""
        async with db_connection.cursor() as cur:
            # Create parent atom
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b'parent', 'parent', '{"modality": "document"}')
            )
            parent_id = (await cur.fetchone())[0]
            
            # Create child atoms
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b'child1', 'child1', '{}')
            )
            child1_id = (await cur.fetchone())[0]
            
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b'child2', 'child2', '{}')
            )
            child2_id = (await cur.fetchone())[0]
            
            # Create compositions
            await cur.execute(
                """
                INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index)
                VALUES (%s, %s, 0), (%s, %s, 1)
                """,
                (parent_id, child1_id, parent_id, child2_id)
            )
            
            # Verify compositions
            await cur.execute(
                """
                SELECT component_atom_id, sequence_index
                FROM atom_composition
                WHERE parent_atom_id = %s
                ORDER BY sequence_index
                """,
                (parent_id,)
            )
            rows = await cur.fetchall()
            
            assert len(rows) == 2
            assert rows[0][0] == child1_id
            assert rows[0][1] == 0
            assert rows[1][0] == child2_id
            assert rows[1][1] == 1
    
    async def test_sparse_composition(self, db_connection, clean_db):
        """Test sparse composition (gaps in sequence_index)."""
        async with db_connection.cursor() as cur:
            # Create parent
            await cur.execute(
                "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                (b'sparse_parent', 'sparse', '{}')
            )
            parent_id = (await cur.fetchone())[0]
            
            # Create components at indices 0, 5, 10 (sparse - gaps = zeros)
            for idx in [0, 5, 10]:
                await cur.execute(
                    "SELECT atomize_value(%s::bytea, %s, %s::jsonb)",
                    (f'val{idx}'.encode(), f'val{idx}', '{}')
                )
                component_id = (await cur.fetchone())[0]
                
                await cur.execute(
                    "INSERT INTO atom_composition (parent_atom_id, component_atom_id, sequence_index) VALUES (%s, %s, %s)",
                    (parent_id, component_id, idx)
                )
            
            # Verify sparse storage
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (parent_id,)
            )
            count = (await cur.fetchone())[0]
            assert count == 3  # Only 3 stored, not 11
            
            # Verify indices
            await cur.execute(
                "SELECT sequence_index FROM atom_composition WHERE parent_atom_id = %s ORDER BY sequence_index",
                (parent_id,)
            )
            indices = [row[0] for row in await cur.fetchall()]
            assert indices == [0, 5, 10]
