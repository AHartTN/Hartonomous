---
name: ml-spectral-projection
description: Extract sparse relationships from dense AI models using HNSWLib k-NN. Models are relationship extractors (sparse edges), not inference engines (dense weights). Discard models after extraction.
---

# ML Sparse Relationship Extraction

This skill manages extraction of sparse relationships from dense transformer models. **Models → sparse graph → discard models.**

## The Sparse Extraction Paradigm

**Core insight**: AI models contain implicit relationships in their weights. Extract those relationships as explicit graph edges, then discard the dense model.

**Result**: 400B parameter model → ~100k relationship edges = 1000x compression.

## The HNSWLib Pipeline

Implementation: `Engine/tools/ingest_model` + `Engine/src/ml/`

### 1. Load Dense Embeddings
- **Input**: Model embeddings (e.g., MiniLM: 30k tokens × 384 dims = 11.52M floats)
- **Format**: Binary float32 arrays or HDF5
- **Tools**: Custom parsers or HuggingFace transformers library

### 2. k-NN Graph Construction (HNSWLib)
**Purpose**: Find semantic neighbors in embedding space (implicit relationships).
- **Library**: HNSWLib with AUTO-SIMD (AVX-512/AVX2 detection)
- **Algorithm**: Hierarchical NSW for O(log N) approximate nearest neighbors
- **Parameters**:
  - `M`: 16 (connections per node)
  - `ef_construction`: 200 (build accuracy)
  - `k`: 10-50 (neighbors to extract per token)

### 3. Sparse Edge Extraction
For each token, extract k nearest neighbors as **Relations** in Hartonomous:
- **Composition A**: Source token (mapped to existing Composition via BLAKE3)
- **Composition B**: Neighbor token
- **Initial ELO**: Derived from embedding distance (closer = higher initial ELO)
  - Formula: `elo_init = 1000 + (1000 * (1.0 - normalized_distance))`
- **Evidence**: Store source model identifier in `relation_evidence`

### 4. Cross-Model Competition
When multiple models suggest same relation:
- Relations compete via ELO dynamics
- Query success increases ELO
- Query failure decreases ELO
- Consensus emerges across models (wisdom of crowds)

### 5. Discard Dense Model
**After extraction**: Delete original model weights.
- Sparse graph preserved (relations + ELO)
- Dense embeddings discarded
- Model capabilities now accessible via graph queries

## Mathematical Libraries
- **Intel MKL**: BLAS for any linear algebra (if using Laplacian eigenmaps for optional projection)
- **Eigen**: Matrix operations
- **HNSWLib**: Core extraction tool (k-NN graph)
- **BLAKE3**: Content-addressing for deduplication

## Optional: Laplacian Eigenmaps Projection
If models need 4D S³ projection (not always necessary):
1. Compute graph Laplacian: $L = D - W$
2. Solve for 4 smallest non-trivial eigenvectors (Spectra + MKL)
3. Gram-Schmidt orthonormalization (MKL QR)
4. Normalize to S³ surface: $v' = v / \|v\|$

## Verification
```sql
-- Check extracted relations from specific model
SELECT COUNT(*) 
FROM hartonomous.relation r
JOIN hartonomous.relation_evidence re ON r.id = re.relation_id
WHERE re.source_description LIKE '%MiniLM%';
-- Should show ~100k-1M relations depending on model size

-- Check ELO distribution
SELECT 
    MIN(elo_score) as min_elo,
    AVG(elo_score) as avg_elo,
    MAX(elo_score) as max_elo
FROM hartonomous.relation_rating;
-- Should see range around 1000 ± 500 initially
```