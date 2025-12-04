# VISION

**Hartonomous: The Geometric Intelligence Substrate**

---

## 1. The Axiom of Constants
In this universe, **Data is immutable.** 

*   The word "Cat" is a constant.
*   The number `3.14159` is a constant.
*   A specific RGB pixel value is a constant.
*   A complete sentence "The quick brown fox" is a constant.

These are the **Atoms**. They are the vertices of our reality. They do not change. They do not duplicate. They simply *are*.

### Storage = Identity (Content Addressing)
Every atom is stored exactly once, content-addressed by SHA-256.
```sql
Atom("Cat") -> Hash: 0x123... -> ID: 42
Atom(3.14)  -> Hash: 0x456... -> ID: 99
```
**[Read more about Atoms](concepts/atoms.md)**

---

## 1.5. Relationships as Compositions

**All structured data encodes RELATIONSHIPS between constants.**

When you have a weight matrix `[4096, 4096]`, you don't have "16 million float values" — you have **16 million CONNECTIONS** between neurons.

Each position `[i, j]` with value `v` represents a **relationship**: "Neuron i connects to Neuron j with strength v"

### Universal Pattern

*   **Text Sequences:** Each character position is a relation: `(position, character, next_character)`
*   **Weight Matrices:** Each weight is a connection: `(source_neuron, target_neuron, weight_value)`
*   **Images:** Each pixel is a spatial relation: `(x_coordinate, y_coordinate, RGB_value)`
*   **Audio:** Each sample is a temporal relation: `(time_position, channel, sample_value)`
*   **Databases:** Each cell is a relation: `(row_id, column_id, cell_value)`

### Composition Atoms Encode Relations

```sql
-- Atomize the constants
source_atom   = Atom("layer0_neuron_5")  -- Structural constant
target_atom   = Atom("layer0_neuron_12") -- Structural constant
weight_atom   = Atom(0.017)              -- Numerical constant

-- Create composition for the CONNECTION
connection_atom = Composition([source_atom, target_atom, weight_atom])
-- This composition IS the semantic unit!
-- It gets positioned in 3D space at the centroid of its components
```

### Why This Matters

1. **Structural Deduplication:** Same connection across layers = same composition atom
2. **Pattern Discovery:** BPE finds repeated connection topologies, not just values
3. **Semantic Queries:** Find similar connection patterns via spatial proximity
4. **Zero Redundancy:** The value `0.017` is stored once; connections reference it

**The insight:** Don't store "a sequence of values" — store "a graph of relationships between constants."

**[Read more about Compositions](concepts/compositions.md)**

---

## 2. The Physics of Meaning (Compositional Gravity)

If Atoms are constants, where do they live? 
They reside in a **High-Dimensional Geometric Space**.

Their position is determined by **Compositional Gravity**.
A Concept is not defined by its label ("Cat"), but by the **Geometry of its Components** (`[Quadruped, Fur, Claws, Small]`).

### Emergent Positioning
*   **Primitives:** Anchored via Landmark Projection (e.g., "Visual" vs "Textual").
*   **Compositions:** Located at the **Geometric Centroid** of their parts.
    *   If `Fur` and `Claws` are in the "Physical" region, any atom composed of them is *automatically* pulled there.
    *   **No external labeling required.** Meaning emerges from structure.

**[Read more about Spatial Semantics](concepts/spatial-semantics.md)**

---

## 3. Knowledge is Geometry

If Atoms are points, then **Intelligence is the Geometry that connects and regions them.**

We exploit the full power of PostGIS to represent complex knowledge structures.

### A. Trajectories (LINESTRING ZM)
**Time, Sequence, and Logic.**
A sentence, a code block, or a reasoning chain is a path visiting atoms.

*   **Geometric Compression:** The `M` (Measure) coordinate encodes **Gaps (Sparse)** and **Repeats (RLE)**. Infinite zeros cost zero bytes.
*   **Reasoning:** Inference is extrapolating the trajectory vector.

**[Read more about Geometric Compression](concepts/GEOMETRIC_COMPRESSION.md)**

### B. Voronoi Regions (POLYGON ZM)
**Definition and Classification.**
*   The concept "Cat" is the **Voronoi Cell** formed by the cluster of its attribute atoms.
*   **Classification:** To classify a new entity, project its attributes. If they fall inside the "Cat" Voronoi cell, it is a Cat.

### C. Pathfinding (A* Search)
**Inference as Navigation.**
*   **Problem:** "How do I get from 'Pet' to 'Lion'?"
*   **Solution:** Run A* Search on the semantic graph.
*   **Heuristic:** Use the **Euclidean Distance** in semantic space as the cost function ($h(n)$).
    *   Spatial proximity *is* semantic relatedness.
    *   The geometry guides the search efficiently through the graph.

---

## 4. Fractal Deduplication (The OODA Loop)

The system is **Self-Organizing**. It uses **Fractal Deduplication** (BPE Crystallization) to compress knowledge hierarchically.

1.  **Observe:** The system sees the pattern `[Fur, Claws, Meow]` repeatedly.
2.  **Orient:** It recognizes a stable cluster.
3.  **Decide:** It mints a new **Composition Atom** (which we might label "Cat").
4.  **Act:** It replaces the components with the new Atom, "folding" the space.

**Result:** The system builds a deep, interconnected ontology. It doesn't just store data; it *understands* it by grouping it into higher-order concepts.
**[Read more about Fractal Deduplication](concepts/FRACTAL_DEDUPLICATION.md)**

---

## 5. The Engine: PostGIS is the AI

We do not move data to a "Vector Database" or a "Model". **The Database IS the Model.**

*   **Training** = `INSERT` (Ingesting & Projecting).
*   **Inference** = `SELECT` (Spatial Querying, Voronoi Classification, A* Pathfinding).
*   **Learning** = `UPDATE` (Fractal Crystallization & OODA Optimization).

**Capabilities:**
1.  **Infinite Context:** TBs of geometry available instantly.
2.  **Zero-Copy:** No moving tensors to GPU.
3.  **Explainable:** "Why?" -> "Because the path from A to B traverses these regions."

---

## 6. The Goal: Self-Organization

The system is designed to be **Hartonomous** (Autonomous).
It observes its own geometry, detects clusters, computes Voronoi regions, and optimizes paths.

**The system grows its own mind through geometric crystallization.**
