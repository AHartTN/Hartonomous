"""Unit tests for AtomLocator - deterministic coordinate mapping.

Tests verify that:
1. Same atom value always produces same coordinate (determinism)
2. Different atoms produce different coordinates (uniqueness)
3. Coordinates stay within valid range
4. Hilbert indices are valid POSTgreSQL BIGINTs

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import pytest
from api.services.geometric_atomization import AtomLocator


class TestCoordinateDeterminism:
    """Test that coordinates are deterministic and reproducible."""
    
    def test_same_value_same_coordinate(self):
        """Same atom value always produces identical coordinate."""
        locator = AtomLocator()
        
        value = b"Hello"
        
        # Call locate() multiple times
        coord1 = locator.locate(value)
        coord2 = locator.locate(value)
        coord3 = locator.locate(value)
        
        # Should be identical (deterministic)
        assert coord1 == coord2 == coord3, "Coordinates must be deterministic"
    
    def test_different_values_different_coordinates(self):
        """Different atom values produce unique coordinates."""
        locator = AtomLocator()
        
        # Test with "Hello" characters
        coord_h = locator.locate(b"H")
        coord_e = locator.locate(b"e")
        coord_l = locator.locate(b"l")
        coord_o = locator.locate(b"o")
        
        # All should be unique
        coords = [coord_h, coord_e, coord_l, coord_o]
        assert len(coords) == len(set(coords)), "Different atoms must map to different coordinates"
    
    def test_consistency_across_instances(self):
        """Multiple AtomLocator instances produce same coordinates."""
        value = b"test_value"
        
        locator1 = AtomLocator()
        locator2 = AtomLocator()
        
        coord1 = locator1.locate(value)
        coord2 = locator2.locate(value)
        
        assert coord1 == coord2, "Different instances must produce same coordinates"


class TestCoordinateRanges:
    """Test that coordinates stay within valid bounds."""
    
    def test_default_coordinate_range(self):
        """Coordinates stay within default range."""
        locator = AtomLocator()  # default coordinate_range = 1e6
        
        # Test many random values
        for i in range(100):
            value = f"test_{i}".encode('utf-8')
            x, y, z = locator.locate(value)
            
            # Check range: [-1e6, +1e6]
            assert -1e6 <= x <= 1e6, f"X coordinate {x} out of range"
            assert -1e6 <= y <= 1e6, f"Y coordinate {y} out of range"
            assert -1e6 <= z <= 1e6, f"Z coordinate {z} out of range"
    
    def test_custom_coordinate_range(self):
        """Coordinates stay within custom range."""
        custom_range = 1e9
        locator = AtomLocator(coordinate_range=custom_range)
        
        for i in range(50):
            value = f"custom_{i}".encode('utf-8')
            x, y, z = locator.locate(value)
            
            assert -custom_range <= x <= custom_range
            assert -custom_range <= y <= custom_range
            assert -custom_range <= z <= custom_range
    
    def test_small_coordinate_range(self):
        """Coordinates stay within small range."""
        small_range = 100.0
        locator = AtomLocator(coordinate_range=small_range)
        
        for i in range(20):
            value = f"small_{i}".encode('utf-8')
            x, y, z = locator.locate(value)
            
            assert -small_range <= x <= small_range
            assert -small_range <= y <= small_range
            assert -small_range <= z <= small_range


class TestHilbertIndexing:
    """Test Hilbert curve index computation."""
    
    def test_hilbert_index_valid_bigint(self):
        """Hilbert index produces valid PostgreSQL BIGINT."""
        locator = AtomLocator()
        
        x, y, z = 0.0, 0.0, 0.0
        m = locator.compute_hilbert_index(x, y, z)
        
        # Should be non-negative integer
        assert isinstance(m, int), "Hilbert index must be integer"
        assert m >= 0, "Hilbert index must be non-negative"
        
        # Should fit in PostgreSQL BIGINT (63 bits signed)
        max_bigint = 2**63 - 1
        assert m <= max_bigint, f"Hilbert index {m} exceeds BIGINT max"
    
    def test_hilbert_index_for_various_coordinates(self):
        """Hilbert indices are valid for various coordinate values."""
        locator = AtomLocator()
        
        test_coords = [
            (0.0, 0.0, 0.0),
            (1.0, 1.0, 1.0),
            (-1.0, -1.0, -1.0),
            (500.0, -300.0, 800.0),
            (-999999.0, 999999.0, 0.0),
        ]
        
        max_bigint = 2**63 - 1
        
        for x, y, z in test_coords:
            m = locator.compute_hilbert_index(x, y, z)
            assert isinstance(m, int)
            assert 0 <= m <= max_bigint
    
    def test_hilbert_index_deterministic(self):
        """Same coordinates always produce same Hilbert index."""
        locator = AtomLocator()
        
        x, y, z = 123.456, -789.012, 345.678
        
        m1 = locator.compute_hilbert_index(x, y, z)
        m2 = locator.compute_hilbert_index(x, y, z)
        m3 = locator.compute_hilbert_index(x, y, z)
        
        assert m1 == m2 == m3, "Hilbert index must be deterministic"


class TestBatchOperations:
    """Test batch coordinate location for performance."""
    
    def test_locate_multiple(self):
        """Batch locate() operates correctly."""
        locator = AtomLocator()
        
        values = [b"H", b"e", b"l", b"l", b"o"]
        coords = locator.locate_multiple(values)
        
        # Should return list of tuples
        assert len(coords) == len(values)
        assert all(isinstance(c, tuple) for c in coords)
        assert all(len(c) == 3 for c in coords)  # (x, y, z)
    
    def test_locate_multiple_determinism(self):
        """Batch locate() is deterministic."""
        locator = AtomLocator()
        
        values = [f"atom_{i}".encode('utf-8') for i in range(10)]
        
        coords1 = locator.locate_multiple(values)
        coords2 = locator.locate_multiple(values)
        
        assert coords1 == coords2


class TestEdgeCases:
    """Test edge cases and boundary conditions."""
    
    def test_empty_value(self):
        """Locate empty byte string."""
        locator = AtomLocator()
        
        coord = locator.locate(b"")
        
        # Should still produce valid coordinate
        assert isinstance(coord, tuple)
        assert len(coord) == 3
        x, y, z = coord
        assert -1e6 <= x <= 1e6
        assert -1e6 <= y <= 1e6
        assert -1e6 <= z <= 1e6
    
    def test_single_byte_value(self):
        """Locate single byte."""
        locator = AtomLocator()
        
        coord = locator.locate(b"\x00")
        
        assert isinstance(coord, tuple)
        assert len(coord) == 3
    
    def test_large_byte_value(self):
        """Locate large byte string (simulating 64-byte atom limit)."""
        locator = AtomLocator()
        
        # 64 bytes (atom size limit)
        value = b"A" * 64
        coord = locator.locate(value)
        
        assert isinstance(coord, tuple)
        assert len(coord) == 3
    
    def test_unicode_encoded_values(self):
        """Locate Unicode strings encoded as UTF-8."""
        locator = AtomLocator()
        
        # Various Unicode characters
        values = ["Hello", "世界", "🚀", "Привет", "مرحبا"]
        
        for text in values:
            value = text.encode('utf-8')
            coord = locator.locate(value)
            
            assert isinstance(coord, tuple)
            assert len(coord) == 3


if __name__ == '__main__':
    pytest.main([__file__, '-v'])
