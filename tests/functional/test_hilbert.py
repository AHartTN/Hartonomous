"""Test Hilbert encoding functionality."""

import pytest

from src.core.spatial.decode_hilbert_3d import decode_hilbert_3d
from src.core.spatial.encode_hilbert_3d import encode_hilbert_3d


@pytest.mark.functional
@pytest.mark.spatial
def test_hilbert_round_trip():
    """Functional test for Hilbert encoding round-trip."""
    # Test round-trip
    x, y, z = 0.5, 0.3, 0.8
    hilbert = encode_hilbert_3d(x, y, z, order=21)
    x2, y2, z2 = decode_hilbert_3d(hilbert, order=21)

    print(f"Original: ({x}, {y}, {z})")
    print(f"Hilbert index: {hilbert}")
    print(f"Decoded: ({x2:.6f}, {y2:.6f}, {z2:.6f})")
    print(f"Error: ({abs(x-x2):.9f}, {abs(y-y2):.9f}, {abs(z-z2):.9f})")

    # Order 21 gives ~2M discretization, so tolerance ~0.5e-6
    assert abs(x - x2) < 1e-5
    assert abs(y - y2) < 1e-5
    assert abs(z - z2) < 1e-5
