# Spatial Semantics

**How atoms are positioned in 3D space: Hilbert curves, landmark projection, and geometric meaning.**

---

## Core Concept

**Every atom has a position in 3D semantic space where proximity = similarity.**

```sql
atom.spatial_key = POINT Z(X, Y, Z)
```

**Close in space ? similar in meaning.**

```sql
-- "cat" and "kitten" have nearby positions
ST_Distance('cat'.spatial_key, 'kitten'.spatial_key) = 0.08

-- "cat" and "quantum" are far apart
ST_Distance('cat'.spatial_key, 'quantum'.spatial_key) = 2.45
```

**No embedding model needed.** Positions emerge from landmark projection and semantic neighbor averaging.

---

## 3D Semantic Space

### Axis Meanings

**Current Implementation: 3D with Hilbert Index**

The system uses **3D coordinates (X, Y, Z)** for semantic positioning, with the **Hilbert curve index** providing N-dimensional compression into a 1D integer for efficient indexing.

**Design Intent: 4D (POINTZM)** The architecture is designed for `GEOMETRY(POINTZM, 0)` where:

- **X, Y, Z**: 3D semantic position
- **M**: Hilbert curve index (N-dimensional encoding)

This exploits PostGIS spatial datatypes to store non-spatial (Hilbert) data for optimization.

**Current Schema Status:** ?? Currently implemented as `GEOMETRY(POINTZ, 0)` (3D only). Schema migration to POINTZM pending.

**X-axis: Modality**

- 0.0 ? Text
- 0.3 ? Code
- 0.6 ? Image
- 0.9 ? Audio

**Y-axis: Category**

- 0.0 ? Abstract concepts
- 0.3 ? Symbolic/logical
- 0.6 ? Relational
- 0.9 ? Literal/concrete

**Z-axis: Specificity**

- 0.0 ? Universal (applies everywhere)
- 0.3 ? Aggregate (collections)
- 0.6 ? Compound (combinations)
- 0.9 ? Atomic (indivisible)

**M-axis (Future): Hilbert Index**

- N-dimensional Hilbert curve encoding of (X, Y, Z)
- Enables fast B-tree indexing via 1D integer
- Preserves spatial locality

**Example positions:**

```
Character 'e':
  POINT Z(0.05, 0.85, 0.92)
  Hilbert index: 12847563892
  Future: POINT ZM(0.05, 0.85, 0.92, 12847563892)
  ? Text modality, literal, atomic

Word "learning":
  POINT Z(0.08, 0.45, 0.65)
  Hilbert index: 987654321
  Future: POINT ZM(0.08, 0.45, 0.65, 987654321)
  ? Text modality, symbolic, compound

Image pixel RGB(255,0,0):
  POINT Z(0.65, 0.90, 0.95)
  Hilbert index: 41231231231
  Future: POINT ZM(0.65, 0.90, 0.95, 41231231231)
  ? Image modality, literal, atomic

Model weight 0.17:
  POINT Z(0.30, 0.50, 0.80)
  Hilbert index: 192837465
  Future: POINT ZM(0.30, 0.50, 0.80, 192837465)
  ? Code/model modality, symbolic, compound
```

---

## Landmark Projection

### What Are Landmarks?

**Landmarks** are fixed reference points in semantic space representing fundamental concepts.

```python
# Modality landmarks
MODALITY_TEXT = LandmarkPosition(x=0.05, y=0.50, z=0.50)
MODALITY_IMAGE = LandmarkPosition(x=0.60, y=0.50, z=0.50)
MODALITY_AUDIO = LandmarkPosition(x=0.90, y=0.50, z=0.50)

# Category landmarks
CATEGORY_LITERAL = LandmarkPosition(x=0.50, y=0.90, z=0.50)
CATEGORY_ABSTRACT = LandmarkPosition(x=0.50, y=0.10, z=0.50)
CATEGORY_RELATIONAL = LandmarkPosition(x=0.50, y=0.60, z=0.50)

# Specificity landmarks
SPECIFICITY_ATOMIC = LandmarkPosition(x=0.50, y=0.50, z=0.95)
SPECIFICITY_UNIVERSAL = LandmarkPosition(x=0.50, y=0.50, z=0.05)
```

### How Projection Works

**New atom position = weighted average of relevant landmark positions.**

```python
def project_atom(modality: str, category: str, specificity: float):
    # Get relevant landmarks
    mod_landmark = get_modality_landmark(modality)
    cat_landmark = get_category_landmark(category)
    spec_landmark = get_specificity_landmark(specificity)
    
    # Weighted average
    x = mod_landmark.x * 0.6 + cat_landmark.x * 0.3 + spec_landmark.x * 0.1
    y = mod_landmark.y * 0.2 + cat_landmark.y * 0.6 + spec_landmark.y * 0.2
    z = mod_landmark.z * 0.1 + cat_landmark.z * 0.2 + spec_landmark.z * 0.7
    
    return (x, y, z)
```

**Weights vary by axis:**

- X (modality): 60% modality, 30% category, 10% specificity
- Y (category): 20% modality, 60% category, 20% specificity
- Z (specificity): 10% modality, 20% category, 70% specificity

**Example: Character 'e'**

```python
# Modality: text
mod = MODALITY_TEXT  # (0.05, 0.50, 0.50)

# Category: literal
cat = CATEGORY_LITERAL  # (0.50, 0.90, 0.50)

# Specificity: atomic (1.0)
spec = SPECIFICITY_ATOMIC  # (0.50, 0.50, 0.95)

# Weighted average
x = 0.05*0.6 + 0.50*0.3 + 0.50*0.1 = 0.03 + 0.15 + 0.05 = 0.23
y = 0.50*0.2 + 0.90*0.6 + 0.50*0.2 = 0.10 + 0.54 + 0.10 = 0.74
z = 0.50*0.1 + 0.50*0.2 + 0.95*0.7 = 0.05 + 0.10 + 0.665 = 0.815

# Result: POINT Z(0.23, 0.74, 0.815)
```

---

## Semantic Neighbor Averaging

### Refining Positions

After initial landmark projection, positions are refined based on **semantic neighbors** (atoms with similar content/meaning).

**Algorithm:**

```sql
-- 1. Find semantic neighbors (during ingestion)
WITH neighbors AS (
    SELECT atom_id, spatial_key, similarity
    FROM atom
    WHERE metadata->>'modality' = $new_atom_modality
    ORDER BY similarity($new_atom_content, atomic_value) DESC
    LIMIT 100
)
-- 2. Compute weighted centroid
UPDATE atom
SET spatial_key = ST_Centroid(
    ST_Collect(
        ARRAY(
            SELECT ST_SetSRID(
                ST_MakePoint(
                    ST_X(spatial_key) * similarity,
                    ST_Y(spatial_key) * similarity,
                    ST_Z(spatial_key) * similarity
                ), 0
            )
            FROM neighbors
        )
    )
)
WHERE atom_id = $new_atom_id;
```

**Example:**

```
New word: "kitten"
Initial position (landmark): POINT Z(0.08, 0.45, 0.65)

Semantic neighbors found:
  "cat"    (similarity=0.95) ? POINT Z(0.10, 0.48, 0.67)
  "feline" (similarity=0.85) ? POINT Z(0.09, 0.46, 0.66)
  "dog"    (similarity=0.70) ? POINT Z(0.15, 0.50, 0.68)

Weighted centroid:
  x = (0.10*0.95 + 0.09*0.85 + 0.15*0.70) / (0.95+0.85+0.70) = 0.11
  y = (0.48*0.95 + 0.46*0.85 + 0.50*0.70) / 2.50 = 0.48
  z = (0.67*0.95 + 0.66*0.85 + 0.68*0.70) / 2.50 = 0.67

Final position: POINT Z(0.11, 0.48, 0.67)
```

**Result:** "kitten" positioned very close to "cat" (distance ~0.03).

---

## Hilbert Curves

### Why Hilbert Curves?

**Problem:** Indexing 3D space is expensive (GiST indexes have overhead).

**Solution:** Map 3D coordinates to 1D integer via **N-dimensional space-filling curve**.

```
3D Point (x, y, z) ? Hilbert Transform ? 1D Integer (Hilbert index)
```

**Key Insight:** The Hilbert curve algorithm is **N-dimensional**, not locked to 3D. The current implementation uses 3D coordinates but the algorithm generalizes to any dimensionality.

**Benefits:**
- **B-tree indexing** on integer (fast!)
- **Locality preservation** (nearby in 3D ? nearby in 1D)
- **Range queries** (box search ? integer range)
- **Exploits spatial types for non-spatial data** (Hilbert index stored as M coordinate)

### Design: POINTZM (4D) Storage

**Intended Schema:**
```sql
spatial_key GEOMETRY(POINTZM, 0)
-- X, Y, Z: 3D semantic coordinates
-- M: Hilbert curve index (N-dimensional encoding)
```

**Current Schema:** ?? `GEOMETRY(POINTZ, 0)` (3D only)

The M coordinate would store the pre-computed Hilbert index, enabling:
1. **GiST index** for exact spatial queries (X, Y, Z)
2. **B-tree index** on M for fast approximate queries (Hilbert)
3. **Dual indexing strategy** without storing redundant data

### Hilbert Curve Properties

**Space-filling curve** that visits every point in an N-dimensional cube:

```
3D Hilbert Curve:
Order 1 (2ณ = 8 points)
Order 2 (4ณ = 64 points)
Order 3 (8ณ = 512 points)
Order 21 (2ฒน ? 2M points per axis ? 10น? total addressable points)
```

**Key property:** Points close in N-dimensional space have nearby Hilbert indices (locality preservation).

**The algorithm is N-dimensional:** While the current implementation encodes 3D coordinates, the Hilbert transform algorithm itself works for any dimensionality (2D, 3D, 4D, ..., ND).

### Encoding Algorithm

```python
def encode_hilbert_3d(x: float, y: float, z: float, order: int = 21) -> int:
    """
    Encode 3D coordinates [0,1]ณ to Hilbert index.
    
    NOTE: The '3d' in the function name refers to the current implementation
    using 3D coordinates. The Hilbert curve algorithm itself is N-dimensional
    and can be extended to any dimensionality.
    
    Args:
        x, y, z: Coordinates in [0, 1]
        order: Precision (21 bits ? 2ฒน = 2M points per axis)
               Total addressable space: (2ฒน)ณ ? 10น? points
    
    Returns:
        Hilbert index (0 to 2^(3*order)-1)
        This 1D integer encodes the N-dimensional position
    """
    # Normalize to integer coordinates
    max_coord = (1 << order) - 1  # 2^order - 1
    ix = int(x * max_coord)
    iy = int(y * max_coord)
    iz = int(z * max_coord)
    
    # Apply N-dimensional Hilbert transform
    # Uses Gray code + bit interleaving + axis rotations
    # to preserve spatial locality in 1D
    return hilbert_3d_encode_impl(ix, iy, iz, order)
```

**Example:**

```python
# Point: (0.5, 0.8, 0.3)
# Order: 21 (2ฒน = 2,097,152 points per axis)

hilbert_index = encode_hilbert_3d(0.5, 0.8, 0.3, order=21)
# Result: 12847563892 (64-bit integer)

# Future: Store in POINTZM
UPDATE atom
SET spatial_key = ST_MakePointM(0.5, 0.8, 0.3, 12847563892)
WHERE atom_id = $id;

# Current: Store separately (pending schema migration)
UPDATE atom
SET 
    spatial_key = ST_MakePoint(0.5, 0.8, 0.3),
    metadata = jsonb_set(metadata, '{hilbert_index}', '12847563892')
WHERE atom_id = $id;
```

### The N-Dimensional Nature

**Key Insight:** While implemented for 3D coordinates, the Hilbert curve algorithm is fundamentally **N-dimensional**:

- **2D**: Map plane to line (used in image processing)
- **3D**: Map cube to line (current implementation)
- **4D+**: Map hypercube to line (future: include temporal, confidence, etc.)

**The algorithm generalizes:** The same Gray code + rotation transformations work for any dimensionality. The current 3D implementation is not a fundamental limitation but a practical starting point for semantic space.
