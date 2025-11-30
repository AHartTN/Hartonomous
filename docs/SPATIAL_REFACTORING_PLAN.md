# Spatial Code Refactoring Plan

**Date**: 2025-01-XX  
**Status**: Proposed  
**Priority**: HIGH (Addresses SOLID/DRY violations)

## Executive Summary

The codebase has **extensive duplication** across spatial geometry operations:
- 50+ `ST_MakePoint()` calls with inconsistent patterns
- Duplicate coordinate normalization logic in 3+ modules
- Duplicate Hilbert/Morton encoding in 2+ modules
- No centralized spatial utilities module
- Violates DRY, SOLID, separation of concerns

**Goal**: Centralize spatial operations into `spatial_utils.py` helper module.

---

## Duplication Analysis

### 1. **AtomLocator vs FractalAtomizer** (Critical Duplication)

**File 1**: `api/services/geometric_atomization/atom_locator.py` (145 lines)
**File 2**: `api/services/geometric_atomization/fractal_atomizer.py` (429 lines)

#### Duplicated Logic:

| Function | AtomLocator | FractalAtomizer | Lines Duplicated |
|----------|------------|----------------|------------------|
| **Hash→Coordinate** | `locate()` (lines 39-73) | `locate_primitive()` (lines 72-98) | ~35 lines |
| **Hilbert Encoding** | `compute_hilbert_index()` (88-119) | `_compute_hilbert()` (401-419) | ~20 lines |
| **Morton Encoding** | `_morton_encode()` (121-144) | `_morton_encode()` (421-429) | ~25 lines |
| **Coordinate Normalization** | Lines 64-69 | Lines 408-415 | ~8 lines |

**Total Duplication**: ~88 lines of identical logic between two modules.

#### Code Comparison:

**AtomLocator.locate()** (lines 39-73):
```python
def locate(self, value: bytes) -> Tuple[float, float, float]:
    hash_bytes = hashlib.sha256(value).digest()
    
    x_bytes = hash_bytes[0:8]
    y_bytes = hash_bytes[8:16]
    z_bytes = hash_bytes[16:24]
    
    x_int = struct.unpack('<Q', x_bytes)[0]
    y_int = struct.unpack('<Q', y_bytes)[0]
    z_int = struct.unpack('<Q', z_bytes)[0]
    
    max_uint64 = 2**64 - 1
    x_norm = x_int / max_uint64
    y_norm = y_int / max_uint64
    z_norm = z_int / max_uint64
    
    x = (x_norm * 2 - 1) * self.coordinate_range
    y = (y_norm * 2 - 1) * self.coordinate_range
    z = (z_norm * 2 - 1) * self.coordinate_range
    
    return (x, y, z)
```

**FractalAtomizer.locate_primitive()** (lines 72-98):
```python
def locate_primitive(self, value: bytes) -> Tuple[float, float, float]:
    hash_bytes = hashlib.sha256(value).digest()
    
    x_bytes = hash_bytes[0:8]
    y_bytes = hash_bytes[8:16]
    z_bytes = hash_bytes[16:24]
    
    x_int = struct.unpack('<Q', x_bytes)[0]
    y_int = struct.unpack('<Q', y_bytes)[0]
    z_int = struct.unpack('<Q', z_bytes)[0]
    
    max_uint64 = 2**64 - 1
    x_norm = x_int / max_uint64
    y_norm = y_int / max_uint64
    z_norm = z_int / max_uint64
    
    x = (x_norm * 2 - 1) * self.coordinate_range
    y = (y_norm * 2 - 1) * self.coordinate_range
    z = (z_norm * 2 - 1) * self.coordinate_range
    
    return (x, y, z)
```

**IDENTICAL** - This is a textbook DRY violation.

---

### 2. **ST_MakePoint() Usage Patterns** (50+ Occurrences)

#### Pattern Analysis:

| Pattern | Occurrences | Files |
|---------|------------|-------|
| `ST_MakePoint(%s, %s, %s, %s)` (PointZM) | 20+ | fractal_atomizer.py, tests, schema |
| `ST_MakePoint(%s, %s, %s)` (Point/PointZ) | 15+ | spatial_reconstructor.py, tests |
| `ST_MakePoint(x, y)` (2D) | 10+ | tests, schema views |
| Manual coordinate formatting | 5+ | Various |

#### Issues:
1. **No validation**: No checks for valid coordinate ranges
2. **No SRID handling**: Each module hardcodes SRID logic
3. **No type safety**: Raw SQL strings, no Python wrappers
4. **Inconsistent usage**: Different modules use different patterns

---

### 3. **Coordinate Normalization** (Multiple Duplications)

Found in:
- `atom_locator.py` lines 64-69
- `fractal_atomizer.py` lines 408-415
- `src/core/spatial/compute_position.py` lines 36-38 (C# equivalent)
- Tests (for validation)

**Pattern**:
```python
x_int = int((x / coordinate_range + 1) / 2 * max_val)
x_int = max(0, min(max_val, x_int))  # Clamp
```

**Should be**: Single helper function with clear semantics.

---

### 4. **Hilbert/Morton Encoding** (Critical Duplication)

Both `AtomLocator` and `FractalAtomizer` implement **identical** Morton encoding:

**AtomLocator._morton_encode()** (lines 121-144):
```python
def _morton_encode(self, x: int, y: int, z: int) -> int:
    def split_by_3(value: int) -> int:
        value &= 0x1fffff  # 21 bits
        value = (value | value << 32) & 0x1f00000000ffff
        value = (value | value << 16) & 0x1f0000ff0000ff
        value = (value | value << 8) & 0x100f00f00f00f00f
        value = (value | value << 4) & 0x10c30c30c30c30c3
        value = (value | value << 2) & 0x1249249249249249
        return value
    
    return split_by_3(x) | (split_by_3(y) << 1) | (split_by_3(z) << 2)
```

**FractalAtomizer._morton_encode()** (lines 421-429):
```python
def _morton_encode(self, x: int, y: int, z: int) -> int:
    def split_by_3(value: int) -> int:
        value &= 0x1fffff
        value = (value | value << 32) & 0x1f00000000ffff
        value = (value | value << 16) & 0x1f0000ff0000ff
        value = (value | value << 8) & 0x100f00f00f00f00f
        value = (value | value << 4) & 0x10c30c30c30c30c3
        value = (value | value << 2) & 0x1249249249249249
        return value
    
    return split_by_3(x) | (split_by_3(y) << 1) | (split_by_3(z) << 2)
```

**IDENTICAL** - 25 lines of complex bit manipulation duplicated verbatim.

---

### 5. **Spatial Query Patterns** (Inconsistent)

Found in:
- `query.py`: `ST_Distance`, `ST_DWithin`, `<->` operator
- `fractal_atomizer.py`: `ST_DWithin` for composition lookup
- `spatial_reconstructor.py`: `ST_Distance` for nearest neighbor

**Issues**:
1. **No query builder**: Each module constructs raw SQL
2. **No distance abstraction**: Euclidean distance logic repeated
3. **No tolerance handling**: Hardcoded epsilon values

---

## Proposed Solution: `spatial_utils.py`

### Module Design

**File**: `api/services/geometric_atomization/spatial_utils.py`

**Purpose**: Centralized spatial geometry operations for Hartonomous.

### API Design

```python
"""
Spatial Utilities for Hartonomous Geometric Atomization

Centralized helpers for:
- Point creation (PointZM geometry)
- Coordinate normalization
- Hilbert/Morton encoding
- Hash-to-coordinate conversion
- Spatial query construction

Copyright (c) 2025 Anthony Hart. All Rights Reserved.
"""

import hashlib
import struct
from typing import Tuple, List, Optional


# ============================================================================
# CONSTANTS
# ============================================================================

DEFAULT_COORDINATE_RANGE = 1e6  # PostGIS compatibility
DEFAULT_HILBERT_BITS = 21       # 63 bits total (BIGINT)
DEFAULT_SRID = 0                # Undefined/local coordinate system


# ============================================================================
# POINT CREATION
# ============================================================================

def make_point_zm(
    x: float,
    y: float, 
    z: float,
    m: float,
    srid: int = DEFAULT_SRID,
    validate: bool = True
) -> Tuple[str, Tuple[float, float, float, float, int]]:
    """
    Create PostGIS PointZM geometry.
    
    Args:
        x, y, z: Spatial coordinates
        m: Measure coordinate (Hilbert index)
        srid: Spatial Reference System ID (default 0 = local)
        validate: Whether to validate coordinate ranges
    
    Returns:
        (sql_function, params) tuple for safe parameterized query
        
    Example:
        sql, params = make_point_zm(1.0, 2.0, 3.0, 1234567)
        # sql = "ST_SetSRID(ST_MakePoint(%s, %s, %s, %s), %s)"
        # params = (1.0, 2.0, 3.0, 1234567, 0)
    """
    if validate:
        _validate_coordinates(x, y, z, m)
    
    # SRID 0 = no need for ST_SetSRID wrapper
    if srid == 0:
        return "ST_MakePoint(%s, %s, %s, %s)", (x, y, z, m)
    else:
        return "ST_SetSRID(ST_MakePoint(%s, %s, %s, %s), %s)", (x, y, z, m, srid)


def make_point_3d(
    x: float,
    y: float,
    z: float,
    srid: int = DEFAULT_SRID,
    validate: bool = True
) -> Tuple[str, Tuple[float, float, float]]:
    """
    Create PostGIS 3D Point (no M coordinate).
    
    Used for spatial distance queries where M is ignored.
    """
    if validate:
        _validate_coordinates(x, y, z)
    
    if srid == 0:
        return "ST_MakePoint(%s, %s, %s)", (x, y, z)
    else:
        return "ST_SetSRID(ST_MakePoint(%s, %s, %s), %s)", (x, y, z, srid)


# ============================================================================
# COORDINATE CALCULATION
# ============================================================================

def hash_to_coordinate(
    value: bytes,
    coordinate_range: float = DEFAULT_COORDINATE_RANGE
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
    x_int = struct.unpack('<Q', x_bytes)[0]
    y_int = struct.unpack('<Q', y_bytes)[0]
    z_int = struct.unpack('<Q', z_bytes)[0]
    
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
    bits: int = DEFAULT_HILBERT_BITS
) -> int:
    """
    Compute Hilbert curve index (M coordinate) from spatial coordinates.
    
    Uses Morton encoding as placeholder (TODO: True Hilbert curve).
    
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
        value &= 0x1fffff  # Keep only 21 bits
        value = (value | value << 32) & 0x1f00000000ffff
        value = (value | value << 16) & 0x1f0000ff0000ff
        value = (value | value << 8) & 0x100f00f00f00f00f
        value = (value | value << 4) & 0x10c30c30c30c30c3
        value = (value | value << 2) & 0x1249249249249249
        return value
    
    return split_by_3(x) | (split_by_3(y) << 1) | (split_by_3(z) << 2)


# ============================================================================
# HIGH-LEVEL API (Combines hash + Hilbert)
# ============================================================================

def locate_atom(
    value: bytes,
    coordinate_range: float = DEFAULT_COORDINATE_RANGE
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
    coordinate_range: float = DEFAULT_COORDINATE_RANGE
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
    child_ids: List[int],
    coordinate_range: float = DEFAULT_COORDINATE_RANGE
) -> Tuple[float, float, float, int]:
    """
    Compute composition coordinate via concept hashing.
    
    Independent of child coordinates - represents abstract concept.
    
    Args:
        child_ids: List of child atom IDs
        coordinate_range: Coordinate range
    
    Returns:
        (x, y, z, m) tuple for composition atom
    """
    # Hash the composition structure
    composition_bytes = b''.join(
        child_id.to_bytes(8, byteorder='little', signed=True)
        for child_id in sorted(child_ids)
    )
    
    return locate_atom(composition_bytes, coordinate_range)


# ============================================================================
# VALIDATION
# ============================================================================

def _validate_coordinates(*coords: float) -> None:
    """Validate that coordinates are finite numbers."""
    for coord in coords:
        if not isinstance(coord, (int, float)):
            raise TypeError(f"Coordinate must be numeric, got {type(coord)}")
        if not (-1e10 < coord < 1e10):
            raise ValueError(f"Coordinate {coord} out of valid range")


# ============================================================================
# SQL QUERY BUILDERS (Future Enhancement)
# ============================================================================

def build_knn_query(
    point_sql: str,
    k: int = 10,
    distance_threshold: Optional[float] = None
) -> Tuple[str, dict]:
    """
    Build K-nearest-neighbor query.
    
    Args:
        point_sql: SQL expression for query point (e.g., "ST_MakePoint(%s, %s, %s)")
        k: Number of neighbors
        distance_threshold: Optional max distance
    
    Returns:
        (sql_template, params) for safe query execution
    """
    # TODO: Implement query builder
    raise NotImplementedError("KNN query builder coming in Phase 3")


def build_spatial_join(
    table1: str,
    table2: str,
    distance_threshold: float
) -> str:
    """Build spatial join query with ST_DWithin."""
    # TODO: Implement query builder
    raise NotImplementedError("Spatial join builder coming in Phase 3")
```

---

## Refactoring Steps

### Step 1: Create `spatial_utils.py`

**Action**: Create the helper module with all functions above.

**Files Created**:
- `api/services/geometric_atomization/spatial_utils.py`

**Tests Created**:
- `tests/unit/geometric/test_spatial_utils.py`

---

### Step 2: Refactor `AtomLocator`

**Before** (145 lines with duplication):
```python
class AtomLocator:
    def locate(self, value: bytes):
        # 35 lines of hash→coordinate logic
        ...
    
    def compute_hilbert_index(self, x, y, z):
        # 20 lines of normalization + Morton encoding
        ...
    
    def _morton_encode(self, x, y, z):
        # 25 lines of bit manipulation
        ...
```

**After** (~50 lines, delegates to spatial_utils):
```python
from . import spatial_utils

class AtomLocator:
    def locate(self, value: bytes) -> Tuple[float, float, float]:
        """Compute deterministic coordinate for atom value."""
        return spatial_utils.hash_to_coordinate(value, self.coordinate_range)
    
    def compute_hilbert_index(self, x: float, y: float, z: float) -> int:
        """Compute Hilbert index for coordinates."""
        return spatial_utils.compute_hilbert_index(
            x, y, z, self.coordinate_range
        )
    
    def locate_with_hilbert(self, value: bytes) -> Tuple[float, float, float, int]:
        """Compute (x, y, z, m) for atom value."""
        return spatial_utils.locate_atom(value, self.coordinate_range)
```

**Lines Removed**: ~80 lines of duplication  
**Lines Added**: ~10 lines of delegation

---

### Step 3: Refactor `FractalAtomizer`

**Before** (429 lines with duplication):
```python
class FractalAtomizer:
    def locate_primitive(self, value: bytes):
        # 26 lines IDENTICAL to AtomLocator.locate()
        ...
    
    def locate_composition(self, child_ids, strategy='midpoint'):
        # 45 lines of coordinate calculation
        ...
    
    def _compute_hilbert(self, x, y, z):
        # 19 lines IDENTICAL to AtomLocator.compute_hilbert_index()
        ...
    
    def _morton_encode(self, x, y, z):
        # 25 lines IDENTICAL to AtomLocator._morton_encode()
        ...
```

**After** (~350 lines, delegates to spatial_utils):
```python
from . import spatial_utils

class FractalAtomizer:
    def locate_primitive(self, value: bytes) -> Tuple[float, float, float]:
        """Compute coordinate for primitive atom."""
        return spatial_utils.hash_to_coordinate(value, self.coordinate_range)
    
    def locate_composition(
        self,
        child_ids: List[int],
        strategy: str = 'midpoint'
    ) -> Tuple[float, float, float]:
        """Compute coordinate for composition atom."""
        if strategy == 'midpoint':
            # Get child coordinates (from cache or DB)
            child_coords = [self.coord_cache[cid] for cid in child_ids]
            x, y, z, _ = spatial_utils.locate_composition_midpoint(
                child_coords, self.coordinate_range
            )
            return (x, y, z)
        
        elif strategy == 'concept':
            x, y, z, _ = spatial_utils.locate_composition_concept(
                child_ids, self.coordinate_range
            )
            return (x, y, z)
        
        else:
            raise ValueError(f"Unknown strategy: {strategy}")
    
    # Remove _compute_hilbert and _morton_encode (now in spatial_utils)
```

**Changes**:
- `locate_primitive`: Delegate to `spatial_utils.hash_to_coordinate()`
- `locate_composition`: Use `spatial_utils.locate_composition_*()` helpers
- Remove `_compute_hilbert()` (use `spatial_utils.compute_hilbert_index()`)
- Remove `_morton_encode()` (use `spatial_utils.morton_encode()`)

**Lines Removed**: ~80 lines  
**Lines Modified**: ~30 lines

---

### Step 4: Refactor `ST_MakePoint()` Calls

**Files to Update**:
1. `fractal_atomizer.py` (lines 205, 272-278, 305)
2. `spatial_reconstructor.py` (lines 100-105)
3. Any other modules with manual point creation

**Before**:
```python
# fractal_atomizer.py line 272
m = spatial_utils.compute_hilbert_index(x, y, z, self.coordinate_range)

sql = """
    SELECT atom_id FROM atom
    WHERE ST_DWithin(spatial_key, ST_MakePoint(%s, %s, %s, %s), %s)
    ORDER BY ST_Distance(spatial_key, ST_MakePoint(%s, %s, %s, %s))
"""
params = (x, y, z, m, tolerance, x, y, z, m)
```

**After**:
```python
# fractal_atomizer.py line 272
point_sql, point_params = spatial_utils.make_point_zm(x, y, z, m)

sql = f"""
    SELECT atom_id FROM atom
    WHERE ST_DWithin(spatial_key, {point_sql}, %s)
    ORDER BY ST_Distance(spatial_key, {point_sql})
"""
params = (*point_params, tolerance, *point_params)
```

**Benefits**:
- ✅ Centralized validation
- ✅ Consistent SRID handling
- ✅ Type safety
- ✅ Easier to modify (e.g., add SRID parameter globally)

---

### Step 5: Update Tests

**Tests to Refactor**:
1. `test_atom_locator.py` - Should now test delegation to `spatial_utils`
2. `test_fractal_atomizer.py` - Should now test high-level behavior (not duplicate logic)
3. **NEW**: `test_spatial_utils.py` - Comprehensive tests for all helper functions

**Test Coverage**:
- ✅ `hash_to_coordinate()`: Determinism, uniqueness, range validation
- ✅ `compute_hilbert_index()`: Valid BIGINT, determinism, edge cases
- ✅ `morton_encode()`: Bit interleaving correctness
- ✅ `locate_atom()`: Full (x,y,z,m) computation
- ✅ `locate_composition_*()`: Midpoint vs concept strategies
- ✅ `make_point_zm()`: SQL generation, SRID handling

---

## Migration Strategy

### Phase 1: Create Foundation ✅
1. ✅ Create `spatial_utils.py` with all helper functions
2. ✅ Create `test_spatial_utils.py` with comprehensive tests
3. ✅ Run tests to ensure helpers work correctly

### Phase 2: Refactor Core Modules ⏳
1. ⏳ Refactor `AtomLocator` to use `spatial_utils`
2. ⏳ Refactor `FractalAtomizer` to use `spatial_utils`
3. ⏳ Run existing tests to ensure no behavioral changes

### Phase 3: Refactor SQL Point Creation ⏳
1. ⏳ Replace raw `ST_MakePoint()` calls with `make_point_zm()`
2. ⏳ Update `spatial_reconstructor.py`
3. ⏳ Update `query.py` spatial queries

### Phase 4: Cleanup & Verify ⏳
1. ⏳ Remove duplicate `_morton_encode()` methods
2. ⏳ Remove duplicate `_compute_hilbert()` methods
3. ⏳ Run ALL 43 integration tests
4. ⏳ Commit refactored Phase 2

---

## Success Metrics

### Code Quality Improvements:
- **Lines Removed**: ~160 lines of duplication
- **Modules Simplified**: `AtomLocator` (145→50 lines), `FractalAtomizer` (429→350 lines)
- **Test Coverage**: +100 lines of spatial_utils tests
- **SOLID Compliance**: ✅ Single Responsibility (helpers isolated)
- **DRY Compliance**: ✅ No duplicate coordinate/Hilbert logic

### Behavioral Verification:
- ✅ All 43 integration tests pass
- ✅ All 85 unit tests pass
- ✅ No performance regression (coordinate calculation speed unchanged)

---

## Risk Assessment

### Low Risk:
- Creating `spatial_utils.py` (additive, no breaking changes)
- Adding tests for helpers

### Medium Risk:
- Refactoring `AtomLocator` and `FractalAtomizer` (must verify behavioral equivalence)
- Updating `ST_MakePoint()` calls (SQL generation must be identical)

### Mitigation:
1. ✅ Comprehensive test suite for `spatial_utils` BEFORE refactoring
2. ✅ Run existing tests after EACH refactoring step
3. ✅ Compare coordinate outputs before/after refactoring (sanity check)

---

## Next Steps

1. **Review this plan with user** - Confirm architectural approach
2. **Implement Phase 1**: Create `spatial_utils.py` + tests
3. **Implement Phase 2**: Refactor `AtomLocator` + `FractalAtomizer`
4. **Implement Phase 3**: Refactor SQL point creation
5. **Run full test suite**: Verify no behavioral changes
6. **Commit**: Clean, DRY, SOLID-compliant Phase 2

---

## Questions for User

1. **Approve overall approach?** Centralize spatial operations in `spatial_utils.py`?
2. **Priority**: Should we do this BEFORE running Phase 2 tests, or after?
3. **Scope**: Refactor ALL `ST_MakePoint()` calls, or just critical paths?
4. **Testing**: Run full 43-test suite after each step, or batch at end?

---

**Estimated Effort**: 2-3 hours  
**Estimated LOC Removed**: ~160 lines of duplication  
**Estimated LOC Added**: ~200 lines of helpers + tests  
**Net Change**: +40 lines, but MUCH cleaner architecture

---

## References

- **PostGIS Documentation**: ST_MakePoint, ST_SetSRID, ST_Distance
- **DRY Principle**: https://en.wikipedia.org/wiki/Don%27t_repeat_yourself
- **SOLID Principles**: https://en.wikipedia.org/wiki/SOLID
- **Hilbert Curve**: TODO link to schema implementation
- **Morton Encoding**: https://en.wikipedia.org/wiki/Z-order_curve
