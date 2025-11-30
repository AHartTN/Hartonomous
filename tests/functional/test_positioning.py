"""Test spatial landmark positioning."""

import pytest
from src.core.spatial.compute_position import compute_position
from src.core.spatial.compute_distance import compute_distance


@pytest.mark.functional
@pytest.mark.spatial
def test_spatial_positioning():
    """Functional test for spatial landmark positioning."""
    # Test code positioning
    x, y, z, hilbert = compute_position(
        modality="code",
        category="function",
        specificity="concrete",
        identifier="my_function",
    )

    print(f"Position for code function: ({x:.3f}, {y:.3f}, {z:.3f})")
    print(f"Hilbert index: {hilbert}")

    # Test image positioning
    x2, y2, z2, hilbert2 = compute_position(
        modality="image", category="literal", specificity="literal", identifier="pixel_data"
    )

    print(f"Position for image literal: ({x2:.3f}, {y2:.3f}, {z2:.3f})")
    print(f"Hilbert index: {hilbert2}")

    # Verify positions are different
    dist = compute_distance((x, y, z), (x2, y2, z2))
    print(f"Distance between them: {dist:.3f}")
    
    assert dist > 0, "Different modalities should have different positions"
    assert 0 <= x <= 1 and 0 <= y <= 1 and 0 <= z <= 1
    assert 0 <= x2 <= 1 and 0 <= y2 <= 1 and 0 <= z2 <= 1

