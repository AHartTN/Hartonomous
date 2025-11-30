"""Test Hilbert curve - ACTUAL SPATIAL PROPERTIES."""

import numpy as np
import pytest

from src.core.spatial.hilbert_curve import decode_hilbert_3d, encode_hilbert_3d


@pytest.mark.unit
@pytest.mark.spatial
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

    def test_spatial_locality_same_region(self):
        """REAL TEST: Points in same small region have relatively close indices."""
        order = 15

        # Test points within a small cube
        base_point = (0.5, 0.5, 0.5)
        base_idx = encode_hilbert_3d(*base_point, order)

        # Very nearby points (within 0.01 distance)
        nearby_points = [
            (0.501, 0.501, 0.501),
            (0.502, 0.502, 0.502),
            (0.505, 0.505, 0.505),
        ]

        nearby_indices = [
            encode_hilbert_3d(x, y, z, order) for x, y, z in nearby_points
        ]

        # Compute average distance for nearby points
        avg_nearby_dist = np.mean([abs(base_idx - idx) for idx in nearby_indices])

        # Far point (opposite corner)
        far_point = (0.0, 0.0, 0.0)
        far_idx = encode_hilbert_3d(*far_point, order)
        far_dist = abs(base_idx - far_idx)

        # VERIFY: Average nearby distance is much less than far distance
        # Allow for Hilbert curve discontinuities
        assert (
            avg_nearby_dist < far_dist * 0.5
        ), f"Spatial locality: avg_nearby={avg_nearby_dist}, far={far_dist}"

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
        assert (
            len(set(indices)) == 8
        ), "All 8 corners should have unique Hilbert indices"
