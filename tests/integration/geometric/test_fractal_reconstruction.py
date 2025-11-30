"""
Integration tests for fractal reconstruction and bit-perfect roundtrips.

Tests the complete atomization → storage → reconstruction pipeline:
1. Atomize various data types (float32, float16, int8, text)
2. Store in database as LINESTRING trajectories
3. Reconstruct from trajectories
4. Verify bit-perfect accuracy
5. Test composition expansion (fractal deduplication)

Uses greenfield GeometricAtomizer architecture.
"""

import pytest
import numpy as np
from typing import List

from api.services.geometric_atomization import GeometricAtomizer

pytestmark = [pytest.mark.asyncio, pytest.mark.integration]


class TestSimpleRoundtrips:
    """Test basic atomization → reconstruction roundtrips."""
    
    async def test_text_roundtrip(self, db_connection, clean_db):
        """Test text atomization and reconstruction."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Original text
        original_text = "Hello, World! This is a test."
        
        # Atomize
        wkt = await atomizer.atomize_text(original_text)
        
        # Store
        comp_id = await atomizer.store_trajectory(
            name="test.text_roundtrip",
            wkt=wkt,
            metadata={'type': 'text'}
        )
        
        # Reconstruct
        reconstructed_text = await atomizer.reconstruct_from_name(
            name="test.text_roundtrip",
            output_type='text'
        )
        
        # Verify exact match
        assert reconstructed_text == original_text, "Text should match exactly"
        
        print(f"\n✓ Text roundtrip:")
        print(f"  Original: '{original_text}'")
        print(f"  Reconstructed: '{reconstructed_text}'")
        print(f"  Match: {original_text == reconstructed_text}")
    
    async def test_float32_roundtrip(self, db_connection, clean_db):
        """Test float32 tensor roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Original tensor
        original = np.array([1.5, -2.7, 3.14159, 0.0, -100.25], dtype=np.float32)
        
        # Atomize → Store
        wkts = await atomizer.atomize_tensor(original, chunk_size=100)
        comp_id = await atomizer.store_trajectory(
            name="test.float32",
            wkt=wkts[0],
            metadata={'shape': list(original.shape), 'dtype': 'float32'}
        )
        
        # Reconstruct
        reconstructed_bytes = await atomizer.reconstruct_from_name(
            name="test.float32",
            output_type='bytes'
        )
        
        # Convert bytes back to float32
        reconstructed = np.frombuffer(b''.join(reconstructed_bytes), dtype=np.float32)
        
        # Verify bit-perfect match
        np.testing.assert_array_equal(reconstructed, original)
        
        print(f"\n✓ Float32 roundtrip:")
        print(f"  Original: {original}")
        print(f"  Reconstructed: {reconstructed}")
        print(f"  Bit-perfect: {np.array_equal(reconstructed, original)}")
    
    async def test_int8_roundtrip(self, db_connection, clean_db):
        """Test int8 tensor roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Original int8 tensor
        original = np.array([-128, -1, 0, 1, 127], dtype=np.int8)
        
        # Atomize → Store
        wkts = await atomizer.atomize_tensor(original, chunk_size=100)
        comp_id = await atomizer.store_trajectory(
            name="test.int8",
            wkt=wkts[0],
            metadata={'shape': list(original.shape), 'dtype': 'int8'}
        )
        
        # Reconstruct
        reconstructed_bytes = await atomizer.reconstruct_from_name(
            name="test.int8",
            output_type='bytes'
        )
        
        # Convert bytes back to int8
        reconstructed = np.frombuffer(b''.join(reconstructed_bytes), dtype=np.int8)
        
        # Verify
        np.testing.assert_array_equal(reconstructed, original)
        
        print(f"\n✓ Int8 roundtrip:")
        print(f"  Original: {original}")
        print(f"  Reconstructed: {reconstructed}")


class TestChunkedRoundtrips:
    """Test roundtrips with chunked trajectories."""
    
    async def test_chunked_tensor_roundtrip(self, db_connection, clean_db):
        """Test large tensor chunked roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Large tensor requiring chunking
        original = np.random.randn(15000).astype(np.float32)
        
        # Atomize with chunking
        wkts = await atomizer.atomize_tensor(original, chunk_size=5000)
        
        # Verify chunking occurred
        assert len(wkts) == 3, "15K values / 5K chunk = 3 chunks"
        
        # Store all chunks with metadata
        for i, wkt in enumerate(wkts):
            await atomizer.store_trajectory(
                name=f"test.chunked.chunk{i}",
                wkt=wkt,
                metadata={
                    'shape': list(original.shape),
                    'dtype': 'float32',
                    'chunk_index': i,
                    'total_chunks': len(wkts)
                }
            )
        
        # Reconstruct all chunks in order
        reconstructed_parts = []
        for i in range(len(wkts)):
            chunk_bytes = await atomizer.reconstruct_from_name(
                name=f"test.chunked.chunk{i}",
                output_type='bytes'
            )
            reconstructed_parts.extend(chunk_bytes)
        
        # Convert to array
        reconstructed = np.frombuffer(b''.join(reconstructed_parts), dtype=np.float32)
        
        # Verify exact match
        np.testing.assert_array_equal(reconstructed, original)
        
        print(f"\n✓ Chunked tensor roundtrip:")
        print(f"  Original shape: {original.shape}")
        print(f"  Chunks: {len(wkts)}")
        print(f"  Reconstructed shape: {reconstructed.shape}")
        print(f"  Match: {np.array_equal(reconstructed, original)}")


class TestMultiDimensionalRoundtrips:
    """Test roundtrips with multi-dimensional tensors."""
    
    async def test_2d_matrix_roundtrip(self, db_connection, clean_db):
        """Test 2D matrix roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # 2D matrix (e.g., small weight matrix)
        original = np.array([
            [1.0, 2.0, 3.0],
            [4.0, 5.0, 6.0],
            [7.0, 8.0, 9.0]
        ], dtype=np.float32)
        
        original_shape = original.shape
        
        # Atomize (flattens automatically)
        wkts = await atomizer.atomize_tensor(original, chunk_size=100)
        
        # Store with shape metadata
        await atomizer.store_trajectory(
            name="test.matrix_2d",
            wkt=wkts[0],
            metadata={'shape': list(original_shape), 'dtype': 'float32'}
        )
        
        # Reconstruct
        reconstructed_bytes = await atomizer.reconstruct_from_name(
            name="test.matrix_2d",
            output_type='bytes'
        )
        
        # Convert back to 2D
        flat_array = np.frombuffer(b''.join(reconstructed_bytes), dtype=np.float32)
        reconstructed = flat_array.reshape(original_shape)
        
        # Verify
        np.testing.assert_array_equal(reconstructed, original)
        
        print(f"\n✓ 2D matrix roundtrip:")
        print(f"  Original shape: {original_shape}")
        print(f"  Flattened for atomization: {flat_array.shape}")
        print(f"  Reconstructed shape: {reconstructed.shape}")
    
    async def test_3d_tensor_roundtrip(self, db_connection, clean_db):
        """Test 3D tensor roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # 3D tensor (e.g., small conv filter)
        original = np.random.randn(4, 4, 3).astype(np.float32)
        original_shape = original.shape
        
        # Atomize → Store
        wkts = await atomizer.atomize_tensor(original, chunk_size=100)
        await atomizer.store_trajectory(
            name="test.tensor_3d",
            wkt=wkts[0],
            metadata={'shape': list(original_shape), 'dtype': 'float32'}
        )
        
        # Reconstruct
        reconstructed_bytes = await atomizer.reconstruct_from_name(
            name="test.tensor_3d",
            output_type='bytes'
        )
        
        # Reshape
        flat_array = np.frombuffer(b''.join(reconstructed_bytes), dtype=np.float32)
        reconstructed = flat_array.reshape(original_shape)
        
        # Verify
        np.testing.assert_array_equal(reconstructed, original)
        
        print(f"\n✓ 3D tensor roundtrip:")
        print(f"  Shape: {original_shape}")
        print(f"  Total elements: {original.size}")


@pytest.mark.slow
class TestUnicodeRoundtrips:
    """Test roundtrips with Unicode text."""
    
    async def test_unicode_text(self, db_connection, clean_db):
        """Test Unicode text roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Unicode text with emojis and special chars
        original_text = "Hello 世界! 🚀 Testing émojis and spëcial çhars: αβγδ"
        
        # Atomize → Store
        wkt = await atomizer.atomize_text(original_text)
        await atomizer.store_trajectory(
            name="test.unicode",
            wkt=wkt,
            metadata={'encoding': 'utf-8'}
        )
        
        # Reconstruct
        reconstructed = await atomizer.reconstruct_from_name(
            name="test.unicode",
            output_type='text'
        )
        
        # Verify
        assert reconstructed == original_text, "Unicode should match exactly"
        
        print(f"\n✓ Unicode roundtrip:")
        print(f"  Original: '{original_text}'")
        print(f"  Reconstructed: '{reconstructed}'")
    
    async def test_long_unicode_text(self, db_connection, clean_db):
        """Test long Unicode document roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Longer text with mixed languages
        original_text = """
        English: The quick brown fox jumps over the lazy dog.
        Español: El rápido zorro marrón salta sobre el perro perezoso.
        Français: Le rapide renard brun saute par-dessus le chien paresseux.
        日本語: 速い茶色のキツネが怠け者の犬を飛び越えます。
        Emoji test: 🎉 🔥 💻 🚀 ⚡
        """ * 5  # Repeat 5 times for length
        
        # Atomize → Store
        wkt = await atomizer.atomize_text(original_text)
        await atomizer.store_trajectory(
            name="test.long_unicode",
            wkt=wkt,
            metadata={'length': len(original_text)}
        )
        
        # Reconstruct
        reconstructed = await atomizer.reconstruct_from_name(
            name="test.long_unicode",
            output_type='text'
        )
        
        # Verify
        assert reconstructed == original_text
        
        print(f"\n✓ Long Unicode roundtrip:")
        print(f"  Original length: {len(original_text)} chars")
        print(f"  Reconstructed length: {len(reconstructed)} chars")


@pytest.mark.slow
class TestFullModelRoundtrips:
    """Test full model ingestion and reconstruction."""
    
    async def test_mock_model_roundtrip(self, db_connection, clean_db):
        """Test complete model roundtrip."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Mock model state dict (small)
        original_state_dict = {
            'embeddings.weight': np.random.randn(50, 32).astype(np.float32),
            'layer1.weight': np.random.randn(32, 32).astype(np.float32),
            'layer1.bias': np.random.randn(32).astype(np.float32),
            'output.weight': np.random.randn(10, 32).astype(np.float32)
        }
        
        # Ingest
        comp_ids = await atomizer.ingest_model(
            model_state_dict=original_state_dict,
            model_name="test-roundtrip-model",
            chunk_size=10000
        )
        
        # Reconstruct
        reconstructed_state_dict = await atomizer.reconstruct_model("test-roundtrip-model")
        
        # Verify all layers
        assert len(reconstructed_state_dict) == len(original_state_dict)
        
        for layer_name, original_tensor in original_state_dict.items():
            assert layer_name in reconstructed_state_dict, f"Layer {layer_name} missing"
            
            reconstructed_tensor = reconstructed_state_dict[layer_name]
            
            # Bit-perfect match
            np.testing.assert_array_equal(
                reconstructed_tensor,
                original_tensor,
                err_msg=f"Layer {layer_name} mismatch"
            )
        
        print(f"\n✓ Full model roundtrip:")
        print(f"  Layers: {len(original_state_dict)}")
        print(f"  All layers bit-perfect: True")
    
    async def test_large_layer_chunked_roundtrip(self, db_connection, clean_db):
        """Test large layer requiring chunking."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Large layer (>10K values, will be chunked)
        original_state_dict = {
            'large_layer': np.random.randn(200, 100).astype(np.float32)  # 20K values
        }
        
        # Ingest with chunk_size=8000
        comp_ids = await atomizer.ingest_model(
            model_state_dict=original_state_dict,
            model_name="test-chunked-roundtrip",
            chunk_size=8000
        )
        
        # Should create multiple chunks
        assert len(comp_ids) > 1, "Large layer should be chunked"
        
        # Reconstruct
        reconstructed_state_dict = await atomizer.reconstruct_model("test-chunked-roundtrip")
        
        # Verify
        reconstructed_tensor = reconstructed_state_dict['large_layer']
        original_tensor = original_state_dict['large_layer']
        
        np.testing.assert_array_equal(reconstructed_tensor, original_tensor)
        
        print(f"\n✓ Large layer chunked roundtrip:")
        print(f"  Shape: {original_tensor.shape}")
        print(f"  Total values: {original_tensor.size:,}")
        print(f"  Chunks created: {len(comp_ids)}")
        print(f"  Bit-perfect: True")


class TestEdgeCases:
    """Test edge cases in reconstruction."""
    
    async def test_single_value_roundtrip(self, db_connection, clean_db):
        """Test roundtrip of single value."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Single float32 value
        original = np.array([3.14159], dtype=np.float32)
        
        # Atomize → Store
        wkts = await atomizer.atomize_tensor(original, chunk_size=100)
        await atomizer.store_trajectory(
            name="test.single_value",
            wkt=wkts[0],
            metadata={'shape': [1], 'dtype': 'float32'}
        )
        
        # Reconstruct
        reconstructed_bytes = await atomizer.reconstruct_from_name(
            name="test.single_value",
            output_type='bytes'
        )
        
        reconstructed = np.frombuffer(b''.join(reconstructed_bytes), dtype=np.float32)
        
        # Verify
        np.testing.assert_array_equal(reconstructed, original)
        
        print(f"\n✓ Single value roundtrip: {original[0]} == {reconstructed[0]}")
    
    async def test_zero_values_roundtrip(self, db_connection, clean_db):
        """Test roundtrip of zeros."""
        atomizer = GeometricAtomizer(db_connection=db_connection)
        
        # Array of zeros
        original = np.zeros(10, dtype=np.float32)
        
        # Atomize → Store
        wkts = await atomizer.atomize_tensor(original, chunk_size=100)
        await atomizer.store_trajectory(
            name="test.zeros",
            wkt=wkts[0],
            metadata={'shape': [10], 'dtype': 'float32'}
        )
        
        # Reconstruct
        reconstructed_bytes = await atomizer.reconstruct_from_name(
            name="test.zeros",
            output_type='bytes'
        )
        
        reconstructed = np.frombuffer(b''.join(reconstructed_bytes), dtype=np.float32)
        
        # Verify
        np.testing.assert_array_equal(reconstructed, original)
        
        print(f"\n✓ Zeros roundtrip: all {len(original)} values == 0.0")

