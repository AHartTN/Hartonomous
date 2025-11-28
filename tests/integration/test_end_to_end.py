"""Test end-to-end atomization workflow."""
import pytest
import numpy as np
from pathlib import Path
from PIL import Image

pytestmark = pytest.mark.asyncio

class TestEndToEndAtomization:
    """Test complete atomization workflows."""
    
    async def test_text_to_atoms_to_query(self, db_connection, clean_db, tmp_path):
        """Test: Text file ? atoms ? spatial query ? results."""
        from src.ingestion.parsers.text_parser import TextParser
        
        # Step 1: Create and atomize text
        text_file = tmp_path / "document.txt"
        text_file.write_text("The quick brown fox")
        
        parser = TextParser()
        doc_atom_id = await parser.parse(text_file, db_connection)
        
        # Step 2: Verify atomization
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (doc_atom_id,)
            )
            char_count = (await cur.fetchone())[0]
            assert char_count == 19  # "The quick brown fox" length
            
            # Step 3: Query related atoms
            await cur.execute(
                """
                SELECT component_atom_id 
                FROM atom_composition 
                WHERE parent_atom_id = %s 
                ORDER BY sequence_index
                LIMIT 3
                """,
                (doc_atom_id,)
            )
            first_chars = await cur.fetchall()
            assert len(first_chars) == 3  # T, h, e
    
    async def test_model_weights_deduplication(self, db_connection, clean_db):
        """Test: Model weights ? deduplication ? storage efficiency."""
        from api.services.model_atomization import GGUFAtomizer
        
        atomizer = GGUFAtomizer(threshold=0.01)
        
        # Simulate atomizing weights with duplicates
        weights = [0.5, 0.5, 0.3, 0.5, 0.3]  # 2 unique values
        
        atom_ids = []
        for weight in weights:
            atom_id = await atomizer._atomize_weight(db_connection, weight)
            atom_ids.append(atom_id)
        
        # Should only create 2 unique atoms
        unique_atoms = set(atom_ids)
        assert len(unique_atoms) == 2
        
        # Verify deduplication stats
        assert atomizer.stats['atoms_deduped'] > 0
    
    async def test_image_sparse_storage(self, db_connection, clean_db, tmp_path):
        """Test: Image ? sparse atomization ? efficient storage."""
        from src.ingestion.parsers.image_parser import ImageParser
        
        # Create mostly black image (sparse)
        img_array = np.zeros((10, 10, 3), dtype=np.uint8)
        img_array[5, 5] = [255, 0, 0]  # One red pixel
        
        img = Image.fromarray(img_array)
        img_path = tmp_path / "sparse.png"
        img.save(img_path)
        
        parser = ImageParser(threshold=0.1)  # High threshold for sparsity
        parent_atom_id = await parser.parse(img_path, db_connection)
        
        # Verify sparse storage
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (parent_atom_id,)
            )
            component_count = (await cur.fetchone())[0]
            
            # Should store much less than full 10x10x3 = 300 pixels
            # Due to sparsity threshold
            total_pixels = 10 * 10
            chunk_size = 4
            max_chunks = (total_pixels + chunk_size - 1) // chunk_size
            
            assert component_count < max_chunks  # Sparse encoding working
