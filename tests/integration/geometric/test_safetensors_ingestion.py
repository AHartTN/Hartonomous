"""
Integration tests for SafeTensors model ingestion using GeometricAtomizer.

Tests the complete pipeline:
1. Load all-MiniLM-L6-v2 SafeTensors model from .cache
2. Extract embedding tensors using safetensors library
3. Atomize embeddings → LINESTRING trajectories  
4. Store in database
5. Reconstruct and verify accuracy

Uses greenfield GeometricAtomizer architecture.
"""

import pytest
import numpy as np
from pathlib import Path
from typing import Dict
import json

from api.services.geometric_atomization import GeometricAtomizer

pytestmark = [pytest.mark.asyncio, pytest.mark.integration]


def load_safetensors(file_path: Path) -> Dict[str, np.ndarray]:
    """
    Load SafeTensors file and return tensors dict.
    
    Args:
        file_path: Path to .safetensors file
    
    Returns:
        Dict mapping tensor names to numpy arrays
    """
    try:
        from safetensors import safe_open
    except ImportError:
        pytest.skip("safetensors library not installed")
    
    tensors = {}
    with safe_open(file_path, framework="numpy") as f:
        for key in f.keys():
            tensors[key] = f.get_tensor(key)
    
    return tensors


class TestSafeTensorsLoading:
    """Test SafeTensors file loading."""
    
    async def test_load_safetensors_file(self, test_safetensors_path):
        """Test loading SafeTensors file."""
        if not test_safetensors_path.exists():
            pytest.skip(f"SafeTensors model not found: {test_safetensors_path}")
        
        tensors = load_safetensors(test_safetensors_path)
        
        # Verify tensors loaded
        assert len(tensors) > 0, "Should have tensors"
        
        # MiniLM typical layers
        expected_patterns = ['embeddings', 'encoder', 'pooler']
        found_patterns = [p for p in expected_patterns if any(p in k for k in tensors.keys())]
        
        assert len(found_patterns) > 0, f"Should have typical embedding layers, found: {list(tensors.keys())[:5]}"
        
        print(f"\n✓ Loaded SafeTensors:")
        print(f"  Total tensors: {len(tensors)}")
        print(f"  Sample keys: {list(tensors.keys())[:5]}")
        
        # Print size info
        total_params = sum(t.size for t in tensors.values())
        print(f"  Total parameters: {total_params:,}")
    
    async def test_tensor_shapes(self, test_safetensors_path):
        """Test tensor shapes are reasonable."""
        if not test_safetensors_path.exists():
            pytest.skip(f"SafeTensors model not found: {test_safetensors_path}")
        
        tensors = load_safetensors(test_safetensors_path)
        
        # Verify all tensors have shapes
        for name, tensor in tensors.items():
            assert hasattr(tensor, 'shape'), f"Tensor {name} should have shape"
            assert len(tensor.shape) > 0, f"Tensor {name} should have dimensions"
            assert tensor.size > 0, f"Tensor {name} should have values"
        
        print(f"\n✓ All {len(tensors)} tensors have valid shapes")


class TestEmbeddingAtomization:
    """Test atomizing embedding tensors."""
    
    async def test_atomize_small_embedding(self):
        """Test atomizing a small embedding matrix."""
        atomizer = GeometricAtomizer()
        
        # Small embedding: 100 tokens × 384 dims (typical for MiniLM)
        embedding = np.random.randn(100, 384).astype(np.float32)
        
        # Atomize
        wkts = atomizer.atomize_tensor(embedding, chunk_size=50000)
        
        # Verify
        # 100 × 384 = 38,400 values < 50K chunk size → single trajectory
        assert len(wkts) == 1, "Small embedding should create 1 trajectory"
        assert wkts[0].startswith("LINESTRING ZM"), "Should be LINESTRING ZM"
        
        print(f"\n✓ Atomized 100×384 embedding → 1 trajectory")
        print(f"  Total values: {embedding.size:,}")
        print(f"  WKT size: {len(wkts[0]):,} chars")
    
    async def test_atomize_large_embedding_chunked(self):
        """Test chunking large embedding matrices."""
        atomizer = GeometricAtomizer()
        
        # Large embedding: 30K tokens × 768 dims = 23M values
        embedding = np.random.randn(30000, 768).astype(np.float32)
        
        # Atomize with chunking
        wkts = atomizer.atomize_tensor(embedding, chunk_size=10000)
        
        # Verify chunking
        total_values = embedding.size
        expected_chunks = (total_values + 9999) // 10000  # Ceiling division
        
        assert len(wkts) == expected_chunks, f"Expected {expected_chunks} chunks"
        
        print(f"\n✓ Chunked large embedding:")
        print(f"  Shape: {embedding.shape}")
        print(f"  Total values: {total_values:,}")
        print(f"  Chunks: {len(wkts)}")


@pytest.mark.slow
class TestDatabaseStorage:
    """Test storing embedding tensors in database."""
    
    async def test_store_single_embedding(self, db_connection, clean_db):
        """Test storing an embedding layer."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Small embedding
        embedding = np.random.randn(100, 256).astype(np.float32)
        wkts = atomizer.atomize_tensor(embedding, chunk_size=50000)
        
        # Store
        comp_id = await atomizer.store_trajectory(
            name="embeddings.word_embeddings.weight",
            wkt=wkts[0],
            metadata={
                'shape': list(embedding.shape),
                'dtype': str(embedding.dtype),
                'layer_type': 'embedding'
            }
        )
        
        # Verify
        assert comp_id > 0, "Should return valid ID"
        
        # Query back
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT name, metadata FROM atom_composition WHERE id = %s",
                (comp_id,)
            )
            row = await cur.fetchone()
        
        assert row is not None
        assert row[0] == "embeddings.word_embeddings.weight"
        
        metadata = json.loads(row[1])
        assert metadata['shape'] == list(embedding.shape)
        assert metadata['layer_type'] == 'embedding'
        
        print(f"\n✓ Stored embedding layer → composition {comp_id}")


@pytest.mark.slow
class TestFullModelIngestion:
    """Test ingesting complete SafeTensors model."""
    
    async def test_ingest_minilm_mock(self, db_connection, clean_db):
        """Test ingesting mock MiniLM-like model."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Mock MiniLM layers (small versions)
        mock_state_dict = {
            'embeddings.word_embeddings.weight': np.random.randn(100, 256).astype(np.float32),
            'embeddings.position_embeddings.weight': np.random.randn(512, 256).astype(np.float32),
            'encoder.layer.0.attention.self.query.weight': np.random.randn(256, 256).astype(np.float32),
            'encoder.layer.0.attention.self.key.weight': np.random.randn(256, 256).astype(np.float32),
            'pooler.dense.weight': np.random.randn(256, 256).astype(np.float32)
        }
        
        # Ingest
        comp_ids = await atomizer.ingest_model(
            model_state_dict=mock_state_dict,
            model_name="test-minilm-mock",
            chunk_size=10000
        )
        
        # Verify
        assert len(comp_ids) == len(mock_state_dict)
        
        # Query
        async with db_connection.cursor() as cur:
            await cur.execute(
                "SELECT COUNT(*) FROM atom_composition WHERE metadata->>'model' = %s",
                ("test-minilm-mock",)
            )
            count = (await cur.fetchone())[0]
        
        assert count == len(mock_state_dict)
        
        print(f"\n✓ Ingested mock MiniLM:")
        print(f"  Layers: {len(mock_state_dict)}")
        print(f"  Compositions: {len(comp_ids)}")
    
    async def test_reconstruct_minilm_mock(self, db_connection, clean_db):
        """Test reconstructing MiniLM model."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Ingest
        original_state_dict = {
            'embeddings.weight': np.array([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]], dtype=np.float32),
            'encoder.weight': np.array([[7.0, 8.0], [9.0, 10.0]], dtype=np.float32)
        }
        
        await atomizer.ingest_model(
            model_state_dict=original_state_dict,
            model_name="test-minilm-reconstruct",
            chunk_size=100
        )
        
        # Reconstruct
        reconstructed = await atomizer.reconstruct_model("test-minilm-reconstruct")
        
        # Verify
        assert len(reconstructed) == len(original_state_dict)
        
        for layer_name, original_tensor in original_state_dict.items():
            reconstructed_tensor = reconstructed[layer_name]
            
            # Bit-perfect match
            np.testing.assert_array_equal(
                reconstructed_tensor,
                original_tensor,
                err_msg=f"Layer {layer_name} should match exactly"
            )
        
        print(f"\n✓ Bit-perfect reconstruction of {len(original_state_dict)} layers")


@pytest.mark.slow  
@pytest.mark.safetensors
class TestRealSafeTensorsIngestion:
    """Test with real all-MiniLM-L6-v2 model (slow)."""
    
    async def test_ingest_first_5_layers(self, db_connection, clean_db, test_safetensors_path):
        """Test ingesting first 5 layers from MiniLM."""
        if not test_safetensors_path.exists():
            pytest.skip(f"SafeTensors model not found: {test_safetensors_path}")
        
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Load first 5 layers
        all_tensors = load_safetensors(test_safetensors_path)
        first_5 = dict(list(all_tensors.items())[:5])
        
        # Ingest
        comp_ids = await atomizer.ingest_model(
            model_state_dict=first_5,
            model_name="test-minilm-partial",
            chunk_size=10000
        )
        
        # Verify
        assert len(comp_ids) >= len(first_5), "Should create compositions (may be chunked)"
        
        print(f"\n✓ Ingested first 5 MiniLM layers:")
        print(f"  Layer names: {list(first_5.keys())}")
        print(f"  Compositions: {len(comp_ids)}")
        
        # Show parameter counts
        for name, tensor in first_5.items():
            print(f"  {name}: {tensor.shape} ({tensor.size:,} params)")
    
    async def test_query_stored_embeddings(self, db_connection, clean_db, test_safetensors_path):
        """Test querying stored embedding compositions."""
        if not test_safetensors_path.exists():
            pytest.skip(f"SafeTensors model not found: {test_safetensors_path}")
        
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Ingest first layer
        all_tensors = load_safetensors(test_safetensors_path)
        first_layer = {list(all_tensors.keys())[0]: list(all_tensors.values())[0]}
        
        await atomizer.ingest_model(
            model_state_dict=first_layer,
            model_name="test-minilm-query",
            chunk_size=10000
        )
        
        # Query compositions
        async with db_connection.cursor() as cur:
            await cur.execute("""
                SELECT 
                    name,
                    ST_NumPoints(spatial_key) as point_count,
                    pg_column_size(spatial_key) as storage_bytes
                FROM atom_composition
                WHERE metadata->>'model' = %s
                ORDER BY name
            """, ("test-minilm-query",))
            
            rows = await cur.fetchall()
        
        assert len(rows) > 0, "Should have stored compositions"
        
        print(f"\n✓ Query results:")
        for name, point_count, storage_bytes in rows:
            print(f"  {name}: {point_count} points, {storage_bytes:,} bytes")
