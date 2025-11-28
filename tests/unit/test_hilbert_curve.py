"""Test Hilbert curve - ACTUAL SPATIAL PROPERTIES."""
import pytest
import numpy as np
from src.core.spatial.hilbert_curve import encode_hilbert_3d, decode_hilbert_3d

class TestHilbertSpatialProperties:
    """Test Hilbert curve preserves spatial locality."""
    
    def test_roundtrip_accuracy(self):
        """REAL TEST: Encode/decode recovers original coordinates."""
        test_points = [
            (0.0, 0.0, 0.0),
            (0.5, 0.5, 0.5),
            (1.0, 1.0, 1.0),
            (0.25, 0.75, 0.125),
        ]
        order = 21
        
        for x, y, z in test_points:
            hilbert_idx = encode_hilbert_3d(x, y, z, order)
            x2, y2, z2 = decode_hilbert_3d(hilbert_idx, order)
            
            # VERIFY: Coordinates recovered within precision
            error = max(abs(x - x2), abs(y - y2), abs(z - z2))
            assert error < 1e-6, f"Roundtrip error {error} too large for ({x},{y},{z})"
    
    def test_spatial_locality_preserved(self):
        """REAL TEST: Nearby points have nearby Hilbert indices."""
        order = 15
        
        # Test multiple nearby pairs
        test_cases = [
            ((0.5, 0.5, 0.5), (0.51, 0.51, 0.51)),  # Very close
            ((0.1, 0.1, 0.1), (0.11, 0.11, 0.11)),  # Close
            ((0.8, 0.2, 0.4), (0.81, 0.21, 0.41)),  # Close
        ]
        
        for (x1, y1, z1), (x2, y2, z2) in test_cases:
            idx1 = encode_hilbert_3d(x1, y1, z1, order)
            idx2 = encode_hilbert_3d(x2, y2, z2, order)
            
            # Far pair for comparison
            idx_far = encode_hilbert_3d(0.0, 0.0, 0.0, order)
            
            dist_close = abs(idx1 - idx2)
            dist_far = abs(idx1 - idx_far)
            
            # VERIFY: Close points have closer indices than far points
            assert dist_close < dist_far, f"Spatial locality violated: close={dist_close}, far={dist_far}"
    
    def test_uniform_distribution_coverage(self):
        """REAL TEST: Hilbert curve covers entire space uniformly."""
        order = 10
        n_samples = 100
        
        # Generate random points
        np.random.seed(42)
        points = np.random.rand(n_samples, 3)
        
        hilbert_indices = []
        for x, y, z in points:
            idx = encode_hilbert_3d(x, y, z, order)
            hilbert_indices.append(idx)
        
        # VERIFY: Indices span a wide range
        min_idx = min(hilbert_indices)
        max_idx = max(hilbert_indices)
        
        max_possible = (2 ** (3 * order)) - 1
        coverage = (max_idx - min_idx) / max_possible
        
        assert coverage > 0.1, f"Hilbert curve not covering space well: {coverage}"
    
    def test_deterministic_encoding(self):
        """REAL TEST: Same coordinates always give same index."""
        x, y, z = 0.333, 0.666, 0.999
        order = 21
        
        # Encode multiple times
        indices = [encode_hilbert_3d(x, y, z, order) for _ in range(5)]
        
        # VERIFY: All identical
        assert len(set(indices)) == 1, "Encoding must be deterministic"
    
    def test_corners_distinct(self):
        """REAL TEST: Corner points have distinct indices."""
        order = 15
        corners = [
            (0.0, 0.0, 0.0),
            (1.0, 0.0, 0.0),
            (0.0, 1.0, 0.0),
            (0.0, 0.0, 1.0),
            (1.0, 1.0, 0.0),
            (1.0, 0.0, 1.0),
            (0.0, 1.0, 1.0),
            (1.0, 1.0, 1.0),
        ]
        
        indices = [encode_hilbert_3d(x, y, z, order) for x, y, z in corners]
        
        # VERIFY: All corners have unique indices
        assert len(set(indices)) == 8, "All 8 corners should have unique Hilbert indices"
