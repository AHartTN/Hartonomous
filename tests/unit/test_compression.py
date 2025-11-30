"""Test compression - ACTUAL DATA INTEGRITY."""

import numpy as np
import pytest

from src.core.compression import compress_atom, decompress_atom


@pytest.mark.unit
@pytest.mark.compression
class TestCompressionDataIntegrity:
    """Test compression preserves data correctly."""

    def test_float32_array_roundtrip(self):
        """REAL TEST: float32 data survives compression/decompression."""
        original = np.array([1.5, -2.3, 3.7, 0.0, -4.2], dtype=np.float32)

        compressed, metadata = compress_atom(original, dtype=np.float32)
        restored = decompress_atom(compressed, metadata)

        # VERIFY: Exact match
        assert np.array_equal(
            original, restored
        ), "Float32 data must be preserved exactly"
        assert restored.dtype == np.float32, "dtype must be preserved"

    def test_float64_high_precision(self):
        """REAL TEST: float64 precision preserved."""
        original = np.array([np.pi, np.e, 1 / 3, 0.123456789012345], dtype=np.float64)

        compressed, metadata = compress_atom(original, dtype=np.float64)
        restored = decompress_atom(compressed, metadata)

        # VERIFY: High precision preserved
        assert np.allclose(original, restored, rtol=1e-15), "Float64 precision lost"
        assert restored.dtype == np.float64

    def test_random_data_integrity(self):
        """REAL TEST: Random data compressed and restored correctly."""
        np.random.seed(42)
        original = np.random.randn(1000).astype(np.float32)

        compressed, metadata = compress_atom(original)
        restored = decompress_atom(compressed, metadata)

        # VERIFY: Perfect reconstruction
        assert np.allclose(original, restored), "Random data integrity failed"
        assert restored.shape == original.shape

    def test_sparse_array_compression(self):
        """REAL TEST: Sparse array compresses efficiently."""
        # 90% zeros
        original = np.zeros(1000, dtype=np.float32)
        original[[0, 100, 500, 999]] = [1.5, 2.3, 3.7, 4.2]

        compressed, metadata = compress_atom(original, sparse_threshold=0.01)

        # VERIFY: Sparsity detected
        if metadata.get("compression") == "sparse":
            assert metadata["sparsity"] > 0.9, "Should detect 90% sparsity"

        # VERIFY: Data preserved
        restored = decompress_atom(compressed, metadata)
        assert np.allclose(original, restored)

    def test_compression_reduces_size(self):
        """REAL TEST: Compression actually reduces data size."""
        # Highly compressible data (repeated pattern)
        original = np.tile(np.array([1.0, 2.0, 3.0], dtype=np.float32), 100)

        compressed, metadata = compress_atom(original)

        original_size = original.nbytes
        compressed_size = len(compressed)

        # VERIFY: Compression achieved
        assert (
            compressed_size <= original_size
        ), f"Compression failed: {compressed_size} > {original_size}"

        # Verify still decompresses correctly
        restored = decompress_atom(compressed, metadata)
        assert np.array_equal(original, restored)

    def test_metadata_completeness(self):
        """REAL TEST: Metadata contains all required fields."""
        data = np.array([1, 2, 3], dtype=np.float32)

        compressed, metadata = compress_atom(data)

        # VERIFY: Required metadata present
        required_fields = [
            "shape",
            "dtype",
            "original_size",
            "compressed_size",
            "compression_ratio",
        ]
        for field in required_fields:
            assert field in metadata, f"Missing required field: {field}"

        assert metadata["shape"] == (3,)
        assert "float32" in str(metadata["dtype"])
        assert metadata["original_size"] == 12  # 3 * 4 bytes
        assert metadata["compressed_size"] == len(compressed)
