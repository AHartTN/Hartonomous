# Geometric Compression & Sparse Encoding

**"The Void is Information."**

Hartonomous does not use traditional compression (gzip/lz4) as a primary layer. Instead, it exploits the **geometry of data** to achieve massive efficiency. 

This document details how **Spatial Gaps**, **Hilbert Curves**, and **Fractal Substitution** work together to enable performance and storage density.

---

## 1. The M-Coordinate: Encoding the Void

In traditional storage, a sparse tensor (mostly zeros) is often stored as a dense array, wasting massive space on `0.0`.

In Hartonomous, we use the **M (Measure)** coordinate of the `LINESTRING ZM` geometry to encode logical position.

### The Principle of Gaps (RLE/Sparse)

A trajectory is a sequence of **relation atoms** (not raw values).
*   **X, Y, Z**: Semantic Location of the relationship composition.
*   **M**: Logical Index (Position in original structure).

**Example: A Sparse Vector**
`[0, 0, 0, "A", 0, 0, "B", 0]`

**Traditional:** Stores 8 values.

**Geometric (Relations):** Stores 2 Composition Atoms.
1.  **Relation "A" at position 3**: `composition([position_3, "A"])` at M=3
2.  **Relation "B" at position 6**: `composition([position_6, "B"])` at M=6

**The Trajectory:**
`LINESTRING ZM (Coords_A 3, Coords_B 6)`

Where `Coords_A` = spatial_key of the composition `(position_3, "A")`

**The Implicit Information:**
*   The gap between `Start` and `3` implies 3 zeros/nulls.
*   The gap between `3` and `6` implies 2 zeros/nulls.

**Result:** Infinite zeros cost **zero bytes**. We only store the signal.

---

### Example: Weight Matrix as Connection Graph

**Weight matrix (4x4):**
```
[0.5  0.0  0.3  0.0]
[0.0  0.5  0.0  0.1]
[0.3  0.0  0.2  0.0]
[0.0  0.1  0.0  0.5]
```

**Traditional storage:** 16 float32 values = 64 bytes

**Geometric storage (Relations):**

1. **Atomize constants:**
```python
# Neuron IDs (structural constants)
n0, n1, n2, n3 = atomize(["n0", "n1", "n2", "n3"])

# Weight values (numerical constants - only 5 unique!)
w_0_0 = atomize(0.0)  # Not actually stored - sparse!
w_0_1 = atomize(0.1)
w_0_2 = atomize(0.2)
w_0_3 = atomize(0.3)
w_0_5 = atomize(0.5)
```

2. **Create connection compositions (only non-zero):**
```python
connections = [
    composition([n0, n0, w_0_5]),  # M=0  (row 0, col 0)
    composition([n0, n2, w_0_3]),  # M=2  (row 0, col 2)
    composition([n1, n1, w_0_5]),  # M=5  (row 1, col 1)
    composition([n1, n3, w_0_1]),  # M=7  (row 1, col 3)
    composition([n2, n0, w_0_3]),  # M=8  (row 2, col 0)
    composition([n2, n2, w_0_2]),  # M=10 (row 2, col 2)
    composition([n3, n1, w_0_1]),  # M=13 (row 3, col 1)
    composition([n3, n3, w_0_5]),  # M=15 (row 3, col 3)
]
```

3. **Build LINESTRING trajectory:**
```
LINESTRING ZM (
    x1 y1 z1 0,   -- Connection at [0,0]
    x2 y2 z2 2,   -- Connection at [0,2] (gap: M=1 is zero)
    x3 y3 z3 5,   -- Connection at [1,1] (gap: M=3,4 are zero)
    x4 y4 z4 7,   -- Connection at [1,3] (gap: M=6 is zero)
    x5 y5 z5 8,   -- Connection at [2,0]
    x6 y6 z6 10,  -- Connection at [2,2] (gap: M=9 is zero)
    x7 y7 z7 13,  -- Connection at [3,1] (gaps: M=11,12 are zero)
    x8 y8 z8 15   -- Connection at [3,3] (gap: M=14 is zero)
)
```

**Storage breakdown:**
- 4 neuron ID atoms (primitives)
- 4 unique weight value atoms (primitives - 0.0 not stored!)
- 8 connection composition atoms
- 1 LINESTRING with 8 points
- Total: 16 atoms + 1 trajectory

**Compression benefits:**
- Zero weights: not stored (gaps in M coordinate)
- Repeated values: w_0_5 appears 3 times → stored once, referenced 3 times
- Repeated connections: If same (source, target, weight) appears elsewhere → deduplicated
- Pattern discovery: BPE notices diagonal pattern (n_i → n_i with 0.5)

---

## 2. Hilbert Curves: Locality & Indexing

While `M` handles logical sequences, **Hilbert Curves** manage physical storage locality and n-dimensional indexing.

### Why Hilbert?
1.  **Locality Preservation:** Points close in 1D memory (Hilbert Index) are close in 3D semantic space. This optimizes disk I/O.
2.  **Clustering:** PostGIS B-Tree indexes on the Hilbert value are faster than R-Tree indexes for certain "box" queries.

### Detection of Repeats via Hilbert Gaps
When traversing the Hilbert curve of a dataset:
*   **Continuous segment:** Dense data cluster.
*   **Large Jump:** Change in concept or empty semantic space.

We use this to optimize **Page Layout**:
*   Atoms with similar Hilbert indices are stored on the same database page.
*   Fetching "Cat" brings "Kitten" into RAM automatically (Pre-fetching via Geometry).

---

## 3. Run-Length Encoding (RLE) via Trajectory

Repeated data is handled geometrically without duplicating atoms.

**Sequence:** `["A", "A", "A", "A", "B"]`

**Geometric Representation:**
`LINESTRING ZM (Coords_A 0, Coords_A 3, Coords_B 4)`

*   Note: We skipped indices 1 and 2.
*   **Protocol:** If `Point_N == Point_N+1` (same semantic location), the gap represents **Repetition** (RLE).
*   **Interpretation:** "The value at M=0 ('A') repeats until M=3."

**Synergy:**
*   **Atoms:** 'A' is stored once (Identity).
*   **Trajectory:** Stores only the *change events* (Transitions).

---

## 4. Performance Implications

This approach unifies **Compression** and **Indexing**.

| Operation | Traditional (Dense) | Geometric (Sparse/Fractal) | Mechanism |
|:---|:---|:---|:---|
| **Storage** | 100GB (High Redundancy) | ~2GB (Unique Atoms + Paths) | CAS + RLE Trajectories |
| **Query** | Scan 100GB | Scan Index of 2GB | Sparse Indexing |
| **Access** | Random I/O | Sequential I/O | Hilbert Locality |
| **Processing** | Load Zeros | Skip Zeros | M-Coordinate Gaps |

### The "Zero-Copy" Advantage
Because the compression is *structural* (not algorithmic like gzip), we can query the data **without decompressing it**.

*   **Query:** "Is there a 'B' after 'A'?"
*   **Action:** `ST_LocateAlong(trajectory, ...)`
*   **Status:** No decompression needed. The geometry *is* the index *is* the data.
