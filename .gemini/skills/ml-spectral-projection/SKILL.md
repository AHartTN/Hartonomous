---
name: ml-spectral-projection
description: Extract sparse relationships from dense AI models using HNSWLib k-NN. Models are relationship extractors, not inference engines. Discard after extraction.
---

# ML Sparse Relationship Extraction

**Paradigm**: Dense model → k-NN graph → sparse relation edges → discard model.

## Pipeline (Engine/tools/ingest_model + Engine/src/ml/)

1. **Load embeddings** — Binary float32 arrays (e.g., MiniLM: 30k tokens × 384 dims)
2. **Build k-NN graph** — HNSWLib (`M=16, ef_construction=200, k=10-50`)
3. **Extract relations** — Each k-NN neighbor pair becomes a Relation with initial ELO derived from distance: `elo_init = 1000 + (1000 * (1.0 - normalized_distance))`
4. **Store evidence** — Source model identifier in `relation_evidence` for provenance
5. **Cross-model competition** — Same relation from multiple models competes via ELO dynamics
6. **Discard model** — Only sparse graph persists

## Key Libraries
- **HNSWLib**: k-NN graph construction (AUTO SIMD: AVX-512/AVX2)
- **Intel MKL + Eigen**: Optional Laplacian eigenmap projection to S³
- **BLAKE3**: Content-addressing for deduplication

## Script
```bash
./scripts/linux/20-ingest-mini-lm.sh  # Ingest MiniLM embedding model
```