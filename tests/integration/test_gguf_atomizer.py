"""Test GGUFAtomizer."""
import pytest
from pathlib import Path
from api.services.model_atomization import GGUFAtomizer

pytestmark = pytest.mark.asyncio

class TestGGUFAtomizer:
    """Test GGUF model atomization."""
    
    async def test_atomizer_initialization(self):
        """Test atomizer can be initialized."""
        atomizer = GGUFAtomizer(threshold=0.01)
        
        assert atomizer.threshold == 0.01
        assert len(atomizer.cache) == 0
        assert atomizer.stats['total_processed'] == 0
    
    async def test_sample_weight_generation(self):
        """Test sample weight generation."""
        atomizer = GGUFAtomizer()
        
        weights = atomizer._generate_sample_weights(1000)
        
        assert len(weights) == 1000
        
        # Should have sparse (zeros), repeated, and unique values
        zeros = sum(1 for w in weights if w == 0.0)
        assert zeros > 600  # At least 60% sparse
    
    async def test_atomize_sample_model(self, db_connection, clean_db, tmp_path):
        """Test atomizing sample model data."""
        atomizer = GGUFAtomizer(threshold=0.1)
        
        # Create fake model file
        fake_model = tmp_path / "test.gguf"
        fake_model.write_text("fake model")
        
        result = await atomizer.atomize_model(
            file_path=fake_model,
            model_name="test_model",
            conn=db_connection,
            max_tensors=2
        )
        
        assert result['model_name'] == 'test_model'
        assert result['layers_processed'] == 2
        assert result['unique_atoms'] > 0
        assert result['deduplication_ratio'] >= 1.0
    
    async def test_weight_deduplication(self, db_connection, clean_db):
        """Test that identical weights are deduplicated."""
        atomizer = GGUFAtomizer()
        
        # Atomize same weight twice
        weight_id_1 = await atomizer._atomize_weight(db_connection, 0.5)
        weight_id_2 = await atomizer._atomize_weight(db_connection, 0.5)
        
        assert weight_id_1 == weight_id_2  # Should be same atom
        assert atomizer.stats['atoms_deduped'] == 1
