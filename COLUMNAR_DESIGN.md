# Columnar Processing Design for Weight Atomization

## Problem
Current approach:
1. NumPy array → Python list
2. List → Decimal objects
3. Decimal → Dict cache
4. Dict → Arrays for SQL
5. Multiple conversions, intermediate allocations

## Solution: Columnar Arrays Throughout

### Phase 1: GPU Processing (Already Columnar)
```python
# weights_gpu is already CuPy array (columnar)
unique_values_gpu = cp.unique(weights_gpu)  # Still columnar
sparse_mask_gpu = abs_weights_gpu < threshold  # Columnar boolean mask
non_sparse_weights_gpu = weights_gpu[~sparse_mask_gpu]  # Columnar subset
```

###Phase 2: Build Columnar Data for Atoms
Instead of dict cache, use:
```python
# Columnar arrays for atom table
atom_hashes = np.array([sha256(w) for w in unique_weights])  # bytea[]
atom_texts = np.array([str(w) for w in unique_weights])      # text[]
atom_metadata = '{"modality": "weight"}'                      # jsonb (constant)

# Single COPY operation
with cursor.copy("COPY atom (content_hash, canonical_text, metadata) FROM STDIN") as copy:
    for i in range(len(unique_weights)):
        copy.write_row((atom_hashes[i], atom_texts[i], atom_metadata))
```

### Phase 3: Map Weights to Atoms (Columnar Index)
Instead of dict lookup, use NumPy searchsorted:
```python
# Sort unique weights and their atom IDs
sorted_indices = np.argsort(unique_weights_np)
sorted_weights = unique_weights_np[sorted_indices]
sorted_atom_ids = atom_ids_np[sorted_indices]

# For each weight, find its atom_id (binary search)
indices = np.searchsorted(sorted_weights, all_weights_np)
weight_atom_ids = sorted_atom_ids[indices]  # Columnar mapping!
```

### Phase 4: Build Compositions (Columnar)
```python
# Already have:
# - non_sparse_indices (array of positions)
# - weight_atom_ids (array of atom IDs)

# Compositions are just:
composition_parent_ids = np.full(len(non_sparse_indices), tensor_atom_id)
composition_component_ids = weight_atom_ids[non_sparse_indices]
composition_sequence_indices = non_sparse_indices

# Single COPY operation
with cursor.copy("COPY composition (parent_atom_id, component_atom_id, sequence_idx) FROM STDIN") as copy:
    for i in range(len(non_sparse_indices)):
        copy.write_row((
            composition_parent_ids[i],
            composition_component_ids[i],
            composition_sequence_indices[i]
        ))
```

## Benefits
1. **No intermediate Python objects** - stay in NumPy/CuPy
2. **Use PostgreSQL COPY** - 100-200x faster than INSERT
3. **Vectorized lookups** - np.searchsorted instead of dict
4. **Less memory** - no dict overhead, just arrays
5. **GPU-friendly** - can keep more on GPU longer

## Migration Strategy
1. Keep current stored procedure for now
2. Replace _atomize_weight_batch() to use COPY
3. Replace dict cache with sorted arrays
4. Use np.searchsorted() for lookups
5. Batch compositions with COPY

## Performance Estimate
- Current: Dict lookup O(1) but Python overhead
- Columnar: Binary search O(log n) but vectorized
- For 50M weights with 1K unique: 
  - Dict: 50M * (Python overhead)
  - NumPy: 50M lookups vectorized in ~0.1s
  - Plus COPY vs INSERT: 100-200x speedup

Total expected: **10-50x faster**
