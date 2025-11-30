"""
Spatial Utilities for Hartonomous Geometric Atomization

Centralized helpers for:
- Point creation (PointZM geometry)
- Coordinate normalization
- Hilbert/Morton encoding
- Hash-to-coordinate conversion
- Spatial query construction

This module eliminates code duplication between AtomLocator and FractalAtomizer
by providing canonical implementations of spatial operations.

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import struct
from typing import List, Tuple

# ============================================================================
# CONSTANTS
# ============================================================================

DEFAULT_COORDINATE_RANGE = 1e6  # PostGIS compatibility
DEFAULT_HILBERT_BITS = 21  # 63 bits total (BIGINT)
DEFAULT_SRID = 0  # Undefined/local coordinate system


# ============================================================================
# POINT CREATION
# ============================================================================


def make_point_zm(
    x: float,
    y: float,
    z: float,
    m: float,
    srid: int = DEFAULT_SRID,
    validate: bool = True,
) -> Tuple[str, Tuple]:
    """
    Create PostGIS PointZM geometry with safe parameterized query.

    Args:
        x, y, z: Spatial coordinates
        m: Measure coordinate (Hilbert index)
        srid: Spatial Reference System ID (default 0 = local)
        validate: Whether to validate coordinate ranges

    Returns:
        (sql_function, params) tuple for safe parameterized query

    Example:
        sql, params = make_point_zm(1.0, 2.0, 3.0, 1234567)
        # sql = "ST_MakePoint(%s, %s, %s, %s)"
        # params = (1.0, 2.0, 3.0, 1234567)
    """
    if validate:
        _validate_spatial_coordinates(x, y, z)
        _validate_m_coordinate(m)

    # SRID 0 = no need for ST_SetSRID wrapper
    if srid == 0:
        return "ST_MakePoint(%s, %s, %s, %s)", (x, y, z, m)
    else:
        return "ST_SetSRID(ST_MakePoint(%s, %s, %s, %s), %s)", (x, y, z, m, srid)


def make_point_3d(
    x: float, y: float, z: float, srid: int = DEFAULT_SRID, validate: bool = True
) -> Tuple[str, Tuple]:
    """
    Create PostGIS 3D Point (no M coordinate).

    Used for spatial distance queries where M is ignored.

    Args:
        x, y, z: Spatial coordinates
        srid: Spatial Reference System ID (default 0 = local)
        validate: Whether to validate coordinate ranges

    Returns:
        (sql_function, params) tuple for safe parameterized query
    """
    if validate:
        _validate_spatial_coordinates(x, y, z)

    if srid == 0:
        return "ST_MakePoint(%s, %s, %s)", (x, y, z)
    else:
        return "ST_SetSRID(ST_MakePoint(%s, %s, %s), %s)", (x, y, z, srid)


# ============================================================================
# COORDINATE CALCULATION
# ============================================================================


def hash_to_coordinate(
    value: bytes, coordinate_range: float = DEFAULT_COORDINATE_RANGE
) -> Tuple[float, float, float]:
    """
    Convert atom value to deterministic 3D coordinate via SHA-256.

    This is the CANONICAL hash→coordinate function used across Hartonomous.
    Same value ALWAYS produces same coordinate.

    Args:
        value: Atom value (raw bytes)
        coordinate_range: Max coordinate magnitude (default 1e6)

    Returns:
        (x, y, z) coordinate tuple in [-coordinate_range, +coordinate_range]
    """
    # Hash value
    hash_bytes = hashlib.sha256(value).digest()

    # Extract 24 bytes (8 per dimension)
    x_bytes = hash_bytes[0:8]
    y_bytes = hash_bytes[8:16]
    z_bytes = hash_bytes[16:24]

    # Convert to uint64
    x_int = struct.unpack("<Q", x_bytes)[0]
    y_int = struct.unpack("<Q", y_bytes)[0]
    z_int = struct.unpack("<Q", z_bytes)[0]

    # Normalize to [0, 1]
    max_uint64 = 2**64 - 1
    x_norm = x_int / max_uint64
    y_norm = y_int / max_uint64
    z_norm = z_int / max_uint64

    # Scale to [-coordinate_range, +coordinate_range]
    x = (x_norm * 2 - 1) * coordinate_range
    y = (y_norm * 2 - 1) * coordinate_range
    z = (z_norm * 2 - 1) * coordinate_range

    return (x, y, z)


def compute_hilbert_index(
    x: float,
    y: float,
    z: float,
    coordinate_range: float = DEFAULT_COORDINATE_RANGE,
    bits: int = DEFAULT_HILBERT_BITS,
) -> int:
    """
    Compute Hilbert curve index (M coordinate) from spatial coordinates.

    Uses Morton encoding as placeholder (TODO: True Hilbert curve).
    Preserves spatial locality for B-tree approximate nearest neighbor.

    Args:
        x, y, z: Spatial coordinates
        coordinate_range: Coordinate range for normalization
        bits: Bits per dimension (default 21 = 63 bits total)

    Returns:
        Hilbert index (fits in PostgreSQL BIGINT)
    """
    # Normalize coordinates to [0, 2^bits - 1]
    max_val = 2**bits - 1

    # Map from [-coordinate_range, +coordinate_range] to [0, max_val]
    x_int = int((x / coordinate_range + 1) / 2 * max_val)
    y_int = int((y / coordinate_range + 1) / 2 * max_val)
    z_int = int((z / coordinate_range + 1) / 2 * max_val)

    # Clamp to valid range
    x_int = max(0, min(max_val, x_int))
    y_int = max(0, min(max_val, y_int))
    z_int = max(0, min(max_val, z_int))

    # Encode via Morton curve (Hilbert placeholder)
    return morton_encode(x_int, y_int, z_int)


def morton_encode(x: int, y: int, z: int) -> int:
    """
    3D Morton (Z-order) curve encoding.

    Interleaves bits: z2y2x2 z1y1x1 z0y0x0
    Preserves spatial locality for B-tree indexing.

    Args:
        x, y, z: Integer coordinates (21 bits each)

    Returns:
        Morton code (63 bits total)
    """

    def split_by_3(value: int) -> int:
        """Spread bits to every 3rd position."""
        value &= 0x1FFFFF  # Keep only 21 bits
        value = (value | value << 32) & 0x1F00000000FFFF
        value = (value | value << 16) & 0x1F0000FF0000FF
        value = (value | value << 8) & 0x100F00F00F00F00F
        value = (value | value << 4) & 0x10C30C30C30C30C3
        value = (value | value << 2) & 0x1249249249249249
        return value

    return split_by_3(x) | (split_by_3(y) << 1) | (split_by_3(z) << 2)


# ============================================================================
# HIGH-LEVEL API (Combines hash + Hilbert)
# ============================================================================


def locate_atom(
    value: bytes, coordinate_range: float = DEFAULT_COORDINATE_RANGE
) -> Tuple[float, float, float, int]:
    """
    Complete atom location: compute (x, y, z, m) from value.

    This is the CANONICAL location function for Hartonomous atoms.

    Args:
        value: Atom value (raw bytes)
        coordinate_range: Coordinate range

    Returns:
        (x, y, z, m) tuple - ready for PointZM creation
    """
    x, y, z = hash_to_coordinate(value, coordinate_range)
    m = compute_hilbert_index(x, y, z, coordinate_range)
    return (x, y, z, m)


# ============================================================================
# COMPOSITION HELPERS
# ============================================================================


def locate_composition_midpoint(
    child_coords: List[Tuple[float, float, float]],
    coordinate_range: float = DEFAULT_COORDINATE_RANGE,
) -> Tuple[float, float, float, int]:
    """
    Compute composition coordinate via midpoint averaging.

    Args:
        child_coords: List of (x, y, z) child atom coordinates
        coordinate_range: Coordinate range

    Returns:
        (x, y, z, m) tuple for composition atom
    """
    if not child_coords:
        raise ValueError("Cannot compute midpoint of empty list")

    # Compute centroid
    x = sum(c[0] for c in child_coords) / len(child_coords)
    y = sum(c[1] for c in child_coords) / len(child_coords)
    z = sum(c[2] for c in child_coords) / len(child_coords)

    # Compute Hilbert index
    m = compute_hilbert_index(x, y, z, coordinate_range)

    return (x, y, z, m)


def locate_composition_concept(
    child_ids: List[int], coordinate_range: float = DEFAULT_COORDINATE_RANGE
) -> Tuple[float, float, float, int]:
    """
    Compute composition coordinate via concept hashing.

    Independent of child coordinates - represents abstract concept.
    
    Order matters: [1,2] != [2,1] (compositions are ordered sequences).

    Args:
        child_ids: List of child atom IDs (order preserved)
        coordinate_range: Coordinate range

    Returns:
        (x, y, z, m) tuple for composition atom
    """
    # Hash the composition structure (preserve order)
    composition_bytes = b"".join(
        child_id.to_bytes(8, byteorder="little", signed=True)
        for child_id in child_ids  # ORDER MATTERS
    )

    return locate_atom(composition_bytes, coordinate_range)


# ============================================================================
# VALIDATION
# ============================================================================


def _validate_spatial_coordinates(*coords: float) -> None:
    """Validate spatial coordinates (X, Y, Z) are finite numbers in reasonable range."""
    for coord in coords:
        if not isinstance(coord, (int, float)):
            raise TypeError(f"Coordinate must be numeric, got {type(coord)}")
        if not (-1e10 < coord < 1e10):
            raise ValueError(f"Coordinate {coord} out of valid range")


def _validate_m_coordinate(m: float) -> None:
    """Validate M coordinate (Hilbert index) is valid BIGINT."""
    if not isinstance(m, (int, float)):
        raise TypeError(f"M coordinate must be numeric, got {type(m)}")
    # PostgreSQL BIGINT range: -2^63 to 2^63-1
    if not (-9223372036854775808 <= m <= 9223372036854775807):
        raise ValueError(f"M coordinate {m} out of BIGINT range")
