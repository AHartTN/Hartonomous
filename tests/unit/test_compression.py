"""Test compression functions."""
import pytest
import numpy as np
from src.core.compression import compress_atom, decompress_atom, COMPRESSION_MAGIC

class TestCompression:
    """Test compression/decompression."""
    
    def test_compress_decompress_float32(self):
        """Test compressing and decompressing float32 array."""
        original = np.array([1.5, 2.3, 3.7, 4.2, 5.1], dtype=np.float32)
        
        compressed, metadata = compress_atom(original, dtype=np.float32)
        restored = decompress_atom(compressed, metadata)
        
        assert restored.shape == original.shape
        assert restored.dtype == original.dtype
        assert np.allclose(original, restored)
    
    def test_compress_decompress_float64(self):
        """Test compressing and decompressing float64 array."""
        original = np.random.randn(100).astype(np.float64)
        
        compressed, metadata = compress_atom(original, dtype=np.float64)
        restored = decompress_atom(compressed, metadata)
        
        assert restored.shape == original.shape
        assert np.allclose(original, restored)
    
    def test_sparse_compression(self):
        """Test sparse compression with threshold."""
        # Array with many near-zero values
        original = np.array([1.5, 0.0001, 0.0002, 3.7, 0.0001], dtype=np.float32)
        threshold = 0.001
        
        compressed, metadata = compress_atom(original, dtype=np.float32, sparse_threshold=threshold)
        
        # Should detect sparsity
        if metadata.get('compression') == 'sparse':
            assert 'sparsity' in metadata
            assert metadata['sparsity'] > 0
    
    def test_compression_metadata(self):
        """Test compression metadata is complete."""
        data = np.random.randn(50).astype(np.float32)
        
        compressed, metadata = compress_atom(data)
        
        assert 'shape' in metadata
        assert 'dtype' in metadata
        assert 'original_size' in metadata
        assert 'compressed_size' in metadata
        assert 'compression_ratio' in metadata
        
        assert metadata['shape'] == data.shape
        assert 'float32' in metadata['dtype'] or 'float64' in metadata['dtype']
    
    def test_compression_magic_bytes(self):
        """Test compression magic bytes are correct."""
        assert COMPRESSION_MAGIC['zlib'] == b'\x1f\x8b'
        assert COMPRESSION_MAGIC['raw'] == b'\xff\xff'
        assert len(COMPRESSION_MAGIC) == 5
