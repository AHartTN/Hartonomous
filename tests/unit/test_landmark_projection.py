"""Test landmark projection - ACTUAL SEMANTIC POSITIONING."""

import pytest

from src.core.spatial.landmark_projection import (compute_distance,
                                                  compute_position)


@pytest.mark.unit
@pytest.mark.spatial
class TestLandmarkSemanticPositioning:
    """Test landmark projection creates semantic spatial positions."""

    def test_same_modality_clusters(self):
        """REAL TEST: Same modality atoms cluster in nearby positions."""
        # Multiple code atoms
        pos1 = compute_position("code", "function", "concrete", "func1")
        pos2 = compute_position("code", "function", "concrete", "func2")
        pos3 = compute_position("code", "class", "concrete", "class1")

        # Different modality
        pos_image = compute_position("image", "pixel", "literal", "pixel1")

        # VERIFY: Code atoms closer to each other than to image
        dist_code = compute_distance(pos1[:3], pos2[:3])
        dist_code_image = compute_distance(pos1[:3], pos_image[:3])

        assert dist_code < dist_code_image, "Same modality should cluster together"

    def test_category_influences_position(self):
        """REAL TEST: Different categories within same modality separate."""
        pos_func = compute_position("code", "function", "concrete", "test")
        pos_class = compute_position("code", "class", "concrete", "test")
        pos_var = compute_position("code", "variable", "concrete", "test")

        # VERIFY: All different
        assert pos_func != pos_class
        assert pos_class != pos_var
        assert pos_func != pos_var

        # VERIFY: Categories create spatial separation
        dist_func_class = compute_distance(pos_func[:3], pos_class[:3])
        assert dist_func_class > 0, "Different categories should have non-zero distance"

    def test_deterministic_positioning(self):
        """REAL TEST: Same input always gives same position."""
        positions = []
        for _ in range(10):
            pos = compute_position("text", "word", "concrete", "hello")
            positions.append(pos)

        # VERIFY: All identical
        assert all(
            p == positions[0] for p in positions
        ), "Positioning must be deterministic"

    def test_identifier_perturbs_position(self):
        """REAL TEST: Identifier hash creates slight variation."""
        pos1 = compute_position("code", "function", "concrete", "calculate_sum")
        pos2 = compute_position("code", "function", "concrete", "calculate_diff")

        # VERIFY: Different positions
        assert pos1 != pos2, "Different identifiers should give different positions"

        # VERIFY: But still nearby (same category/specificity)
        dist = compute_distance(pos1[:3], pos2[:3])
        assert dist < 0.2, f"Similar atoms should be nearby, got distance {dist}"

    def test_hilbert_index_valid(self):
        """REAL TEST: Hilbert index is within valid range."""
        _, _, _, hilbert = compute_position("code", "function", "concrete", "test")

        # VERIFY: Positive integer
        assert isinstance(hilbert, int)
        assert hilbert >= 0

        # VERIFY: Within range for order=21
        max_index = (2 ** (3 * 21)) - 1
        assert hilbert <= max_index

    def test_coordinates_in_unit_cube(self):
        """REAL TEST: All coordinates in [0,1] range."""
        test_cases = [
            ("code", "function", "concrete", "test"),
            ("image", "pixel", "literal", "rgb"),
            ("text", "character", "literal", "a"),
            ("model", "weight", "instance", "w123"),
        ]

        for modality, category, specificity, identifier in test_cases:
            x, y, z, _ = compute_position(modality, category, specificity, identifier)

            # VERIFY: Coordinates in valid range
            assert 0 <= x <= 1, f"x={x} out of range [0,1]"
            assert 0 <= y <= 1, f"y={y} out of range [0,1]"
            assert 0 <= z <= 1, f"z={z} out of range [0,1]"

