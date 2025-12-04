# Atomization Relationship Structure

## Overview

When models are atomized, we maintain **hierarchical compositional relationships** between all components. This allows reconstruction, cross-model deduplication, and semantic queries.

## Relationship Hierarchy

### Model → Components
```
Model Atom (root)
├─ Vocabulary Atom (metadata)
│  └─ Token Atoms (3D semantic coords)
│     └─ Character Atoms (shared across models)
├─ Architecture Atom (hyperparameters)
│  └─ Config Parameter Atoms (shared across models with same arch)
└─ Tensor Atoms (metadata: shape, dtype, device)
   └─ Weight Atoms (values with RLE compression, shared across models)
```

### Composition Table Structure

Every parent→component relationship is stored in `atom_composition`:

```sql
CREATE TABLE atom_composition (
    parent_atom_id BIGINT REFERENCES atom(atom_id),
    component_atom_id BIGINT REFERENCES atom(atom_id),
    sequence_index BIGINT,  -- Order matters for reconstruction
    PRIMARY KEY (parent_atom_id, component_atom_id, sequence_index)
);
```

## Vocabulary Relationships

### Token → Character Compositions
```python
# For each token: "hello" (token_id=123)
# We create compositions preserving order:
token_atom[123] → char_atom['h'] (sequence_index=0)
token_atom[123] → char_atom['e'] (sequence_index=1)
token_atom[123] → char_atom['l'] (sequence_index=2)
token_atom[123] → char_atom['l'] (sequence_index=3)
token_atom[123] → char_atom['o'] (sequence_index=4)
```

**Deduplication**: Character 'l' appears twice but references the **same atom** (refcount=2).

### Vocabulary → Token Compositions
```python
# Vocabulary root contains all tokens in order
vocab_atom → token_atom[0] (sequence_index=0)
vocab_atom → token_atom[1] (sequence_index=1)
vocab_atom → token_atom[2] (sequence_index=2)
...
vocab_atom → token_atom[vocab_size-1] (sequence_index=vocab_size-1)
```

### Model → Vocabulary Composition
```python
# Link vocabulary to model
model_atom → vocab_atom (sequence_index=0)
```

## Tensor Relationships

### Tensor → Weight Compositions
```python
# For each tensor: "model.layers.0.attention.wq"
# Weights are stored in order (can reconstruct tensor):
tensor_atom[wq] → weight_atom[0.12345] (sequence_index=0)
tensor_atom[wq] → weight_atom[0.00000] (sequence_index=1)  # Sparse
tensor_atom[wq] → weight_atom[0.67890] (sequence_index=2)
...
```

**Deduplication**: If multiple positions have value `0.0`, they all reference the **same weight atom**.

**RLE Compression**: Consecutive identical values are run-length encoded:
```python
# Instead of:
# [0.5, 0.5, 0.5, 0.5, 0.5] → 5 atoms, 5 rows

# We store:
# Run atom: {value: 0.5, run_length: 5} → 1 atom, 1 row
```

### Model → Tensor Compositions
```python
# Link each tensor to model in order
model_atom → tensor_atom["model.layers.0.attention.wq"] (sequence_index=0)
model_atom → tensor_atom["model.layers.0.attention.wk"] (sequence_index=1)
model_atom → tensor_atom["model.layers.0.attention.wv"] (sequence_index=2)
...
```

## Architecture Relationships

### Architecture → Parameter Compositions
```python
# Architecture root contains all hyperparameters
arch_atom → param_atom["num_layers"] (sequence_index=0)
arch_atom → param_atom["hidden_size"] (sequence_index=1)
arch_atom → param_atom["num_attention_heads"] (sequence_index=2)
...
```

### Model → Architecture Composition
```python
# Link architecture to model
model_atom → arch_atom (sequence_index=1)
```

## Semantic Embeddings

### 3D Spatial Coordinates

Each token atom has **semantic embedding coordinates** stored in metadata:

```json
{
  "modality": "tokenizer/vocabulary",
  "token_id": 123,
  "semantic_coords": [0.234, -0.567, 0.891]  // PCA-reduced to 3D
}
```

These coordinates enable:
- **Geometric similarity queries**: Find tokens near "king" in semantic space
- **Clustering validation**: 6.7x compression via spatial proximity
- **Cross-model semantic alignment**: Same token in different models has similar coords

### Spatial Keys (PostGIS Point)

Tokens also have **PostGIS spatial keys** for efficient geometric queries:

```sql
SELECT canonical_text, metadata->>'token_id'
FROM atom
WHERE spatial_key IS NOT NULL
  AND ST_DWithin(
    spatial_key,
    ST_MakePoint(-0.234, 0.567, 0.891),  -- Query point
    0.1  -- Distance threshold
  )
ORDER BY ST_Distance(spatial_key, ST_MakePoint(...))
LIMIT 10;
```

## Cross-Model Deduplication

### Character Deduplication
```
Model A: token "hello" → chars ['h','e','l','l','o']
Model B: token "world" → chars ['w','o','r','l','d']

Shared: 'l' and 'o' reference THE SAME atoms
Dedup ratio: 9 chars stored / 7 unique atoms = 1.3x
```

### Weight Deduplication
```
Model A: tensor[0] = 0.0, tensor[1] = 0.0, tensor[2] = 0.0
Model B: tensor[50] = 0.0, tensor[51] = 0.0

Shared: All five 0.0 values reference THE SAME weight atom
Dedup ratio: 5 stored / 1 unique atom = 5.0x
```

Real-world example (Qwen3-30B, 2 tensors):
- Total weights: 589,952
- Unique atoms: 344
- **Deduplication: 1715x** (99.94% reduction)

### Architecture Deduplication
```
Qwen3-30B-A: {num_layers: 64, hidden_size: 4096, ...}
Qwen3-30B-B: {num_layers: 64, hidden_size: 4096, ...}

Shared: ALL architecture atoms (same config hash)
Dedup ratio: 2 models / 1 architecture = 2.0x
```

## Reconstruction

### Rebuilding Tokens
```sql
-- Get all characters for token 123 in order
SELECT c.canonical_text
FROM atom_composition ac
JOIN atom c ON c.atom_id = ac.component_atom_id
WHERE ac.parent_atom_id = (
    SELECT atom_id FROM atom 
    WHERE metadata->>'token_id' = '123'
)
ORDER BY ac.sequence_index;
-- Result: ['h','e','l','l','o'] → "hello"
```

### Rebuilding Tensors
```sql
-- Get all weights for tensor "model.layers.0.attention.wq" in order
SELECT w.canonical_text::float
FROM atom_composition ac
JOIN atom w ON w.atom_id = ac.component_atom_id
WHERE ac.parent_atom_id = (
    SELECT atom_id FROM atom 
    WHERE canonical_text = 'model.layers.0.attention.wq'
)
ORDER BY ac.sequence_index;
-- Result: [0.12345, 0.00000, 0.67890, ...]
-- Reshape using metadata->>'shape' → torch.tensor(...)
```

### Rebuilding Models
```sql
-- Get all tensors for model "Qwen3-30B"
SELECT t.canonical_text AS tensor_name,
       t.metadata->>'shape' AS shape,
       t.metadata->>'dtype' AS dtype
FROM atom_composition ac
JOIN atom t ON t.atom_id = ac.component_atom_id
WHERE ac.parent_atom_id = (
    SELECT atom_id FROM atom 
    WHERE canonical_text = 'Qwen3-30B'
)
AND t.metadata->>'modality' = 'tensor'
ORDER BY ac.sequence_index;
-- Result: List of all tensors with metadata
-- For each tensor: Rebuild weights from compositions
```

## Reference Counting

Every composition increments `reference_count` on component atoms:

```python
# Token "hello" uses char 'l' twice
char_atom['l'].reference_count += 2

# 1000 tokens use char 'e'
char_atom['e'].reference_count += 1000
```

**Usage**:
- Garbage collection: Delete atoms with `reference_count = 0`
- Popularity analysis: Most reused atoms are compression wins
- Cross-model sharing: High refcounts indicate shared patterns

## Query Examples

### Find all models using a specific weight value
```sql
SELECT DISTINCT m.canonical_text AS model_name
FROM atom w
JOIN atom_composition tc ON tc.component_atom_id = w.atom_id
JOIN atom t ON t.atom_id = tc.parent_atom_id
JOIN atom_composition mc ON mc.component_atom_id = t.atom_id
JOIN atom m ON m.atom_id = mc.parent_atom_id
WHERE w.canonical_text = '0.0'
  AND w.metadata->>'modality' = 'weight'
  AND m.metadata->>'modality' = 'model';
```

### Find tokens semantically similar to "king"
```sql
SELECT canonical_text AS token,
       ST_Distance(
           spatial_key,
           (SELECT spatial_key FROM atom WHERE canonical_text = 'king')
       ) AS semantic_distance
FROM atom
WHERE metadata->>'modality' = 'tokenizer/vocabulary'
  AND spatial_key IS NOT NULL
ORDER BY semantic_distance
LIMIT 20;
-- Result: queen, monarch, ruler, prince, ...
```

### Calculate deduplication ratio for a model
```sql
WITH model_weights AS (
    SELECT COUNT(*) AS total_weights
    FROM atom_composition tc
    JOIN atom t ON t.atom_id = tc.parent_atom_id
    WHERE t.metadata->>'modality' = 'tensor'
      AND tc.parent_atom_id IN (
          SELECT component_atom_id FROM atom_composition
          WHERE parent_atom_id = (
              SELECT atom_id FROM atom WHERE canonical_text = 'Qwen3-30B'
          )
      )
),
unique_weights AS (
    SELECT COUNT(DISTINCT component_atom_id) AS unique_atoms
    FROM atom_composition tc
    JOIN atom t ON t.atom_id = tc.parent_atom_id
    WHERE t.metadata->>'modality' = 'tensor'
)
SELECT 
    total_weights,
    unique_atoms,
    (total_weights::float / unique_atoms) AS dedup_ratio
FROM model_weights, unique_weights;
```

## Benefits of Relationship Maintenance

1. **Lossless Reconstruction**: Can rebuild any model completely from atoms
2. **Cross-Model Deduplication**: Shared atoms reduce storage exponentially
3. **Semantic Queries**: Find patterns across models using spatial/graph traversal
4. **Incremental Updates**: Add new models without re-atomizing existing atoms
5. **Provenance Tracking**: Know which models contributed which atoms
6. **Transformation Primitives**: Merge/prune/quantize by manipulating compositions

## Implementation Details

See:
- `api/services/model_atomization.py` - GGUF relationship creation
- `api/services/safetensors_atomization.py` - SafeTensors relationship creation
- `api/services/base_atomizer.py` - `create_composition()` helper
- `schema/core/atom_composition.sql` - Schema definition
- `schema/triggers/update_refcount.sql` - Automatic reference counting
