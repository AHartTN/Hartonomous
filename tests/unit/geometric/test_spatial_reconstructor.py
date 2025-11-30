"""
Unit Tests: SpatialReconstructor

Tests trajectory parsing and local reconstruction (no DB required).

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import pytest

from api.services.geometric_atomization import (
    AtomLocator,
    SpatialReconstructor,
    TrajectoryBuilder,
)


class TestWKTParsing:
    """Test LINESTRING ZM parsing."""

    def test_parse_simple_trajectory(self):
        """Parse basic LINESTRING ZM format."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (1.0 2.0 3.0 0, 4.0 5.0 6.0 1, 7.0 8.0 9.0 2)"
        points = reconstructor.parse_wkt(wkt)

        assert len(points) == 3
        assert points[0] == (1.0, 2.0, 3.0, 0.0)
        assert points[1] == (4.0, 5.0, 6.0, 1.0)
        assert points[2] == (7.0, 8.0, 9.0, 2.0)

    def test_parse_single_point(self):
        """Parse trajectory with single point."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (100.5 200.7 300.9 0)"
        points = reconstructor.parse_wkt(wkt)

        assert len(points) == 1
        assert points[0] == (100.5, 200.7, 300.9, 0.0)

    def test_parse_negative_coordinates(self):
        """Parse trajectory with negative coordinates."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (-100.0 -200.0 -300.0 0, 100.0 200.0 300.0 1)"
        points = reconstructor.parse_wkt(wkt)

        assert len(points) == 2
        assert points[0] == (-100.0, -200.0, -300.0, 0.0)
        assert points[1] == (100.0, 200.0, 300.0, 1.0)

    def test_parse_case_insensitive(self):
        """WKT parsing should be case insensitive."""
        reconstructor = SpatialReconstructor()

        # Try lowercase
        wkt_lower = "linestring zm (1.0 2.0 3.0 0)"
        points = reconstructor.parse_wkt(wkt_lower)
        assert len(points) == 1

        # Try mixed case
        wkt_mixed = "LineString ZM (1.0 2.0 3.0 0)"
        points = reconstructor.parse_wkt(wkt_mixed)
        assert len(points) == 1

    def test_parse_invalid_wkt_raises(self):
        """Invalid WKT should raise ValueError."""
        reconstructor = SpatialReconstructor()

        with pytest.raises(ValueError, match="Invalid LINESTRING ZM"):
            reconstructor.parse_wkt("POINT (1 2)")

        with pytest.raises(ValueError, match="Invalid LINESTRING ZM"):
            reconstructor.parse_wkt("LINESTRING (1 2)")  # Missing ZM

        with pytest.raises(ValueError, match="Expected 4 coordinates"):
            reconstructor.parse_wkt("LINESTRING ZM (1 2 3)")  # Only 3 coords


class TestLocalReconstruction:
    """Test reconstruction without database (local lookup)."""

    def test_reconstruct_hello_world(self):
        """Reconstruct 'Hello World' from trajectory."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        # Original text
        text = "Hello World"
        atom_values = [char.encode("utf-8") for char in text]

        # Build trajectory
        wkt = builder.build_from_atoms(atom_values, locator)

        # Create lookup dict
        coordinates = locator.locate_multiple(atom_values)
        lookup_dict = {coord: value for coord, value in zip(coordinates, atom_values)}

        # Reconstruct
        reconstructed_atoms = reconstructor.reconstruct_local(wkt, lookup_dict)

        # Should match original
        assert reconstructed_atoms == atom_values

        # Verify text
        reconstructed_text = b"".join(reconstructed_atoms).decode("utf-8")
        assert reconstructed_text == text

    def test_reconstruct_empty_sequence(self):
        """Handle empty trajectory gracefully."""
        reconstructor = SpatialReconstructor()

        # Empty trajectory would be unusual, but test parsing
        # Most systems would store at least one point
        # This test ensures we don't crash on edge cases
        wkt = "LINESTRING ZM (0.0 0.0 0.0 0)"
        lookup_dict = {(0.0, 0.0, 0.0): b""}

        reconstructed = reconstructor.reconstruct_local(wkt, lookup_dict)
        assert len(reconstructed) == 1
        assert reconstructed[0] == b""

    def test_reconstruct_unicode(self):
        """Reconstruct Unicode text correctly."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        # Unicode text (emoji, CJK, etc.)
        text = "Hello 世界 🌍"
        atom_values = [char.encode("utf-8") for char in text]

        # Build trajectory
        wkt = builder.build_from_atoms(atom_values, locator)

        # Create lookup
        coordinates = locator.locate_multiple(atom_values)
        lookup_dict = {coord: value for coord, value in zip(coordinates, atom_values)}

        # Reconstruct
        reconstructed_atoms = reconstructor.reconstruct_local(wkt, lookup_dict)
        reconstructed_text = b"".join(reconstructed_atoms).decode("utf-8")

        assert reconstructed_text == text

    def test_reconstruct_binary_data(self):
        """Reconstruct binary data (not just text)."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        # Binary sequence
        binary_data = [bytes([i]) for i in range(256)]

        # Build trajectory
        wkt = builder.build_from_atoms(binary_data, locator)

        # Create lookup
        coordinates = locator.locate_multiple(binary_data)
        lookup_dict = {coord: value for coord, value in zip(coordinates, binary_data)}

        # Reconstruct
        reconstructed = reconstructor.reconstruct_local(wkt, lookup_dict)

        assert reconstructed == binary_data

    def test_reconstruct_missing_coordinate_raises(self):
        """Reconstruction should fail if coordinate not in lookup."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (1.0 2.0 3.0 0)"
        lookup_dict = {}  # Empty lookup

        with pytest.raises(ValueError, match="No atom found at coordinate"):
            reconstructor.reconstruct_local(wkt, lookup_dict)


class TestCoordinateTolerance:
    """Test floating point tolerance in coordinate matching."""

    def test_exact_match(self):
        """Exact coordinate match should work."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (1.0 2.0 3.0 0)"
        lookup_dict = {(1.0, 2.0, 3.0): b"X"}

        reconstructed = reconstructor.reconstruct_local(wkt, lookup_dict)
        assert reconstructed == [b"X"]

    def test_floating_point_imprecision(self):
        """Handle floating point imprecision with tolerance."""
        reconstructor = SpatialReconstructor()

        # Coordinates with tiny floating point differences
        wkt = "LINESTRING ZM (1.0000000001 2.0 3.0 0)"
        lookup_dict = {(1.0, 2.0, 3.0): b"X"}

        # Should match within default tolerance (1e-6)
        reconstructed = reconstructor.reconstruct_local(
            wkt, lookup_dict, tolerance=1e-6
        )
        assert reconstructed == [b"X"]

    def test_custom_tolerance(self):
        """Custom tolerance for coordinate matching."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (1.1 2.0 3.0 0)"
        lookup_dict = {(1.0, 2.0, 3.0): b"X"}

        # Should NOT match with tight tolerance
        with pytest.raises(ValueError):
            reconstructor.reconstruct_local(wkt, lookup_dict, tolerance=1e-6)

        # Should match with loose tolerance
        reconstructed = reconstructor.reconstruct_local(wkt, lookup_dict, tolerance=0.2)
        assert reconstructed == [b"X"]


class TestSequenceOrdering:
    """Test M coordinate ordering for reconstruction."""

    def test_unsorted_points_sorted_by_m(self):
        """Points should be sorted by M coordinate during reconstruction."""
        reconstructor = SpatialReconstructor()

        # Points out of order in WKT
        wkt = "LINESTRING ZM (7.0 8.0 9.0 2, 1.0 2.0 3.0 0, 4.0 5.0 6.0 1)"

        # Lookup for all three points
        lookup_dict = {
            (1.0, 2.0, 3.0): b"A",
            (4.0, 5.0, 6.0): b"B",
            (7.0, 8.0, 9.0): b"C",
        }

        # Reconstruct
        reconstructed = reconstructor.reconstruct_local(wkt, lookup_dict)

        # Should be sorted by M: 0, 1, 2 → A, B, C
        assert reconstructed == [b"A", b"B", b"C"]

    def test_negative_m_values(self):
        """Handle negative M coordinates."""
        reconstructor = SpatialReconstructor()

        wkt = "LINESTRING ZM (1.0 2.0 3.0 -10, 4.0 5.0 6.0 -5, 7.0 8.0 9.0 0)"
        lookup_dict = {
            (1.0, 2.0, 3.0): b"A",
            (4.0, 5.0, 6.0): b"B",
            (7.0, 8.0, 9.0): b"C",
        }

        reconstructed = reconstructor.reconstruct_local(wkt, lookup_dict)

        # Should be sorted: -10, -5, 0
        assert reconstructed == [b"A", b"B", b"C"]


class TestRoundtripIntegrity:
    """Test bit-perfect roundtrip: atomize → reconstruct."""

    def test_text_roundtrip(self):
        """Bit-perfect text roundtrip."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        original = "The quick brown fox jumps over the lazy dog"
        atom_values = [char.encode("utf-8") for char in original]

        # Atomize
        wkt = builder.build_from_atoms(atom_values, locator)

        # Reconstruct
        coordinates = locator.locate_multiple(atom_values)
        lookup_dict = {coord: value for coord, value in zip(coordinates, atom_values)}
        reconstructed_atoms = reconstructor.reconstruct_local(wkt, lookup_dict)
        reconstructed_text = b"".join(reconstructed_atoms).decode("utf-8")

        assert reconstructed_text == original

    def test_long_text_roundtrip(self):
        """Bit-perfect roundtrip for longer text."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        reconstructor = SpatialReconstructor()

        # 1000 character text
        original = "Lorem ipsum dolor sit amet. " * 36  # ~1000 chars
        atom_values = [char.encode("utf-8") for char in original]

        wkt = builder.build_from_atoms(atom_values, locator)

        coordinates = locator.locate_multiple(atom_values)
        lookup_dict = {coord: value for coord, value in zip(coordinates, atom_values)}
        reconstructed_atoms = reconstructor.reconstruct_local(wkt, lookup_dict)
        reconstructed_text = b"".join(reconstructed_atoms).decode("utf-8")

        assert reconstructed_text == original
        assert len(reconstructed_text) == len(original)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
