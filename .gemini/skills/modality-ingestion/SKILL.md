---
name: modality-ingestion
description: Map non-text data modalities (audio, images, signals) into the Hartonomous Unicode-geometric space. Use when defining the cascade from bits to Atoms, Compositions, and Relationships.
---

# Modality Ingestion: The Trajectory Cascade

This skill governs the conversion of raw signals into meaningful geometric trajectories on S³.

## The Cascading Trajectory Paradigm

Meaning is meaningless until sequenced. This system throws away "file types" in favor of trajectories of seeded Atoms.

### 1. The Atomization (Signal -> Decimal Atom)
Every numeric signal value is decomposed into its decimal-digit character Atoms.
- **Audio Sample**: A PCM value of `0.123` becomes the Atom sequence `['0', '.', '1', '2', '3']`.
- **Frequency**: 440 Hz becomes the Atom sequence `['4', '4', '0']`.
- **Constants**: $\pi$ becomes `['3', '.', '1', '4', ...]`.

### 2. The Composition (Sequence -> Level 1 Trajectory)
A sequence of Atoms forms a **Composition**.
- **The Rule**: Numeric identifiers for Compositions must be **Even**.
- **Spatial Mapping**: A Composition is a 4D linestring. Its centroid is its semantic location on S³.

### 3. The Relationship (Trajectory -> Level 2 Trajectory)
A sequence of Compositions forms a **Relationship**.
- **Meaning**: Emerges from the context of Level 1 trajectories.
- **Deduplication**: Relationships are content-addressable via BLAKE3 hashes of their Composition-sequences.

## Implementation Workflow
1.  **Source Parsing**: Use treesitter or custom PCM/RGB parsers to extract raw values.
2.  **Decimal Decomposition**: Transform values into the seeded Unicode Atom sequences.
3.  **Trajectory Storage**: Batch insert into `CompositionSequence` and `RelationSequence`.
4.  **Geometric Indexing**: Normalize centroids to S³ surface and encode into the **strictly 128-bit Hilbert index**.
5.  **Lossy Reconstruction**: Verify signal integrity by reversing the cascade.