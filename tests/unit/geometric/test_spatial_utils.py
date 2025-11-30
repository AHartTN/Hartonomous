"""Unit tests for spatial_utils - centralized spatial operations.

Tests verify that spatial utilities:
1. Produce deterministic coordinates from hashes
2. Generate valid Hilbert indices (PostgreSQL BIGINT)
3. Correctly encode Morton curves
4. Handle composition coordinates properly
5. Generate safe SQL with proper validation

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import pytest

from api.services.geometric_atomization import spatial_utils


class TestHashToCoordinate:
    """Test deterministic hash→coordinate conversion."""

    def test_deterministic(self):
        """Same value always produces same coordinate."""
        value = b"Hello"

        coord1 = spatial_utils.hash_to_coordinate(value)
        coord2 = spatial_utils.hash_to_coordinate(value)
        coord3 = spatial_utils.hash_to_coordinate(value)

        assert coord1 == coord2 == coord3

    def test_unique(self):
        """Different values produce different coordinates."""
        coord_a = spatial_utils.hash_to_coordinate(b"A")
        coord_b = spatial_utils.hash_to_coordinate(b"B")
        coord_c = spatial_utils.hash_to_coordinate(b"C")

        coords = [coord_a, coord_b, coord_c]
        assert len(coords) == len(set(coords)), "All coordinates must be unique"

    def test_within_range(self):
        """Coordinates stay within specified range."""
        coord_range = 1e6

        for i in range(10):
            value = f"test_{i}".encode("utf-8")
            x, y, z = spatial_utils.hash_to_coordinate(value, coord_range)

            assert -coord_range <= x <= coord_range
            assert -coord_range <= y <= coord_range
            assert -coord_range <= z <= coord_range

    def test_custom_range(self):
        """Respects custom coordinate ranges."""
        value = b"test"

        small_range = 100.0
        x_s, y_s, z_s = spatial_utils.hash_to_coordinate(value, small_range)

        assert -small_range <= x_s <= small_range
        assert -small_range <= y_s <= small_range
        assert -small_range <= z_s <= small_range


class TestComputeHilbertIndex:
    """Test Hilbert curve index computation."""

    def test_deterministic(self):
        """Same coordinates always produce same Hilbert index."""
        x, y, z = 123.456, -789.012, 345.678

        m1 = spatial_utils.compute_hilbert_index(x, y, z)
        m2 = spatial_utils.compute_hilbert_index(x, y, z)
        m3 = spatial_utils.compute_hilbert_index(x, y, z)

        assert m1 == m2 == m3

    def test_valid_bigint(self):
        """Hilbert indices fit in PostgreSQL BIGINT."""
        max_bigint = 2**63 - 1

        test_coords = [
            (0.0, 0.0, 0.0),
            (1.0, 1.0, 1.0),
            (-1.0, -1.0, -1.0),
            (500.0, -300.0, 800.0),
            (-999999.0, 999999.0, 0.0),
        ]

        for x, y, z in test_coords:
            m = spatial_utils.compute_hilbert_index(x, y, z)

            assert isinstance(m, int)
            assert 0 <= m <= max_bigint

    def test_non_negative(self):
        """Hilbert indices are always non-negative."""
        for i in range(20):
            x = (i - 10) * 100.0
            y = (i - 10) * 50.0
            z = (i - 10) * 75.0

            m = spatial_utils.compute_hilbert_index(x, y, z)
            assert m >= 0


class TestMortonEncode:
    """Test Morton (Z-order) curve encoding."""

    def test_deterministic(self):
        """Same input always produces same Morton code."""
        x, y, z = 12345, 67890, 11111

        m1 = spatial_utils.morton_encode(x, y, z)
        m2 = spatial_utils.morton_encode(x, y, z)

        assert m1 == m2

    def test_origin(self):
        """Morton code for origin is 0."""
        m = spatial_utils.morton_encode(0, 0, 0)
        assert m == 0

    def test_unique(self):
        """Different coordinates produce different Morton codes."""
        codes = set()

        for x in range(0, 100, 10):
            for y in range(0, 100, 10):
                for z in range(0, 100, 10):
                    code = spatial_utils.morton_encode(x, y, z)
                    codes.add(code)

        # Should have 10*10*10 = 1000 unique codes
        assert len(codes) == 1000

    def test_bit_interleaving(self):
        """Morton encoding correctly interleaves bits."""
        # Simple test: x=1, y=0, z=0 should give 0b001 (x bit in lowest position)
        m = spatial_utils.morton_encode(1, 0, 0)
        assert m & 0b111 == 0b001  # x in position 0

        # y=1 should give 0b010 (y bit in position 1)
        m = spatial_utils.morton_encode(0, 1, 0)
        assert m & 0b111 == 0b010

        # z=1 should give 0b100 (z bit in position 2)
        m = spatial_utils.morton_encode(0, 0, 1)
        assert m & 0b111 == 0b100


class TestLocateAtom:
    """Test complete atom location (x, y, z, m)."""

    def test_returns_four_values(self):
        """locate_atom returns (x, y, z, m) tuple."""
        value = b"test"
        result = spatial_utils.locate_atom(value)

        assert isinstance(result, tuple)
        assert len(result) == 4

        x, y, z, m = result
        assert isinstance(x, float)
        assert isinstance(y, float)
        assert isinstance(z, float)
        assert isinstance(m, int)

    def test_deterministic(self):
        """Same value always produces same (x, y, z, m)."""
        value = b"deterministic_test"

        result1 = spatial_utils.locate_atom(value)
        result2 = spatial_utils.locate_atom(value)
        result3 = spatial_utils.locate_atom(value)

        assert result1 == result2 == result3

    def test_m_matches_hilbert(self):
        """M coordinate matches Hilbert index of (x, y, z)."""
        value = b"consistency_test"

        x, y, z, m = spatial_utils.locate_atom(value)

        # Recompute Hilbert from coordinates
        m_recomputed = spatial_utils.compute_hilbert_index(x, y, z)

        assert m == m_recomputed


class TestCompositionHelpers:
    """Test composition coordinate calculation."""

    def test_midpoint_empty_raises(self):
        """Midpoint of empty list raises ValueError."""
        with pytest.raises(ValueError, match="empty"):
            spatial_utils.locate_composition_midpoint([])

    def test_midpoint_single(self):
        """Midpoint of single coordinate equals that coordinate."""
        child_coords = [(100.0, 200.0, 300.0)]

        x, y, z, m = spatial_utils.locate_composition_midpoint(child_coords)

        assert x == 100.0
        assert y == 200.0
        assert z == 300.0
        assert isinstance(m, int)

    def test_midpoint_two(self):
        """Midpoint of two coordinates is average."""
        child_coords = [(0.0, 0.0, 0.0), (100.0, 200.0, 300.0)]

        x, y, z, m = spatial_utils.locate_composition_midpoint(child_coords)

        assert x == 50.0
        assert y == 100.0
        assert z == 150.0

    def test_midpoint_deterministic(self):
        """Midpoint strategy is deterministic."""
        child_coords = [(10.0, 20.0, 30.0), (40.0, 50.0, 60.0), (70.0, 80.0, 90.0)]

        result1 = spatial_utils.locate_composition_midpoint(child_coords)
        result2 = spatial_utils.locate_composition_midpoint(child_coords)

        assert result1 == result2

    def test_concept_deterministic(self):
        """Concept strategy is deterministic."""
        child_ids = [1, 2, 3]

        result1 = spatial_utils.locate_composition_concept(child_ids)
        result2 = spatial_utils.locate_composition_concept(child_ids)

        assert result1 == result2

    def test_concept_order_invariant(self):
        """Concept strategy is order-invariant (sorted internally)."""
        result1 = spatial_utils.locate_composition_concept([1, 2, 3])
        result2 = spatial_utils.locate_composition_concept([3, 2, 1])
        result3 = spatial_utils.locate_composition_concept([2, 1, 3])

        assert result1 == result2 == result3

    def test_concept_unique(self):
        """Different child sets produce different concept coordinates."""
        coord1 = spatial_utils.locate_composition_concept([1, 2, 3])
        coord2 = spatial_utils.locate_composition_concept([4, 5, 6])
        coord3 = spatial_utils.locate_composition_concept([1, 2, 4])

        coords = [coord1, coord2, coord3]
        assert len(coords) == len(set(coords))


class TestMakePointZM:
    """Test PointZM SQL generation."""

    def test_basic_generation(self):
        """Generates correct SQL and params."""
        sql, params = spatial_utils.make_point_zm(1.0, 2.0, 3.0, 123456)

        assert "ST_MakePoint" in sql
        assert "%s" in sql  # Parameterized
        assert params == (1.0, 2.0, 3.0, 123456)

    def test_srid_zero_no_wrapper(self):
        """SRID=0 doesn't add ST_SetSRID wrapper."""
        sql, params = spatial_utils.make_point_zm(1.0, 2.0, 3.0, 123, srid=0)

        assert "ST_SetSRID" not in sql
        assert sql.count("ST_MakePoint") == 1

    def test_custom_srid(self):
        """Custom SRID adds ST_SetSRID wrapper."""
        sql, params = spatial_utils.make_point_zm(1.0, 2.0, 3.0, 123, srid=4326)

        assert "ST_SetSRID" in sql
        assert params == (1.0, 2.0, 3.0, 123, 4326)

    def test_validation_enabled(self):
        """Validation catches invalid coordinates."""
        with pytest.raises((TypeError, ValueError)):
            spatial_utils.make_point_zm(1e15, 2.0, 3.0, 123, validate=True)

    def test_validation_disabled(self):
        """Can skip validation if needed."""
        # Should not raise even with extreme values
        sql, params = spatial_utils.make_point_zm(1e15, 2.0, 3.0, 123, validate=False)
        assert "ST_MakePoint" in sql


class TestMakePoint3D:
    """Test 3D Point SQL generation (no M)."""

    def test_basic_generation(self):
        """Generates correct SQL for 3D point."""
        sql, params = spatial_utils.make_point_3d(1.0, 2.0, 3.0)

        assert "ST_MakePoint" in sql
        assert params == (1.0, 2.0, 3.0)
        assert len(params) == 3  # No M coordinate

    def test_srid_zero(self):
        """SRID=0 doesn't add wrapper."""
        sql, params = spatial_utils.make_point_3d(1.0, 2.0, 3.0, srid=0)

        assert "ST_SetSRID" not in sql

    def test_custom_srid(self):
        """Custom SRID adds wrapper."""
        sql, params = spatial_utils.make_point_3d(1.0, 2.0, 3.0, srid=4326)

        assert "ST_SetSRID" in sql
        assert params == (1.0, 2.0, 3.0, 4326)


class TestValidation:
    """Test coordinate validation."""

    def test_valid_coordinates(self):
        """Valid coordinates pass validation."""
        # Should not raise
        spatial_utils._validate_coordinates(0.0, 100.0, -500.0, 123456)

    def test_invalid_type(self):
        """Non-numeric types fail validation."""
        with pytest.raises(TypeError):
            spatial_utils._validate_coordinates(1.0, "invalid", 3.0)

    def test_extreme_values(self):
        """Extremely large values fail validation."""
        with pytest.raises(ValueError):
            spatial_utils._validate_coordinates(1e15, 2.0, 3.0)


if __name__ == "__main__":
    pytest.main([__file__, "-v"])
