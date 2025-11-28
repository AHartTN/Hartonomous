"""Compute Hilbert index range for spatial box query."""

from typing import Tuple


def hilbert_box_query(
    center_index: int,
    radius: int,
    order: int = 21
) -> Tuple[int, int]:
    """Compute Hilbert index range for approximate spatial box query."""
    max_index = (1 << (3 * order)) - 1
    
    start = max(0, center_index - radius)
    end = min(max_index, center_index + radius)
    
    return (start, end)
