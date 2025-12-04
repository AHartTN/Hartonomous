# CORRECT MODEL ATOMIZATION ORDER

**The Right Way: Structure First, Weights as Relations**

---

## The Fundamental Error

**WRONG**: Store connections as composition atoms  
**RIGHT**: Store connections as RELATIONS between pre-populated neuron atoms

## The Three-Phase Pipeline

### Phase 1: Pre-Populate Structural Atoms (FAST)

**Key Insight**: Model structure is KNOWN from metadata. Extract and atomize ALL structural components FIRST.

```python
# Parse model metadata
model_info = parse_gguf_metadata(model_file)
# Result: {
#   'vocab_size': 151646,
#   'num_layers': 32,
#   'hidden_dim': 4096,
#   'attention_heads': 32,
#   'architecture': 'llama-3'
# }

# Phase 1a: Atomize vocabulary (parallel, deterministic positions)
vocabulary_atoms = []
for token_id, token_text in model_info['vocabulary']:
    spatial_key = calculate_vocabulary_spatial_key(
        token_id=token_id,
        text=token_text,
        vocab_size=model_info['vocab_size']
    )
    # X/Y/Z = semantic position based on token characteristics
    # M = Hilbert index computed from (X,Y,Z)
    
    atom = {
        'content_hash': sha256(token_text),
        'atom_value': token_text.encode(),
        'spatial_key': spatial_key,
        'metadata': {
            'token_id': token_id,
            'modality': 'vocabulary',
            'model': model_info['architecture']
        }
    }
    vocabulary_atoms.append(atom)

# Batch insert (151K atoms in seconds)
await db.batch_insert_atoms(vocabulary_atoms)

# Phase 1b: Atomize neurons (parallel, deterministic positions)
neuron_atoms = []
for layer_idx in range(model_info['num_layers']):
    for neuron_idx in range(model_info['hidden_dim']):
        spatial_key = calculate_neuron_spatial_key(
            layer_idx=layer_idx,
            neuron_idx=neuron_idx,
            num_layers=model_info['num_layers'],
            hidden_dim=model_info['hidden_dim']
        )
        
        atom = {
            'content_hash': sha256(f"L{layer_idx}N{neuron_idx}"),
            'canonical_text': f"L{layer_idx}N{neuron_idx}",
            'spatial_key': spatial_key,
            'metadata': {
                'layer': layer_idx,
                'neuron': neuron_idx,
                'modality': 'neuron-id',
                'model': model_info['architecture']
            }
        }
        neuron_atoms.append(atom)

# Batch insert (131K atoms for 32 layers × 4096 neurons, takes seconds)
await db.batch_insert_atoms(neuron_atoms)
```

**Result**: Complete structural skeleton atomized in **< 1 minute**, with **O(1) lookup tables**:
- `vocabulary_lookup[token_id] = atom_id`
- `neuron_lookup[(layer, neuron)] = atom_id`

---

### Phase 2: Stream Weights as Relations (NO MEMORY EXPLOSION)

**Key Insight**: Weights are EDGES in a graph, stored in `atom_relation` table, NOT composition atoms.

```python
# Phase 2: Process each tensor as streaming relations
for tensor_name, tensor_data in model.iter_tensors():
    # Parse tensor name to understand structure
    # Example: "blk.0.attn_q.weight" → layer 0, attention query weights
    layer_idx, tensor_type = parse_tensor_name(tensor_name)
    
    # Get relation type atom (created once, reused)
    relation_type = get_or_create_relation_type(tensor_type)
    
    # Stream through NON-ZERO weights only (sparse optimization)
    batch_relations = []
    for i, j, value in iter_nonzero_weights(tensor_data):
        # O(1) lookup for source/target neuron atoms
        source_atom_id = neuron_lookup[(layer_idx, i)]
        target_atom_id = neuron_lookup[(layer_idx, j)]
        
        # Create relation (edge in graph)
        relation = {
            'source_atom_id': source_atom_id,
            'target_atom_id': target_atom_id,
            'relation_type_id': relation_type,
            'weight': float(value),  # Model weight = relation strength
            'metadata': {
                'tensor': tensor_name,
                'position': [i, j],
                'layer': layer_idx
            }
        }
        batch_relations.append(relation)
        
        # Batch insert every 10K relations
        if len(batch_relations) >= 10000:
            await db.batch_insert_relations(batch_relations)
            batch_relations.clear()
    
    # Insert remaining
    if batch_relations:
        await db.batch_insert_relations(batch_relations)
```

**Result**: 
- **No memory explosion**: Never build 53M-element arrays, stream in batches
- **Sparse automatic**: Only create relations for non-zero weights
- **O(1) lookups**: Pre-populated atoms enable instant source/target resolution
- **Spatial queries work**: All atoms have positions, PostGIS queries enabled immediately

---

### Phase 3: BPE on Topology (OPTIONAL)

**Key Insight**: Find repeated SUBGRAPH patterns across layers, not flat value sequences.

```python
# Phase 3: Crystallize topology patterns
# Find repeated connection patterns that appear across multiple layers

# Example: Same "attention head" pattern in layers 0, 5, 12
pattern_subgraph = find_common_subgraph([
    relations_for_layer[0],
    relations_for_layer[5],
    relations_for_layer[12]
])

if pattern_subgraph.frequency > threshold:
    # Create composition atom representing this pattern
    pattern_atom = {
        'composition_ids': [rel.atom_id for rel in pattern_subgraph.relations],
        'metadata': {
            'type': 'attention-head-pattern',
            'layers': [0, 5, 12],
            'frequency': pattern_subgraph.frequency
        }
    }
    await db.insert_atom(pattern_atom)
```

**Result**: Hierarchical compression of GRAPH STRUCTURE, not values.

---

## Why This Order Matters

### ❌ Wrong Order (Current Code)
```python
# 1. Flatten tensor
weights = tensor.flatten()  # 53M values in memory

# 2. Find unique values
unique_weights = np.unique(weights)  # 500K unique values

# 3. Try to build sequence
for i, weight_value in enumerate(weights):
    weight_atom_id = unique_weight_lookup[weight_value]
    sequence.append(weight_atom_id)  # 53M-element array → HANGS

# PROBLEMS:
# - Lost structural information (which neurons?)
# - Can't create relations (no source/target atoms)
# - Memory explosion (building 53M sequences)
# - No spatial queries (no positions yet)
```

### ✅ Right Order (This Document)
```python
# 1. Pre-populate structure
vocab_atoms = atomize_vocabulary(151K tokens)      # Fast, parallel
neuron_atoms = atomize_neurons(32 layers × 4096)   # Fast, parallel

# 2. Stream relations
for i, j, value in nonzero_weights(tensor):
    insert_relation(neuron_atoms[i], neuron_atoms[j], value)  # O(1) lookup

# 3. BPE on topology
find_repeated_subgraphs(relations)

# BENEFITS:
# - Structural context preserved (source/target neurons known)
# - Relations enable graph queries
# - No memory explosion (stream in batches)
# - Spatial queries work immediately (all atoms positioned)
# - Sparse optimization automatic (only insert non-zero)
```

---

## Database Schema Mapping

### Structural Atoms → `atom` Table
```sql
-- Vocabulary atoms (pre-populated)
INSERT INTO atom (content_hash, atom_value, spatial_key, metadata)
VALUES (
  sha256('hello'),
  'hello'::bytea,
  ST_SetSRID(ST_MakePoint(x, y, z, m), 0),  -- Deterministic from token_id
  '{"token_id": 42, "modality": "vocabulary"}'::jsonb
);

-- Neuron atoms (pre-populated)
INSERT INTO atom (content_hash, canonical_text, spatial_key, metadata)
VALUES (
  sha256('L0N1024'),
  'L0N1024',
  ST_SetSRID(ST_MakePoint(x, y, z, m), 0),  -- Deterministic from layer/neuron
  '{"layer": 0, "neuron": 1024, "modality": "neuron-id"}'::jsonb
);
```

### Neural Connections → `atom_relation` Table
```sql
-- Weight as relation strength
INSERT INTO atom_relation (source_atom_id, target_atom_id, relation_type_id, weight, metadata)
VALUES (
  12345,  -- Source neuron atom (L0N512)
  67890,  -- Target neuron atom (L0N1024)
  42,     -- Relation type: 'attention_weight'
  0.0173, -- Model weight value = relation strength
  '{"tensor": "blk.0.attn_q.weight", "position": [512, 1024]}'::jsonb
);
```

### Topology Patterns → `atom` Table (composition_ids)
```sql
-- Repeated subgraph pattern (optional, phase 3)
INSERT INTO atom (content_hash, composition_ids, metadata)
VALUES (
  sha256(pattern_signature),
  ARRAY[rel1_atom, rel2_atom, rel3_atom, ...],  -- Relations forming pattern
  '{"type": "attention-head-pattern", "layers": [0, 5, 12]}'::jsonb
);
```

---

## Implementation Checklist

- [ ] **Phase 1a**: Implement `pre_populate_vocabulary()`
  - Extract vocabulary from GGUF metadata
  - Calculate deterministic spatial keys for all tokens
  - Batch insert vocabulary atoms
  - Build `vocabulary_lookup[token_id] → atom_id` dict

- [ ] **Phase 1b**: Implement `pre_populate_neurons()`
  - Extract layer/neuron structure from metadata
  - Calculate deterministic spatial keys for all neurons
  - Batch insert neuron atoms
  - Build `neuron_lookup[(layer, neuron)] → atom_id` dict

- [ ] **Phase 2**: Refactor `gguf_atomizer.py`
  - Remove `flatten()` and `unique()` calls
  - Implement streaming tensor processor
  - Use `iter_nonzero_weights()` for sparse iteration
  - Batch insert relations (10K at a time)
  - Use pre-populated lookup tables for O(1) atom resolution

- [ ] **Phase 3**: Implement `crystallize_topology()`
  - Query relations by layer
  - Find common subgraph patterns
  - Create composition atoms for repeated patterns
  - Store pattern metadata (frequency, layers)

- [ ] **Universal Pattern**: Apply to other atomizers
  - Text: Pre-populate character/token atoms → relations for sequences
  - Images: Pre-populate color atoms → relations for pixel adjacency
  - Video: Pre-populate frame atoms → relations for temporal flow

---

## Expected Performance

**Before** (wrong order):
```
Vocabulary:    NOT pre-populated
Neurons:       NOT pre-populated
Weight tensor: Flatten 53M → Unique 500K → Build 53M sequence → HANGS
Time:          NEVER COMPLETES (memory explosion)
```

**After** (correct order):
```
Phase 1a: Atomize 151K tokens        → 10 seconds   (batch insert, parallel)
Phase 1b: Atomize 131K neurons       → 5 seconds    (batch insert, parallel)
Phase 2:  Stream 16M relations       → 2 minutes    (batched, streaming)
Phase 3:  Find topology patterns     → 30 seconds   (optional, graph queries)
-------------------------------------------------------------------
Total:                                 < 3 minutes   (vs. never completing)
```

**Storage**:
```
Vocabulary atoms:     151K × 200 bytes  ≈ 30 MB
Neuron atoms:         131K × 200 bytes  ≈ 26 MB
Weight relations:     16M × 100 bytes   ≈ 1.6 GB (non-zero only, sparse!)
Pattern compositions: 10K × 500 bytes   ≈ 5 MB
-------------------------------------------------------------------
Total:                                    ≈ 1.7 GB (vs. 32 GB for full dense matrix)
```

---

## Key Takeaways

1. **Structure is KNOWN**: Don't discover it from weights, extract it from metadata
2. **Pre-populate FIRST**: Vocabulary and neurons before processing any weights
3. **Weights are RELATIONS**: Store in `atom_relation`, not as composition atoms
4. **Stream, don't load**: Never build 53M-element arrays in memory
5. **Sparse is FREE**: Only insert relations for non-zero weights
6. **O(1) lookups**: Pre-populated atoms enable instant source/target resolution
7. **Spatial queries work**: All atoms positioned immediately, PostGIS enabled
8. **Universal pattern**: Applies to ALL data types (text, images, models, etc.)

**The correct order**: Structure → Relations → Patterns  
**NOT**: Values → Sequences → Compositions ❌
