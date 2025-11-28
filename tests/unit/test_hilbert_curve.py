"""Test Hilbert curve encoding/decoding."""
import pytest
import numpy as np
from src.core.spatial.hilbert_curve import encode_hilbert_3d, decode_hilbert_3d, hilbert_box_query, compute_hilbert_distance

class TestHilbertCurve:
    """Test Hilbert curve functions."""
    
    def test_encode_decode_roundtrip(self):
        """Test encoding and decoding returns close to original."""
        x, y, z = 0.5, 0.3, 0.8
        order = 21
        
        hilbert_index = encode_hilbert_3d(x, y, z, order)
        x2, y2, z2 = decode_hilbert_3d(hilbert_index, order)
        
        # Should be very close (within precision of order=21)
        assert abs(x - x2) < 0.000001
        assert abs(y - y2) < 0.000001
        assert abs(z - z2) < 0.000001
    
    def test_encode_corners(self):
        """Test encoding corner points."""
        order = 10
        
        # (0,0,0) corner
        idx_000 = encode_hilbert_3d(0.0, 0.0, 0.0, order)
        assert idx_000 >= 0
        
        # (1,1,1) corner
        idx_111 = encode_hilbert_3d(1.0, 1.0, 1.0, order)
        assert idx_111 >= 0
        
        # Different points should have different indices
        assert idx_000 != idx_111
    
    def test_spatial_locality(self):
        """Test that nearby points have nearby Hilbert indices."""
        order = 15
        
        # Two close points
        idx1 = encode_hilbert_3d(0.5, 0.5, 0.5, order)
        idx2 = encode_hilbert_3d(0.51, 0.51, 0.51, order)
        
        # Two far points
        idx3 = encode_hilbert_3d(0.5, 0.5, 0.5, order)
        idx4 = encode_hilbert_3d(0.9, 0.9, 0.9, order)
        
        dist_close = abs(idx1 - idx2)
        dist_far = abs(idx3 - idx4)
        
        # Close points should have closer indices than far points
        assert dist_close < dist_far
    
    def test_hilbert_box_query(self):
        """Test Hilbert box query range."""
        center_index = 1000000
        radius = 5000
        order = 21
        
        start, end = hilbert_box_query(center_index, radius, order)
        
        assert start < end
        assert start == center_index - radius
        assert end == center_index + radius
    
    def test_compute_hilbert_distance(self):
        """Test Hilbert distance computation."""
        idx1 = 100
        idx2 = 500
        
        distance = compute_hilbert_distance(idx1, idx2)
        
        assert distance == 400
        assert compute_hilbert_distance(idx2, idx1) == 400  # Symmetric
