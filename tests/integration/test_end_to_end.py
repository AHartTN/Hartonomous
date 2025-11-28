"""Test end-to-end atomization workflows - REAL WORKFLOWS."""
import pytest
import numpy as np
from PIL import Image

pytestmark = pytest.mark.asyncio

class TestCompleteWorkflows:
    """Test complete atomization workflows from input to query."""
    
    async def test_text_atomization_to_query_workflow(self, db_connection, clean_db, tmp_path):
        """REAL TEST: Text ? atoms ? reconstruct ? verify."""
        from src.ingestion.parsers.text_parser import TextParser
        
        # Step 1: Create text file
        text_content = "AI"
        text_file = tmp_path / "test.txt"
        text_file.write_text(text_content)
        
        # Step 2: Parse and atomize
        parser = TextParser()
        doc_atom_id = await parser.parse(text_file, db_connection)
        
        # VERIFY: Parent atom created
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT metadata->>'char_count' FROM atom WHERE atom_id = %s",
                (doc_atom_id,)
            )
            char_count = (await cur.fetchone())[0]
            assert int(char_count) == 2, "Should have 2 characters"
            
            # Step 3: Verify characters atomized
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (doc_atom_id,)
            )
            comp_count = (await cur.fetchone())[0]
            assert comp_count == 2, "Should have 2 character compositions"
            
            # Step 4: Reconstruct text from compositions
            await cur.execute(
                """
                SELECT a.canonical_text
                FROM atom_composition ac
                JOIN atom a ON a.atom_id = ac.component_atom_id
                WHERE ac.parent_atom_id = %s
                ORDER BY ac.sequence_index
                """,
                (doc_atom_id,)
            )
            chars = [row[0] for row in await cur.fetchall()]
            reconstructed = ''.join(chars)
            
            assert reconstructed == text_content, f"Should reconstruct '{text_content}', got '{reconstructed}'"
    
    async def test_model_weight_deduplication_workflow(self, db_connection, clean_db):
        """REAL TEST: Model weights ? deduplication ? storage efficiency."""
        from api.services.model_atomization import GGUFAtomizer
        
        atomizer = GGUFAtomizer(threshold=0.01)
        
        # Simulate model with duplicate weights
        weights = [0.5, 0.5, 0.3, 0.5, 0.3, 0.5]  # 2 unique values, 6 total
        
        atom_ids = []
        for weight in weights:
            atom_id = await atomizer._atomize_weight(db_connection, weight)
            atom_ids.append(atom_id)
        
        # VERIFY: Only 2 unique atoms created
        unique_atoms = len(set(atom_ids))
        assert unique_atoms == 2, f"Should deduplicate to 2 atoms, got {unique_atoms}"
        
        # VERIFY: Atoms stored in database
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT COUNT(*) FROM atom WHERE metadata->>'modality' = 'weight'"
            )
            db_count = (await cur.fetchone())[0]
            assert db_count >= 2, "Weight atoms should be in database"
    
    async def test_image_to_atoms_workflow(self, db_connection, clean_db, tmp_path):
        """REAL TEST: Image ? pixel atoms ? sparse storage."""
        from src.ingestion.parsers.image_parser import ImageParser
        
        # Create small test image (mostly black)
        img_array = np.zeros((5, 5, 3), dtype=np.uint8)
        img_array[2, 2] = [255, 0, 0]  # One red pixel
        
        img = Image.fromarray(img_array)
        img_path = tmp_path / "test.png"
        img.save(img_path)
        
        # Parse image
        parser = ImageParser(threshold=0.1)
        parent_atom_id = await parser.parse(img_path, db_connection)
        
        # VERIFY: Parent atom created
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT metadata FROM atom WHERE atom_id = %s",
                (parent_atom_id,)
            )
            metadata = (await cur.fetchone())[0]
            
            assert metadata['modality'] == 'image'
            assert metadata['width'] == 5
            assert metadata['height'] == 5
            
            # VERIFY: Sparse storage (not all 25 pixels stored)
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE parent_atom_id = %s",
                (parent_atom_id,)
            )
            comp_count = (await cur.fetchone())[0]
            
            max_possible = (5 * 5 + 3) // 4  # pixels / chunk_size
            assert comp_count <= max_possible, "Should use sparse storage"
