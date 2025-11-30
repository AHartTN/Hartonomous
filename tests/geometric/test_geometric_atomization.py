"""
Tests for Geometric Atomization

Verifies the breakthrough trajectory-based architecture:
1. Deterministic coordinate mapping (same atom → same location)
2. Single LINESTRING for sequences (no record explosion)
3. Bit-perfect reconstruction

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import numpy as np
import pytest

from api.services.geometric_atomization import (
    AtomLocator,
    GeometricAtomizer,
    SpatialReconstructor,
    TrajectoryBuilder,
)


class TestAtomLocator:
    """Test deterministic coordinate mapping."""

    def test_deterministic_coordinates(self):
        """Same atom value always produces same coordinate."""
        locator = AtomLocator()

        value = b"Hello"

        # Call locate() multiple times
        coord1 = locator.locate(value)
        coord2 = locator.locate(value)
        coord3 = locator.locate(value)

        # Should be identical
        assert coord1 == coord2 == coord3

    def test_different_atoms_different_coords(self):
        """Different atoms produce different coordinates."""
        locator = AtomLocator()

        coord_h = locator.locate(b"H")
        coord_e = locator.locate(b"e")
        coord_l = locator.locate(b"l")
        coord_o = locator.locate(b"o")

        # All should be unique
        coords = [coord_h, coord_e, coord_l, coord_o]
        assert len(coords) == len(set(coords))  # No duplicates

    def test_coordinate_range(self):
        """Coordinates stay within specified range."""
        locator = AtomLocator(coordinate_range=1e6)

        # Test many random values
        for i in range(100):
            value = f"test_{i}".encode("utf-8")
            x, y, z = locator.locate(value)

            # Check range: [-1e6, +1e6]
            assert -1e6 <= x <= 1e6
            assert -1e6 <= y <= 1e6
            assert -1e6 <= z <= 1e6

    def test_hilbert_index(self):
        """Hilbert index produces valid BIGINT."""
        locator = AtomLocator()

        x, y, z = 0.0, 0.0, 0.0
        m = locator.compute_hilbert_index(x, y, z)

        # Should be non-negative integer
        assert isinstance(m, int)
        assert m >= 0

        # Should fit in PostgreSQL BIGINT (63 bits signed)
        max_bigint = 2**63 - 1
        assert m <= max_bigint


class TestTrajectoryBuilder:
    """Test LINESTRING trajectory construction."""

    def test_build_simple_trajectory(self):
        """Build trajectory from coordinates."""
        builder = TrajectoryBuilder()

        coords = [(1.0, 2.0, 3.0), (4.0, 5.0, 6.0), (7.0, 8.0, 9.0)]

        wkt = builder.build_wkt(coords)

        # Should produce valid WKT
        assert wkt.startswith("LINESTRING ZM (")
        assert wkt.endswith(")")

        # Should contain all coordinates
        assert "1.0 2.0 3.0" in wkt
        assert "4.0 5.0 6.0" in wkt
        assert "7.0 8.0 9.0" in wkt

        # Should have M values (0, 1, 2)
        assert "0" in wkt
        assert "1" in wkt
        assert "2" in wkt

    def test_hello_world_single_linestring(self):
        """
        CRITICAL TEST: "Hello World" should be ONE LINESTRING row.

        This is the breakthrough - no record explosion.
        """
        locator = AtomLocator()
        builder = TrajectoryBuilder()

        text = "Hello World"
        atom_values = [char.encode("utf-8") for char in text]

        # Build trajectory
        wkt = builder.build_from_atoms(atom_values, locator)

        # Should be single WKT string (not 11 separate rows)
        assert isinstance(wkt, str)
        assert wkt.startswith("LINESTRING ZM (")

        # Should contain 11 points (one per character)
        # Count commas: 11 points = 10 commas
        comma_count = wkt.count(",")
        assert comma_count == len(text) - 1

    def test_large_tensor_chunking(self):
        """Large tensors can be chunked to avoid PostGIS limits."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()

        # Create 50K atom sequence
        atom_values = [f"atom_{i}".encode("utf-8") for i in range(50000)]
        coordinates = locator.locate_multiple(atom_values)

        # Chunk into 10K per trajectory
        chunks = builder.chunk_trajectory(coordinates, chunk_size=10000)

        # Should produce 5 chunks
        assert len(chunks) == 5

        # Each should be valid WKT
        for chunk in chunks:
            assert chunk.startswith("LINESTRING ZM (")


class TestSpatialReconstructor:
    """Test trajectory reconstruction."""

    def test_parse_wkt(self):
        """Parse WKT into coordinate list."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (1.0 2.0 3.0 0, 4.0 5.0 6.0 1, 7.0 8.0 9.0 2)"
        points = reconstructor.parse_wkt(wkt)

        assert len(points) == 3
        assert points[0] == (1.0, 2.0, 3.0, 0.0)
        assert points[1] == (4.0, 5.0, 6.0, 1.0)
        assert points[2] == (7.0, 8.0, 9.0, 2.0)

    def test_reconstruct_local(self):
        """Reconstruct sequence without database (using local lookup)."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        # Original text
        text = "Hello"
        atom_values = [char.encode("utf-8") for char in text]

        # Build trajectory
        wkt = builder.build_from_atoms(atom_values, locator)

        # Create lookup dict: coordinate → atom value
        coordinates = locator.locate_multiple(atom_values)
        lookup_dict = {coord: value for coord, value in zip(coordinates, atom_values)}

        # Reconstruct
        reconstructed_atoms = reconstructor.reconstruct_local(wkt, lookup_dict)

        # Should match original
        assert reconstructed_atoms == atom_values

        # Verify text
        reconstructed_text = b"".join(reconstructed_atoms).decode("utf-8")
        assert reconstructed_text == text


class TestGeometricAtomizer:
    """Test high-level orchestrator."""

    async def test_atomize_text(self):
        """Atomize text into trajectory."""
        atomizer = GeometricAtomizer()

        text = "Hello World"
        wkt = await atomizer.atomize_text(text)

        # Should produce valid trajectory
        assert wkt.startswith("LINESTRING ZM (")

        # Should have 11 points
        comma_count = wkt.count(",")
        assert comma_count == len(text) - 1

    async def test_atomize_tensor_small(self):
        """Atomize small tensor (no chunking needed)."""
        atomizer = GeometricAtomizer()

        # Small tensor: 3x3 = 9 elements
        tensor = np.array(
            [[1.0, 2.0, 3.0], [4.0, 5.0, 6.0], [7.0, 8.0, 9.0]], dtype=np.float32
        )

        wkts = await atomizer.atomize_tensor(tensor, chunk_size=1000)

        # Should produce single trajectory (9 elements < 1000)
        assert len(wkts) == 1
        assert wkts[0].startswith("LINESTRING ZM (")

    async def test_atomize_tensor_large(self):
        """
        CRITICAL TEST: Large tensor should produce FEW trajectory rows.

        53M elements → ~5 trajectories (10K chunks) instead of 53M rows.
        """
        atomizer = GeometricAtomizer()

        # Simulate large tensor: 1000 x 1000 = 1M elements
        tensor = np.random.randn(1000, 1000).astype(np.float32)

        wkts = await atomizer.atomize_tensor(tensor, chunk_size=10000)

        # Should produce ~100 chunks (1M / 10K)
        expected_chunks = 1_000_000 // 10000
        assert len(wkts) == expected_chunks

        # Each should be valid trajectory
        for wkt in wkts[:5]:  # Check first 5
            assert wkt.startswith("LINESTRING ZM (")

    def test_roundtrip_text(self):
        """
        CRITICAL TEST: Bit-perfect text reconstruction.

        Proves the architecture works: atomize → store → reconstruct.
        """
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        # Original text
        original = "The quick brown fox jumps over the lazy dog"

        # Atomize
        atom_values = [char.encode("utf-8") for char in original]
        wkt = builder.build_from_atoms(atom_values, locator)

        # Reconstruct (local, no DB)
        coordinates = locator.locate_multiple(atom_values)
        lookup_dict = {coord: value for coord, value in zip(coordinates, atom_values)}
        reconstructed_atoms = reconstructor.reconstruct_local(wkt, lookup_dict)
        reconstructed_text = b"".join(reconstructed_atoms).decode("utf-8")

        # Should match EXACTLY
        assert reconstructed_text == original

    def test_roundtrip_tensor(self):
        """
        CRITICAL TEST: Bit-perfect tensor reconstruction.

        Proves tensor weights can be stored and reconstructed losslessly.
        """
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        # Original tensor (small for test speed)
        original = np.array(
            [[1.5, 2.7, 3.9], [4.1, 5.3, 6.8], [7.2, 8.4, 9.6]], dtype=np.float32
        )

        # Atomize
        flat_values = original.flatten()
        atom_values = [val.tobytes() for val in flat_values]
        wkt = builder.build_from_atoms(atom_values, locator)

        # Reconstruct (local)
        coordinates = locator.locate_multiple(atom_values)
        lookup_dict = {coord: value for coord, value in zip(coordinates, atom_values)}
        reconstructed_atoms = reconstructor.reconstruct_local(wkt, lookup_dict)

        # Convert back to tensor
        reconstructed_flat = np.frombuffer(
            b"".join(reconstructed_atoms), dtype=np.float32
        )
        reconstructed = reconstructed_flat.reshape(original.shape)

        # Should match EXACTLY (bit-perfect)
        np.testing.assert_array_equal(reconstructed, original)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
