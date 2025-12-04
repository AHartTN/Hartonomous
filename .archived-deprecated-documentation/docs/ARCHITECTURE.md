# ARCHITECTURE

**Hartonomous System Architecture**
**Version:** 0.8.0 (Voronoi-Geometric Reformation)

---

## 1. The Data Model: Geometric Atomization

The core insight is that **Data = Geometry**. We map information to PostGIS geometry types, enabling Meaning (Compositional Gravity) and Efficiency (Fractal Compression).

### The `atom` Table
The "Periodic Table" of the system. Every record is an immutable constant.

```sql
CREATE TABLE atom (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA UNIQUE NOT NULL,     -- Identity (CAS)
    atomic_value BYTEA,                     -- The Constant (Value)
    
    -- GEOMETRY IS MEANING
    spatial_key GEOMETRY(GEOMETRY, 0),      
    
    metadata JSONB,                         -- Input for Projection
    created_at TIMESTAMPTZ DEFAULT now()
);
```

### The Geometry Types

| Concept | Geometry | PostGIS Type | Usage |
|:---|:---|:---|:---|
| **Atom** | Vertex | `POINT ZM` | Single constant. Positioned via Centroid/Projection. |
| **Sequence** | Trajectory | `LINESTRING ZM` | Ordered list. **M** encodes Sparse/RLE gaps. |
| **Definition** | Region | `POLYGON ZM` | Voronoi Cell defining a concept scope. |
| **Cluster** | Cloud | `MULTIPOINT ZM` | Distributed attributes. |

---

## 2. The Unified Pipeline

```
[ Ingestion Layer ]
        | Raw Data
        v
[ Fractal Atomizer (BPE) ]               <-- (FRACTAL_DEDUPLICATION.md)
    * Pattern Recognition (OODA)
    * Mint Composition Atoms
        |
        v
[ Projection Engine (spatial_utils.py) ] <-- (Spatial Semantics)
    * Primitives: Landmark Projection (Gram-Schmidt)
    * Compositions: Gravity (Weighted Centroids)
        |
        v
[ Trajectory Builder ]                   <-- (GEOMETRIC_COMPRESSION.md)
    * Construct LINESTRING ZM
    * Encode Gaps (Sparse/RLE) in M-Coordinate
        |
        v
[ Database Layer (PostgreSQL + PostGIS) ]
    * Storage (Atoms)
    * Indexing (GiST + Hilbert)
    * Optimization (Voronoi Calculation Stored Procs)
```

---

## 3. The Projection Engine (`spatial_utils.py`)

This module determines *where* things live.

**Dual Strategy:**
1.  **Landmark Projection (Primitives):** 
    *   Use **Gram-Schmidt Orthonormalization** to project primitive atoms onto basis vectors (e.g., Modality, Sentiment).
2.  **Compositional Gravity (Compounds):**
    *   Calculate position as the **Centroid** of component atoms.
    *   Meaning emerges: A "Cat" atom automatically lands near "Fur" and "Claws".

---

## 4. Geometric Compression (The M-Coordinate)

We exploit the `M` (Measure) coordinate for efficiency.

*   **Standard:** `M = Sequence_Index`
*   **Sparse/RLE:** `M = Logical_Index`
    *   Jumps in `M` represent Zeros or Repetition.
    *   Allows efficient storage of sparse tensors and redundant text.

---

## 5. Inference Strategy

Inference is **Geometry & Pathfinding**.

1.  **Classification (Voronoi):**
    *   Generate Voronoi Polygons from concept centroids.
    *   `ST_Contains(Voronoi_Cell, Query_Point)` -> Classify.
2.  **Reasoning (A* Search):**
    *   Use **Euclidean Distance** as the A* heuristic ($h(n)$).
    *   Find the logical path between concepts by navigating the semantic graph.
3.  **Prediction (Trajectory):**
    *   Extrapolate the vector of a `LINESTRING`.

---

## 6. The OODA Loop (Self-Optimization)

Autonomous maintenance cycles implemented as **PostgreSQL Stored Procedures**.

1.  **Observe:** Detect clusters of atoms.
2.  **Orient:** Analyze density.
3.  **Decide:** "This cluster defines a new region."
4.  **Act:** Compute `ST_VoronoiPolygons` and store the new concept boundary.

---

## 7. Current Status & Roadmap

**Implemented:**
*   [x] `atom` table structure.
*   [x] `FractalAtomizer` (Recursive decomposition).
*   [x] `LINESTRING` trajectory generation.

**Immediate Goals:**
1.  **Refactor Projection Engine:** Implement Landmark Projection & Compositional Gravity in `spatial_utils.py`.
2.  **Implement Sparse Trajectories:** Update `TrajectoryBuilder` to handle sparse `M` values.
3.  **Deploy Voronoi Logic:** Create stored procedures for dynamic region generation.
