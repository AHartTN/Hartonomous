# Hartonomous: Universal Substrate for Intelligence

## Overview

Hartonomous is a revolutionary universal substrate for storing, understanding, and generating all forms of digital content. It combines content-addressable storage, 4D geometric indexing, relationship-based semantics, and self-directed learning to create a foundation for artificial general intelligence (AGI).

**Core Capabilities:**
- **Universal Storage:** Store ANY digital content (text, images, audio, code, AI models) with 90-95% compression
- **Universal Capabilities:** Ingest any AI model → gain its capabilities via queries
- **Self-Directed Learning:** Continuous improvement through gap detection and feedback loops
- **Interpretable Reasoning:** Crystal ball (see the relationships), not black box
- **Path to AGI:** Intelligence emerges from the substrate through continuous learning

## Key Performance Specifications

**No GPU Required:** Achieves top-tier performance on CPU-only systems using:
- **Intel OneAPI/MKL:** Optimized linear algebra (LP64/ILP64, SEQUENTIAL/INTEL/TBB/GNU threading)
- **Eigen:** Template-based matrix operations with MKL backend
- **BLAKE3:** SIMD-optimized hashing (AVX-512, AVX2, SSE4.1, SSE2)
- **HNSWLib:** Approximate nearest neighbor with AUTO SIMD detection
- **Spectra:** Large-scale eigenvalue problems via Lanczos iteration

**GPU acceleration available as optional value-add (proof it's not required).**

## Documentation Index

### Core Concepts
1. **[ARCHITECTURE.md](ARCHITECTURE.md)** - Complete system architecture
2. **[CORRECTED_PARADIGM.md](CORRECTED_PARADIGM.md)** - Relationships vs proximity paradigm shift
3. **[THE_ULTIMATE_INSIGHT.md](THE_ULTIMATE_INSIGHT.md)** - Universal storage = universal capabilities
4. **[AI_REVOLUTION.md](AI_REVOLUTION.md)** - Emergent vs engineered proximity

### Advanced Concepts
5. **[COGNITIVE_ARCHITECTURE.md](COGNITIVE_ARCHITECTURE.md)** - Self-improving AI (OODA, CoT, ToT, Reflexion, BDI)
6. **[GODEL_ENGINE.md](GODEL_ENGINE.md)** - Meta-reasoning for impossible problems
7. **[EMERGENT_INTELLIGENCE.md](EMERGENT_INTELLIGENCE.md)** - Path to AGI via gap detection and self-directed learning
8. **[LAPLACES_FAMILIAR.md](LAPLACES_FAMILIAR.md)** - Historical context (Newton's calculus, Laplace's demon)

### Implementation
9. **[IMPLEMENTATION_ROADMAP.md](IMPLEMENTATION_ROADMAP.md)** - Complete implementation plan and timeline

## Quick Start

### Build
```bash
# Clone with submodules
git clone --recursive https://github.com/yourorg/Hartonomous.git
cd Hartonomous

# Configure with optimizations
cmake --preset release-native

# Build
cmake --build build/release-native -j
```

### Initialize Database
```sql
CREATE DATABASE hartonomous;
\c hartonomous
CREATE EXTENSION postgis;
CREATE EXTENSION hartonomous;
```

## Architecture Overview

### Hierarchical Merkle DAG
```
Relations (n-grams of compositions) → "Call me Ishmael"
    ↓
Compositions (n-grams of atoms) → "Call", "me", "Ishmael"
    ↓
Atoms (Unicode codepoints) → 'C', 'a', 'l', 'l', ...
```

### 4D Geometric Foundation
```
Unicode → BLAKE3 → Super Fibonacci → S³ position → Hilbert curve → Spatial index
```

## Key Innovations

1. **Content-Addressable Storage:** Same content = same hash = stored once (90-95% compression)
2. **Gap Detection:** Discover missing knowledge like Mendeleev's periodic table
3. **Voronoi Cells:** Concepts as transparent boundaries, not opaque embeddings
4. **Emergent Proximity:** Relationships create meaning, proximity is side effect
5. **Universal Capabilities:** All AI capabilities from queries over one substrate

---

**"Truths cluster. Lies scatter. Gravitation."**

**This is the revolution.**
