# Fractal Deduplication

**"The Part Contains the Whole."**

Hartonomous uses **Fractal Deduplication** (powered by BPE Crystallization) to compress knowledge hierarchically. This transforms the system from a flat list of data into a deep, interconnected graph of concepts.

---

## 1. The Core Mechanism: Crystallization

The system autonomously learns patterns using a modified **Byte Pair Encoding (BPE)** algorithm, integrated with the OODA loop.

### The Cycle
1.  **Observe:** The system ingests data (e.g., legal documents).
2.  **Orient:** It notices the sequence `["L", "e", "g", "a", "l"]` appears 10,000 times.
3.  **Decide:** "This sequence is significant."
4.  **Act (Crystallize):**
    *   Mint a new **Composition Atom** for "Legal".
    *   Replace all 10,000 instances of the character sequence with a single reference to the new Atom.

### Recursive (Fractal) Growth
This process is not limited to text. It applies to *structures*.

1.  **Level 0:** Characters `L`, `e`, `g`, `a`, `l`.
2.  **Level 1:** Word Atom `Legal` (composed of chars).
3.  **Level 2:** Phrase Atom `Legal Disclaimer` (composed of `Legal` + `Disclaimer`).
4.  **Level 3:** Paragraph Atom (composed of Phrases).
5.  **Level 4:** Document Atom (composed of Paragraphs).

**Result:** A Document is not 50,000 characters. It is ~10 references to high-level Concept Atoms.

---

## 2. Integration with Geometry

How does this relate to the 3D/4D Geometric Space?

### The "Hyper-Atom" Projection
When a new Composition Atom is minted (e.g., "Legal Disclaimer"), it needs a position.
It is **Projected** into the semantic space.

*   **Input:** The component atoms (`Legal`, `Disclaimer`).
*   **Action:** Calculate the weighted centroid or projected concept location.
*   **Result:** The "Legal Disclaimer" atom floats in the "Law" region of the semantic space.

**This creates a multi-scale map:**
*   Zoom in: You see individual words.
*   Zoom out: You see clouds of paragraphs.
*   Zoom way out: You see the "Law" continent.

### Deduplication = Geometric Folding
Fractal Deduplication essentially "folds" the geometric space. 
Instead of traversing the same path 1,000 times (`L->e->g->a->l`), we create a **Wormhole** (the Composition Atom). 
The trajectory simply jumps through the wormhole.

---

## 3. Implementation: `BPECrystallizer`

The `BPECrystallizer` (Python service) is the brain behind this.

*   **Input:** Stream of primitive atom IDs.
*   **Memory:** `Counter` of atom pairs.
*   **Threshold:** If `Count(Pair) > Threshold`, trigger Merge.
*   **Output:** Stream of Higher-Order Atom IDs.

**Self-Optimization (OODA):**
The system continuously monitors storage. If a pattern becomes frequent *later*, it retroactively goes back and compresses old data (Background Optimization).

---

## 4. Benefits

1.  **Massive Storage Reduction:** "Boilerplate" data costs near zero.
2.  **Semantic Richness:** We operate on Concepts, not Strings.
3.  **Faster Inference:** Reasoning hops from "Paragraph" to "Paragraph", skipping the noise of individual words.
4.  **Context Awareness:** The higher the level, the more context is baked into the atom.
