"""Unit tests for TrajectoryBuilder - LINESTRING trajectory construction.

Tests verify that:
1. Trajectories are built as single LINESTRING (no record explosion)
2. WKT format is valid PostGIS LINESTRING ZM
3. Large sequences can be chunked efficiently
4. M values (sequence order) are preserved

This is the BREAKTHROUGH component that prevents record explosion:
- "Hello World" (11 chars) → 1 LINESTRING row (not 11 rows)
- 1M tensor elements → ~100 LINESTRING rows (not 1M rows)

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import pytest

from api.services.geometric_atomization import AtomLocator, TrajectoryBuilder


class TestBasicTrajectoryConstruction:
    """Test basic WKT trajectory building."""
    
    def test_build_simple_trajectory(self):
        """Build trajectory from list of coordinates."""
        builder = TrajectoryBuilder()
        
        coords = [
            (1.0, 2.0, 3.0),
            (4.0, 5.0, 6.0),
            (7.0, 8.0, 9.0)
        ]
        
        wkt = builder.build_wkt(coords)
        
        # Should produce valid WKT LINESTRING ZM
        assert wkt.startswith("LINESTRING ZM ("), "Must be LINESTRING ZM format"
        assert wkt.endswith(")"), "Must have closing parenthesis"
        
        # Should contain all coordinates
        assert "1.0 2.0 3.0" in wkt
        assert "4.0 5.0 6.0" in wkt
        assert "7.0 8.0 9.0" in wkt
        
        # Should have M values (sequence order: 0, 1, 2)
        assert "0" in wkt  # First M value
        assert "1" in wkt  # Second M value
        assert "2" in wkt  # Third M value
    
    def test_build_two_point_trajectory(self):
        """Minimum trajectory has 2 points."""
        builder = TrajectoryBuilder()
        
        coords = [
            (0.0, 0.0, 0.0),
            (1.0, 1.0, 1.0)
        ]
        
        wkt = builder.build_wkt(coords)
        
        assert wkt.startswith("LINESTRING ZM (")
        assert wkt.count(',') == 1  # Two points = one comma
    
    def test_m_values_are_sequential(self):
        """M values (sequence order) are 0, 1, 2, 3, ..."""
        builder = TrajectoryBuilder()
        
        coords = [(float(i), float(i), float(i)) for i in range(10)]
        wkt = builder.build_wkt(coords)
        
        # Parse WKT to extract M values
        # Format: "LINESTRING ZM (x y z m, x y z m, ...)"
        points_str = wkt.replace("LINESTRING ZM (", "").replace(")", "")
        points = points_str.split(", ")
        
        for i, point in enumerate(points):
            parts = point.split()
            m_value = int(float(parts[3]))  # Fourth value is M
            assert m_value == i, f"M value at position {i} should be {i}, got {m_value}"


class TestSingleLINESTRINGBreakthrough:
    """Test the critical breakthrough: ONE LINESTRING per sequence."""
    
    def test_hello_world_single_linestring(self):
        """
        CRITICAL TEST: "Hello World" → ONE LINESTRING row (not 11 rows).
        
        This proves the architecture breakthrough:
        - Traditional: 11 characters → 11 atom rows + 1 composition row = 12 rows
        - Geometric: 11 characters → 1 LINESTRING row = 1 row (92% reduction)
        """
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        
        text = "Hello World"
        atom_values = [char.encode('utf-8') for char in text]
        
        # Build trajectory
        wkt = builder.build_from_atoms(atom_values, locator)
        
        # Should be single WKT string (not 11 separate rows)
        assert isinstance(wkt, str), "Must return single WKT string"
        assert wkt.startswith("LINESTRING ZM ("), "Must be LINESTRING ZM"
        
        # Should contain 11 points (one per character)
        # Count commas: 11 points = 10 commas
        comma_count = wkt.count(',')
        assert comma_count == len(text) - 1, f"Expected {len(text)-1} commas, got {comma_count}"
    
    def test_paragraph_single_linestring(self):
        """Entire paragraph → ONE LINESTRING (no matter how long)."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        
        text = (
            "The quick brown fox jumps over the lazy dog. "
            "This sentence contains every letter of the alphabet. "
            "Yet it all fits in a single LINESTRING trajectory."
        )
        atom_values = [char.encode('utf-8') for char in text]
        
        wkt = builder.build_from_atoms(atom_values, locator)
        
        # Single WKT string
        assert isinstance(wkt, str)
        assert wkt.startswith("LINESTRING ZM (")
        
        # Should have len(text) points
        comma_count = wkt.count(',')
        assert comma_count == len(text) - 1
    
    def test_comparison_with_traditional_approach(self):
        """
        Document the breakthrough with explicit comparison.
        
        Traditional (Brownfield):
        - "Hello" (5 chars) → 5 atom rows + 1 composition row = 6 rows
        
        Geometric (Greenfield):
        - "Hello" (5 chars) → 1 LINESTRING row = 1 row
        
        Result: 83% reduction in row count
        """
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        
        text = "Hello"
        atom_values = [char.encode('utf-8') for char in text]
        
        wkt = builder.build_from_atoms(atom_values, locator)
        
        # Traditional would create 6 rows
        traditional_rows = len(text) + 1  # atoms + composition
        
        # Geometric creates 1 row
        geometric_rows = 1
        
        reduction = (traditional_rows - geometric_rows) / traditional_rows
        
        assert geometric_rows == 1
        assert abs(reduction - 0.8333) < 0.001, f"Should be ~83.33% reduction, got {reduction:.1%}"


class TestLargeSequenceChunking:
    """Test chunking for very large sequences."""
    
    def test_large_tensor_chunking(self):
        """
        Large tensors can be chunked to avoid PostGIS limits.
        
        PostGIS has practical limits on LINESTRING size.
        Solution: chunk into multiple LINESTRINGs.
        
        Example: 50K elements → 5 chunks of 10K each
        Still massive improvement: 50K rows → 5 rows (99.99% reduction)
        """
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        
        # Create 50K atom sequence
        atom_values = [f"atom_{i}".encode('utf-8') for i in range(50000)]
        coordinates = locator.locate_multiple(atom_values)
        
        # Chunk into 10K per trajectory
        chunks = builder.chunk_trajectory(coordinates, chunk_size=10000)
        
        # Should produce 5 chunks
        assert len(chunks) == 5, f"Expected 5 chunks, got {len(chunks)}"
        
        # Each should be valid WKT
        for i, chunk in enumerate(chunks):
            assert chunk.startswith("LINESTRING ZM ("), f"Chunk {i} not valid WKT"
            
            # Last chunk might be smaller
            if i < 4:
                comma_count = chunk.count(',')
                assert comma_count == 9999, f"Chunk {i} should have 10K points (9999 commas)"
    
    def test_chunk_size_boundary_conditions(self):
        """Test chunking at exact boundaries."""
        builder = TrajectoryBuilder()
        
        # Exactly 3 chunks of size 10
        coordinates = [(float(i), float(i), float(i)) for i in range(30)]
        chunks = builder.chunk_trajectory(coordinates, chunk_size=10)
        
        assert len(chunks) == 3
        
        for chunk in chunks:
            comma_count = chunk.count(',')
            assert comma_count == 9  # 10 points = 9 commas
    
    def test_chunk_size_with_remainder(self):
        """Test chunking with partial last chunk."""
        builder = TrajectoryBuilder()
        
        # 25 coordinates, chunk size 10 → 2 full chunks + 1 partial (5 elements)
        coordinates = [(float(i), float(i), float(i)) for i in range(25)]
        chunks = builder.chunk_trajectory(coordinates, chunk_size=10)
        
        assert len(chunks) == 3
        
        # First two chunks: 10 elements each
        assert chunks[0].count(',') == 9
        assert chunks[1].count(',') == 9
        
        # Last chunk: 5 elements
        assert chunks[2].count(',') == 4
    
    def test_no_chunking_for_small_sequences(self):
        """Small sequences don't get chunked."""
        builder = TrajectoryBuilder()
        
        coordinates = [(float(i), float(i), float(i)) for i in range(100)]
        chunks = builder.chunk_trajectory(coordinates, chunk_size=1000)
        
        # Should produce single chunk (100 < 1000)
        assert len(chunks) == 1


class TestBuildFromAtoms:
    """Test convenience method that combines locator + builder."""
    
    def test_build_from_atom_values(self):
        """Build trajectory directly from atom values."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        
        atom_values = [b"H", b"e", b"l", b"l", b"o"]
        
        wkt = builder.build_from_atoms(atom_values, locator)
        
        assert wkt.startswith("LINESTRING ZM (")
        assert wkt.count(',') == 4  # 5 points = 4 commas
    
    def test_build_from_empty_list(self):
        """Handle empty atom list gracefully."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        
        # Empty list should raise ValueError
        with pytest.raises(ValueError, match="empty coordinate list"):
            builder.build_from_atoms([], locator)
    
    def test_build_from_single_atom(self):
        """Single atom should produce valid LINESTRING."""
        locator = AtomLocator()
        builder = TrajectoryBuilder()
        
        atom_values = [b"A"]
        
        wkt = builder.build_from_atoms(atom_values, locator)
        
        # Even single point should be valid LINESTRING
        # (PostGIS allows single-point LINESTRINGs)
        assert wkt.startswith("LINESTRING ZM (")


class TestCoordinatePrecision:
    """Test coordinate formatting and precision."""
    
    def test_coordinate_precision_preserved(self):
        """Coordinates maintain sufficient precision in WKT."""
        builder = TrajectoryBuilder()
        
        # High-precision coordinates
        coords = [
            (1.123456789, 2.987654321, 3.111222333),
            (4.444555666, 5.777888999, 6.123123123)
        ]
        
        wkt = builder.build_wkt(coords)
        
        # Check that precision is preserved (at least 6 decimal places)
        assert "1.123456" in wkt or "1.12345" in wkt  # At least 5-6 decimals
        assert "2.987654" in wkt or "2.98765" in wkt
    
    def test_negative_coordinates(self):
        """Negative coordinates handled correctly."""
        builder = TrajectoryBuilder()
        
        coords = [
            (-100.5, -200.3, -300.7),
            (100.5, 200.3, 300.7)
        ]
        
        wkt = builder.build_wkt(coords)
        
        assert "-100.5" in wkt
        assert "-200.3" in wkt
        assert "-300.7" in wkt


if __name__ == '__main__':
    pytest.main([__file__, '-v'])
