"""Compute Hilbert index ranges for spatial box query - PROPER IMPLEMENTATION."""

from typing import List, Tuple


def hilbert_box_query(
    x_min: float,
    x_max: float,
    y_min: float,
    y_max: float,
    z_min: float,
    z_max: float,
    order: int = 21,
    max_ranges: int = 64
) -> List[Tuple[int, int]]:
    """
    Compute Hilbert index ranges for 3D bounding box using octree subdivision.

    A 3D box maps to MULTIPLE disjoint Hilbert ranges, not a single continuous range.
    This function recursively subdivides the octree to find all ranges that intersect
    the query box.

    Args:
        x_min, x_max: X bounds in [0, 1]
        y_min, y_max: Y bounds in [0, 1]
        z_min, z_max: Z bounds in [0, 1]
        order: Hilbert curve order (bits per dimension)
        max_ranges: Maximum number of ranges to return

    Returns:
        List of (start, end) Hilbert index ranges
    """
    ranges = []

    # Stack: (level, hilbert_base, ox_min, ox_max, oy_min, oy_max, oz_min, oz_max)
    stack = [(0, 0, 0.0, 1.0, 0.0, 1.0, 0.0, 1.0)]

    while stack and len(ranges) < max_ranges:
        level, hilbert_base, ox_min, ox_max, oy_min, oy_max, oz_min, oz_max = stack.pop()

        # Check intersection with query box
        intersects = (
            ox_min <= x_max and ox_max >= x_min and
            oy_min <= y_max and oy_max >= y_min and
            oz_min <= z_max and oz_max >= z_min
        )

        if not intersects:
            continue  # Skip non-intersecting octants

        # Check if query box completely contains octant
        contained = (
            x_min <= ox_min and x_max >= ox_max and
            y_min <= oy_min and y_max >= oy_max and
            z_min <= oz_min and z_max >= oz_max
        )

        if contained or level >= order:
            # Emit range for this octant
            range_size = 1 << (3 * (order - level))
            ranges.append((hilbert_base, hilbert_base + range_size - 1))
        else:
            # Subdivide octant into 8 children
            x_mid = (ox_min + ox_max) / 2.0
            y_mid = (oy_min + oy_max) / 2.0
            z_mid = (oz_min + oz_max) / 2.0

            # Add children in Hilbert curve order (0-7 octants)
            for octant in range(8):
                child_base = (hilbert_base << 3) | octant

                child_x_min = ox_min if (octant & 4) == 0 else x_mid
                child_x_max = x_mid if (octant & 4) == 0 else ox_max
                child_y_min = oy_min if (octant & 2) == 0 else y_mid
                child_y_max = y_mid if (octant & 2) == 0 else oy_max
                child_z_min = oz_min if (octant & 1) == 0 else z_mid
                child_z_max = z_mid if (octant & 1) == 0 else oz_max

                stack.append((
                    level + 1,
                    child_base,
                    child_x_min, child_x_max,
                    child_y_min, child_y_max,
                    child_z_min, child_z_max
                ))

    # Merge adjacent ranges for efficiency
    if ranges:
        ranges.sort()
        merged = [ranges[0]]
        for start, end in ranges[1:]:
            if start <= merged[-1][1] + 1:
                # Merge with previous range
                merged[-1] = (merged[-1][0], max(merged[-1][1], end))
            else:
                merged.append((start, end))
        return merged

    return ranges
