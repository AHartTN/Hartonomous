"""Tests for multi-layer encoding system."""

import numpy as np
import pytest

from src.core.compression.encoding import (AtomEncoder, DeltaEncoder,
                                           EncodingType, MultiLayerEncoder,
                                           RLEEncoder, SparseEncoder)


class TestSparseEncoder:
    """Test sparse encoding."""

    def test_threshold_zeros(self):
        """Values below threshold become zero."""
        encoder = SparseEncoder(threshold=0.01)
        data = np.array([0.001, 0.5, 0.009, 1.0, 0.005])
        result, meta = encoder.encode(data)

        assert result[0] == 0.0
        assert result[1] == 0.5
        assert result[2] == 0.0
        assert meta["zeros_created"] == 3

    def test_sparsity_calculation(self):
        """Sparsity ratio calculated correctly."""
        encoder = SparseEncoder(threshold=0.1)
        data = np.array([0.05] * 80 + [1.0] * 20)
        result, meta = encoder.encode(data)

        assert meta["sparsity"] == 0.8


class TestRLEEncoder:
    """Test run-length encoding."""

    def test_single_run(self):
        """Single repeated value."""
        encoder = RLEEncoder()
        data = np.array([5.0] * 10)
        encoded, meta = encoder.encode(data)

        assert len(encoded) == 2  # value, count
        assert encoded[0] == 5.0
        assert encoded[1] == 10
        assert meta["runs"] == 1

    def test_multiple_runs(self):
        """Multiple runs of different values."""
        encoder = RLEEncoder()
        data = np.array([1.0, 1.0, 2.0, 2.0, 2.0, 3.0])
        encoded, meta = encoder.encode(data)

        assert len(encoded) == 6  # 3 runs * 2
        assert encoded[0] == 1.0 and encoded[1] == 2
        assert encoded[2] == 2.0 and encoded[3] == 3
        assert encoded[4] == 3.0 and encoded[5] == 1

    def test_decode(self):
        """Decoding reconstructs original."""
        encoder = RLEEncoder()
        original = np.array([1.0, 1.0, 2.0, 2.0, 2.0, 3.0])
        encoded, meta = encoder.encode(original)
        decoded = encoder.decode(encoded, meta, original.shape)

        np.testing.assert_array_equal(decoded, original)


class TestDeltaEncoder:
    """Test delta encoding."""

    def test_smooth_sequence(self):
        """Smooth increasing sequence."""
        encoder = DeltaEncoder()
        data = np.array([1.0, 1.1, 1.2, 1.3, 1.4])
        encoded, meta = encoder.encode(data)

        assert encoded[0] == 1.0  # First value
        assert np.allclose(encoded[1:], 0.1)  # All deltas
        assert meta["delta_range"] == pytest.approx(0.1)

    def test_decode(self):
        """Decoding reconstructs original."""
        encoder = DeltaEncoder()
        original = np.array([5.0, 5.5, 6.0, 6.5, 7.0])
        encoded, meta = encoder.encode(original)
        decoded = encoder.decode(encoded, meta, original.shape)

        np.testing.assert_array_almost_equal(decoded, original)


class TestMultiLayerEncoder:
    """Test combined encoding pipeline."""

    def test_sparse_data(self):
        """Sparse data gets sparse encoding."""
        encoder = MultiLayerEncoder()
        data = np.array([0.0001] * 90 + [1.0] * 10)
        result = encoder.encode(data)

        assert result.encoding_flags & EncodingType.SPARSE
        assert "sparse" in result.metadata

    def test_repeated_data(self):
        """Repeated data gets RLE."""
        encoder = MultiLayerEncoder()
        data = np.array([1.0] * 100)
        result = encoder.encode(data)

        assert result.encoding_flags & EncodingType.RLE
        assert result.data.size < data.size

    def test_smooth_data(self):
        """Smooth data gets delta encoding."""
        encoder = MultiLayerEncoder()
        data = np.arange(0, 100, 0.1)
        result = encoder.encode(data)

        assert result.encoding_flags & EncodingType.DELTA

    def test_decode_reversible(self):
        """Encoding/decoding is reversible (within precision)."""
        encoder = MultiLayerEncoder()
        original = np.array([0.0001, 0.0001, 1.0, 1.0, 1.0, 2.0])

        encoded = encoder.encode(original)
        decoded = encoder.decode(encoded)

        # Sparse encoding is lossy by design
        # Check non-zero values match
        assert np.allclose(decoded[decoded != 0], original[original > 1e-6])


class TestAtomEncoder:
    """Test atom-specific encoding."""

    def test_size_constraint(self):
        """Encoded atoms respect 64-byte limit."""
        encoder = AtomEncoder()

        # Try various data sizes
        for size in [1, 4, 8, 16, 32]:
            data = np.random.randn(size)
            encoded_bytes, flags, meta = encoder.encode_atom(data)
            assert len(encoded_bytes) <= 64

    def test_encode_decode_cycle(self):
        """Encoding and decoding preserves data."""
        encoder = AtomEncoder()
        original = np.array([1.0, 2.0, 3.0, 4.0])

        encoded_bytes, flags, meta = encoder.encode_atom(original)
        decoded = encoder.decode_atom(
            encoded_bytes, flags, meta["shape"], meta["dtype"]
        )

        np.testing.assert_array_almost_equal(decoded, original)

    def test_metadata(self):
        """Metadata includes necessary info."""
        encoder = AtomEncoder()
        data = np.array([1.0, 2.0])

        _, flags, meta = encoder.encode_atom(data)

        assert "size" in meta
        assert "flags" in meta
        assert "shape" in meta
        assert "dtype" in meta
