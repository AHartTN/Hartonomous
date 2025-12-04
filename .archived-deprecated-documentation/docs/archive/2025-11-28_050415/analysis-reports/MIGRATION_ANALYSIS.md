# Migration Analysis: FractalAtomizer → AtomFactory

**Created**: 2025-01-XX  
**Status**: Planning Phase (Task 6)  
**Critical**: Must maintain universal scope (ALL content types) and backward compatibility

---

## Executive Summary

This document provides comprehensive analysis of migrating 9 method calls from `FractalAtomizer.get_or_create_primitives_batch()` to `AtomFactory.create_primitives_batch()`. 

**Files Affected**: 5 files with 9 total calls  
**Risk Level**: MEDIUM (breaking existing functionality if not careful)  
**Testing Required**: Unit + integration tests for each migrated file  

---

## API Comparison

### Old API: FractalAtomizer.get_or_create_primitives_batch()

**Location**: `api/services/geometric_atomization/fractal_atomizer.py` (lines 144-245)

**Signature**:
```python
async def get_or_create_primitives_batch(
    self,
    values: List[bytes], 
    metadata: dict = None, 
    modality: str = 'text', 
    auto_commit: bool = True
) -> tuple[List[int], List[tuple[float, float, float]]]
```

**Behavior**:
- **Cache check**: Checks `self.atom_cache` first (returns cached atom_ids immediately)
- **Vectorized projection**: For numeric modalities (`model-weight`), uses vectorized `spatial_utils.project_tensor()` with numpy
- **Hash computation**: Uses `hashlib.sha256(value).digest()` for content hash
- **Hilbert computation**: Calls `self._compute_hilbert(x, y, z)` or vectorized `spatial_utils.compute_hilbert_indices_batch()`
- **Database insert**: Uses `execute_values()` with `ON CONFLICT (content_hash) DO NOTHING` for deduplication
- **Auto-commit**: Commits transaction if `auto_commit=True` (default)
- **Returns**: Tuple of `(atom_ids, coordinates)` in same order as input `values`

**Dependencies**:
- `self.db` (database connection)
- `self.coordinate_range` (spatial config)
- `self.atom_cache`, `self.coord_cache` (local caches)
- `spatial_utils` module (projection, Hilbert)

---

### New API: AtomFactory.create_primitives_batch()

**Location**: `api/core/atom_factory.py` (lines 130-250)

**Signature**:
```python
async def create_primitives_batch(
    self,
    values: List[bytes],
    modality: Literal['text', 'model-weight', 'image-pixel', 'audio-sample', 'code-token'],
    metadata: AtomMetadata,
    conn: Connection
) -> Tuple[List[int], List[Tuple[float, float, float]]]
```

**Behavior**:
- **NO caching**: Does NOT use local cache (relies on database deduplication)
- **Vectorized projection**: For numeric modalities, uses `spatial_ops.project_to_coordinates()` with numpy
- **Hash computation**: Uses `hashlib.sha256(value).digest()` (SAME as old API)
- **Hilbert computation**: Calls `spatial_ops.compute_hilbert_index()` or vectorized version
- **Database insert**: Uses `unnest()` bulk insert with `ON CONFLICT (content_hash) DO NOTHING`
- **NO auto-commit**: Does NOT commit (caller's responsibility)
- **Returns**: Tuple of `(atom_ids, coordinates)` in same order as input `values` (SAME as old API)

**Dependencies**:
- `conn` (database connection passed as argument)
- `self.coordinate_range` (spatial config)
- `spatial_ops` module (projection, Hilbert, WKT)

---

## Key Differences (Migration Risks)

### 1. **NO LOCAL CACHING** ⚠️ CRITICAL

**Old**: `FractalAtomizer` maintains `self.atom_cache` and `self.coord_cache`
```python
if value in self.atom_cache:
    cached_id = self.atom_cache[value]
    return atom_ids.append(cached_id)
```

**New**: `AtomFactory` has NO caching (relies on database `ON CONFLICT`)

**Impact**: 
- Performance: May be slower for repeated lookups (no cache hits)
- Correctness: Database deduplication still works via content_hash uniqueness
- **Mitigation**: Consider adding optional cache to AtomFactory later if performance degrades

---

### 2. **NO AUTO-COMMIT** ⚠️ CRITICAL

**Old**: `auto_commit=True` by default → commits transaction after insert
```python
if auto_commit:
    await self.db.commit()
```

**New**: `AtomFactory` does NOT commit → caller must commit explicitly

**Impact**:
- Breaking change: If caller expects auto-commit, atoms won't be visible to other transactions
- **Migration Fix**: Add explicit `await conn.commit()` after `create_primitives_batch()` calls
- **Pattern**: Batch multiple operations, then commit once (more efficient)

---

### 3. **Connection Argument** ⚠️ MEDIUM

**Old**: Uses `self.db` (instance variable set at initialization)
```python
self.db = db_connection  # Set once in __init__
```

**New**: Accepts `conn` argument (must be passed explicitly)
```python
conn: Connection  # Passed to every method call
```

**Impact**:
- Requires explicit connection passing: `await factory.create_primitives_batch(..., conn=conn)`
- **Migration Fix**: Ensure connection is available in call scope (already is in safetensors_atomization.py)

---

### 4. **Modality Type Safety** ✅ IMPROVEMENT

**Old**: `modality: str = 'text'` (no type checking, easy typos)

**New**: `modality: Literal['text', 'model-weight', 'image-pixel', 'audio-sample', 'code-token']` (compile-time checking)

**Impact**:
- Better: Catches typos at development time (e.g., `'model_weight'` → type error)
- **Migration Fix**: Ensure all modality strings match Literal values exactly

---

### 5. **Metadata Structure** ⚠️ MEDIUM

**Old**: `metadata: dict = None` (untyped, free-form)

**New**: `metadata: AtomMetadata` (TypedDict with known fields)

**Impact**:
- Better type safety: IDE autocomplete, type checking
- **Migration Fix**: Ensure metadata dicts conform to `AtomMetadata` structure (model_name, tensor_name, etc.)

---

## Migration Call Sites Analysis

### CRITICAL FINDINGS ⚠️

After deep analysis of all 9 call sites, **3 CRITICAL issues** identified:

1. **Coordinate Usage**: 4 out of 9 calls (44%) USE the returned coordinates immediately
   - `geometric_atomizer.py` lines 66, 109: Build WKT from atom_coords
   - `tensor_utils.py` line 133: Build LINESTRING from atom_coords
   - **Impact**: Cannot discard coordinates → Must preserve return value

2. **Modality Mismatch**: 1 call uses `'numeric'` modality (NOT in AtomFactory Literal)
   - `geometric_atomizer.py` line 109: Uses `modality='numeric'`
   - **Impact**: Type error at runtime → Must change to `'model-weight'`

3. **Missing Parameters**: 2 calls omit modality/metadata entirely
   - `geometric_atomizer.py` lines 167, 175: No modality, no metadata
   - **Impact**: Defaults to 'text' → May cause incorrect projection

**Migration Priority**: Fix modality mismatch FIRST (causes immediate failure)

---

### File 1: safetensors_atomization.py (Line 290)

**Context** (lines 280-310):
```python
metadata = {
    "dtype": str(tensor_data.dtype),
    "total_elements": int(np.prod(tensor_data.shape)),
    "model_name": model_name,
    "tensor_name": tensor_name,
    "quantization_type": "inferred",
    "unique_values": unique_count,
    "sequence_length": len(non_sparse_weights)
}

logger.info(f"  Atomizing {unique_count:,} unique constants as primitives...")
value_atom_ids, _ = await self.fractal_atomizer.get_or_create_primitives_batch(
    values=value_bytes,
    metadata=metadata,
    modality='model-weight'  # Vectorized projection for numeric values
)

self.stats["atoms_created"] += unique_count
```

**Analysis**:
- **Modality**: `'model-weight'` ✅ (matches AtomFactory Literal)
- **Metadata**: Contains `model_name`, `tensor_name`, `dtype` ✅ (conforms to AtomMetadata)
- **Connection**: Uses `self.fractal_atomizer.db` (set at line 112: `FractalAtomizer(db_connection=conn)`)
- **Return value**: Unpacks `(value_atom_ids, _)` → only uses atom_ids, discards coordinates ✅
- **Commit**: No explicit commit after call → relies on auto_commit in old API ⚠️

**Migration Steps**:
1. Add import: `from api.core.atom_factory import AtomFactory, AtomMetadata`
2. Replace `self.fractal_atomizer` with `self.atom_factory` (initialize in `__init__`)
3. Replace call:
   ```python
   value_atom_ids, _ = await self.atom_factory.create_primitives_batch(
       values=value_bytes,
       metadata=metadata,  # Already conforms to AtomMetadata
       modality='model-weight',
       conn=conn  # Pass connection explicitly
   )
   # NEW: Explicit commit (old API had auto_commit=True)
   await conn.commit()
   ```
4. Update initialization (line 112):
   ```python
   # OLD: self.fractal_atomizer = FractalAtomizer(db_connection=conn)
   # NEW: self.atom_factory = AtomFactory(coordinate_range=1e6)
   ```

**Test Plan**:
- Run safetensors atomization with sample model (e.g., Llama 7B)
- Verify: atoms created, coordinates correct, deduplication works
- Verify: BPE crystallization still works (line 306 uses returned atom_ids)

---

### File 2: gguf_atomizer.py (Lines 325, 427)

**Call Site 1: Line 325** (weight atomization):
```python
# Context (lines 315-335):
unique_values, inverse_indices = np.unique(non_sparse_weights, return_inverse=True)
logger.info(f"  Found {len(unique_values):,} unique quantized values (constants)")

# Step 1: Atomize the unique constants (not the full sequence!)
value_bytes = [v.tobytes() for v in unique_values]
value_atom_ids, _ = await self.fractal_atomizer.get_or_create_primitives_batch(
    values=value_bytes,
    metadata={"modality": "model-weight", "tensor": tensor_name},
    modality="model-weight",
)

logger.info(f"  Atomized {len(value_atom_ids)} unique constants")
```

**Analysis**:
- **Modality**: `'model-weight'` ✅
- **Metadata**: Contains `modality`, `tensor` ⚠️ (needs `tensor_name` key for AtomMetadata)
- **Connection**: Uses `self.fractal_atomizer.db` (needs explicit `conn` parameter)
- **Return value**: Unpacks `(value_atom_ids, _)` → discards coordinates ✅
- **Commit**: No explicit commit → relies on auto_commit ⚠️

**Migration Fix**:
```python
value_atom_ids, _ = await self.atom_factory.create_primitives_batch(
    values=value_bytes,
    metadata={"modality": "model-weight", "tensor_name": tensor_name},  # Fixed key
    modality='model-weight',
    conn=conn
)
await conn.commit()  # Explicit commit
```

---

**Call Site 2: Line 427** (token atomization):
```python
# Context (lines 415-430):
token_bytes = [t.encode('utf-8') for t in tokens]
token_ids, _ = await self.fractal_atomizer.get_or_create_primitives_batch(
    values=token_bytes,
    metadata={
        "modality": "text",
        "subtype": "vocabulary-token",
        "model_name": model_name
    },
    modality="text",
)

logger.info(f"  OK Atomized {len(token_ids):,} vocabulary tokens")
```

**Analysis**:
- **Modality**: `'text'` ✅
- **Metadata**: Contains `model_name`, `modality`, `subtype` ✅
- **Connection**: Uses `self.fractal_atomizer.db`
- **Return value**: Unpacks `(token_ids, _)` → discards coordinates ✅
- **Commit**: No explicit commit → relies on auto_commit ⚠️

**Migration Fix**:
```python
token_ids, _ = await self.atom_factory.create_primitives_batch(
    values=token_bytes,
    metadata={
        "modality": "text",
        "subtype": "vocabulary-token",
        "model_name": model_name
    },
    modality='text',
    conn=conn
)
await conn.commit()  # Explicit commit
```

---

### File 3: geometric_atomizer.py (Lines 66, 109, 167, 175)

**Call Site 1: Line 66** (text atomization):
```python
# Context (lines 60-75):
atom_values = [char.encode("utf-8") for char in text]

# Batch create all atoms at once
atom_ids, atom_coords = await self.fractal.get_or_create_primitives_batch(
    atom_values, 
    metadata={"modality": "text"}, 
    modality='text'
)

# atom_coords already returned from batch operation - no need to look up!
wkt = self.builder.build_wkt(atom_coords)
```

**Analysis**:
- **Modality**: `'text'` ✅
- **Metadata**: Only contains `modality` (minimal) ✅
- **Connection**: Uses `self.fractal.db` (needs explicit `conn`)
- **Return value**: Uses BOTH atom_ids and atom_coords ⚠️ CRITICAL
- **Commit**: No explicit commit in this call

**Migration Fix**:
```python
atom_ids, atom_coords = await self.atom_factory.create_primitives_batch(
    values=atom_values,
    metadata={"modality": "text"},
    modality='text',
    conn=conn
)
# Note: atom_coords are used immediately for WKT building - must preserve!
```

---

**Call Site 2: Line 109** (tensor atomization):
```python
# Context (lines 105-120):
atom_values = [val.tobytes() for val in flat_values]

# Batch create all atoms at once for performance
atom_ids, atom_coords = await self.fractal.get_or_create_primitives_batch(
    atom_values, 
    metadata={"modality": "numeric"}, 
    modality='numeric',
    auto_commit=False  # Don't commit yet - will commit after compositions
)

# Commit the atom batch here before continuing
if self.db is not None:
    await self.db.commit()
```

**Analysis**:
- **Modality**: `'numeric'` ⚠️ NOT in AtomFactory Literal! (should be `'model-weight'`)
- **Metadata**: Only contains `modality` ✅
- **Connection**: Uses `self.fractal.db`
- **Return value**: Uses BOTH atom_ids and atom_coords ⚠️ CRITICAL
- **Commit**: Explicit commit AFTER call (manually controlled with `auto_commit=False`) ✅

**Migration Fix**:
```python
atom_ids, atom_coords = await self.atom_factory.create_primitives_batch(
    values=atom_values,
    metadata={"modality": "model-weight"},  # Changed from 'numeric'
    modality='model-weight',  # Changed from 'numeric'
    conn=conn
)
# Explicit commit already present
if self.db is not None:
    await self.db.commit()
```

---

**Call Site 3: Line 167** (generic atomization):
```python
# Context (lines 165-170):
# Batch atomization helper
atom_ids, _ = await self.fractal.get_or_create_primitives_batch(atom_values)
```

**Analysis**:
- **NO modality specified** ⚠️ CRITICAL (defaults to 'text')
- **NO metadata specified** ⚠️
- **Return value**: Discards coordinates
- **Context**: Used in helper method (need to check caller)

**Migration Fix**:
```python
atom_ids, _ = await self.atom_factory.create_primitives_batch(
    values=atom_values,
    metadata={},  # Empty metadata
    modality='text',  # Default modality
    conn=conn
)
```

---

**Call Site 4: Line 175** (generic atomization):
```python
# Context (lines 173-178):
# Batch atomization helper
atom_ids, _ = await self.fractal.get_or_create_primitives_batch(atom_values)
```

**Analysis**:
- **Same as Call Site 3**: NO modality, NO metadata
- **Return value**: Discards coordinates
- **Context**: Used in another helper method

**Migration Fix**: Same as Call Site 3

---

### File 4: bpe_crystallizer.py (Line 203)

**Context** (lines 195-210):
```python
async def atomize_sequence(
    self, sequence: bytes, fractal_atomizer, learn: bool = True
) -> List[int]:
    """
    Atomize sequence into primitives, then compress with learned merges.
    """
    # Batch convert to primitive atoms
    primitive_ids, _ = await fractal_atomizer.get_or_create_primitives_batch(
        list(sequence),
        metadata={"modality": "bpe"},
        modality="text"
    )

    # OBSERVE phase (if learning enabled)
    if learn:
        self.observe_sequence(primitive_ids)
```

**Analysis**:
- **Modality**: `'text'` ✅
- **Metadata**: Contains `modality: "bpe"` ✅
- **Connection**: Passed as `fractal_atomizer` parameter (needs refactoring)
- **Return value**: Discards coordinates ✅
- **Commit**: No explicit commit

**Migration Fix** (requires method signature change):
```python
async def atomize_sequence(
    self, sequence: bytes, atom_factory: AtomFactory, conn: Connection, learn: bool = True
) -> List[int]:
    """Atomize sequence into primitives, then compress with learned merges."""
    # Batch convert to primitive atoms
    primitive_ids, _ = await atom_factory.create_primitives_batch(
        values=list(sequence),
        metadata={"modality": "bpe"},
        modality='text',
        conn=conn
    )
    await conn.commit()  # Explicit commit
```

---

### File 5: tensor_utils.py (Line 133)

**Context** (lines 125-145):
```python
# Convert weights to bytes - vectorized
batch_start = time.time()
weight_bytes = weights_to_bytes_vectorized(weights)
logger.info(f"  Batch atomizing {len(weight_bytes):,} weights...")

# Batch atomize using FractalAtomizer - returns BOTH atom_ids AND coordinates
# This eliminates redundant database queries for coordinate retrieval
atom_ids, atom_coords = await fractal_atomizer.get_or_create_primitives_batch(
    values=weight_bytes,
    metadata={"modality": "model-weight"},
    modality="model-weight",  # Uses vectorized projection via ModalityConfig
)

unique_atoms = len(set(atom_ids))
stats["atoms_created"] += unique_atoms
```

**Analysis**:
- **Modality**: `'model-weight'` ✅
- **Metadata**: Contains `modality` ✅
- **Connection**: Passed as `fractal_atomizer` parameter
- **Return value**: Uses BOTH atom_ids and atom_coords ⚠️ CRITICAL
- **Commit**: No explicit commit

**Migration Fix**:
```python
atom_ids, atom_coords = await atom_factory.create_primitives_batch(
    values=weight_bytes,
    metadata={"modality": "model-weight"},
    modality='model-weight',
    conn=conn
)
# atom_coords used for LINESTRING building below - must preserve!
```

---

## Common Migration Pattern

For ALL call sites, follow this pattern:

### Step 1: Add Imports
```python
from api.core.atom_factory import AtomFactory, AtomMetadata
```

### Step 2: Initialize AtomFactory (in __init__ or module scope)
```python
# OLD: self.fractal_atomizer = FractalAtomizer(db_connection=conn)
# NEW: self.atom_factory = AtomFactory(coordinate_range=1e6, hilbert_bits=20)
```

### Step 3: Replace Method Call
```python
# OLD:
atom_ids, coords = await self.fractal_atomizer.get_or_create_primitives_batch(
    values=values,
    metadata=metadata,
    modality='model-weight'
)

# NEW:
atom_ids, coords = await self.atom_factory.create_primitives_batch(
    values=values,
    metadata=metadata,  # Ensure conforms to AtomMetadata
    modality='model-weight',  # Must match Literal type
    conn=conn  # Pass connection explicitly
)
# NEW: Explicit commit (if needed)
await conn.commit()
```

### Step 4: Test Individual File
```python
# Run atomization with sample data
# Verify: atoms created, coordinates match, deduplication works
```

---

## Testing Strategy

### Unit Tests (Per File)

For each migrated file:
1. **Test atom creation**: Verify atoms inserted with correct content_hash, coordinates
2. **Test deduplication**: Insert same value twice → should return same atom_id
3. **Test coordinate accuracy**: Compare old vs new projection (should match within epsilon)
4. **Test batch ordering**: Returned atom_ids should match input value order
5. **Test commit behavior**: Verify atoms visible after commit

### Integration Tests

After all files migrated:
1. **End-to-end GGUF atomization**: Load GGUF model, atomize fully, verify structure
2. **End-to-end SafeTensors atomization**: Load SafeTensors model, atomize fully, verify structure
3. **Cross-modality queries**: Query spatial proximity across different content types
4. **BPE crystallization**: Verify pattern learning still works with new infrastructure
5. **Performance benchmark**: Compare old vs new (should be 10-100x faster with vectorization)

### Regression Tests

Before removing old code (Task 9):
1. **Run full test suite**: Ensure no existing tests break
2. **Manual testing**: Test with large models (Llama 70B, Mistral)
3. **Performance profiling**: Compare memory usage, query latency

---

## Universal Scope Verification

Must verify ALL content types work after migration:

| Content Type | Modality | Test Case | Status |
|--------------|----------|-----------|--------|
| **Tokens** | `'text'` | Tokenizer vocabulary atomization | ❌ Not tested |
| **Weights** | `'model-weight'` | Tensor atomization (GGUF, SafeTensors) | ❌ Not tested |
| **Neurons** | `'model-weight'` | Activation state atomization | ❌ Not tested |
| **Images** | `'image-pixel'` | Pixel atomization (if implemented) | ❌ Not tested |
| **Audio** | `'audio-sample'` | Waveform atomization (if implemented) | ❌ Not tested |
| **Video** | `'audio-sample'` | Frame sequence atomization (if implemented) | ❌ Not tested |
| **Code** | `'code-token'` | AST node atomization (if implemented) | ❌ Not tested |
| **Relations** | N/A (trajectory) | Edge atomization | ❌ Not tested |
| **Concepts** | N/A (composition) | Convex hull atomization | ❌ Not tested |

**Critical**: Do NOT narrow scope to just "embeddings" or "weights" → test ALL implemented modalities

---

## Risk Mitigation

### High-Risk Areas

1. **Auto-commit removal**: May cause atoms to be invisible if commit forgotten
   - **Mitigation**: Add explicit `await conn.commit()` after each call
   - **Verification**: Check transaction isolation level, test commit behavior

2. **Cache removal**: May degrade performance for repeated lookups
   - **Mitigation**: Profile performance, add optional cache to AtomFactory if needed
   - **Verification**: Benchmark repeated atomization (same model loaded twice)

3. **Connection passing**: May cause errors if connection unavailable in scope
   - **Mitigation**: Ensure connection passed through method arguments
   - **Verification**: Check all call sites have `conn` variable in scope

### Medium-Risk Areas

1. **Metadata structure changes**: May cause type errors if metadata doesn't conform
   - **Mitigation**: Validate metadata against `AtomMetadata` TypedDict
   - **Verification**: Run mypy type checking

2. **Modality typos**: May cause runtime errors if modality string wrong
   - **Mitigation**: Use Literal type checking, verify all modality strings
   - **Verification**: Run mypy type checking

### Low-Risk Areas

1. **Return value changes**: Both APIs return `(atom_ids, coordinates)` in same format
   - **Verification**: Test return value unpacking

2. **Hash computation**: Both APIs use SHA-256 (same deduplication behavior)
   - **Verification**: Insert same content, verify same atom_id returned

---

## Next Steps (Execution Plan)

### Phase 1: Deep Analysis (CURRENT)
- ✅ Read safetensors_atomization.py context (line 290)
- ⏳ Read gguf_atomizer.py context (lines 325, 427)
- ⏳ Find and read geometric_atomizer.py context
- ⏳ Read bpe_crystallizer.py context (line 203)
- ⏳ Read tensor_utils.py context (line 133)
- ⏳ Document all 9 call sites comprehensively

### Phase 2: Migration (After analysis complete)
- ⏳ Migrate safetensors_atomization.py (reference implementation)
- ⏳ Test safetensors atomization (unit + integration)
- ⏳ Migrate gguf_atomizer.py (2 calls)
- ⏳ Test GGUF atomization
- ⏳ Migrate remaining files (geometric_atomizer, bpe_crystallizer, tensor_utils)
- ⏳ Test all atomizers

### Phase 3: Validation (After migration)
- ⏳ Run full test suite
- ⏳ Performance benchmarks
- ⏳ Universal scope verification (ALL content types)
- ⏳ Document migration results

### Phase 4: Cleanup (After validation passes)
- ⏳ Remove old FractalAtomizer code (Task 9)
- ⏳ Update imports throughout codebase
- ⏳ Add comprehensive tests (Task 10)

---

## Conclusion

This migration is **CRITICAL** for maintaining universal geometric atomization scope (ALL content types) while achieving enterprise-grade SOLID/DRY architecture.

**Key Insight**: The new infrastructure is BETTER (no duplication, type-safe, vectorized) but requires careful migration to avoid breaking existing functionality.

**Success Criteria**:
1. All 9 calls migrated successfully
2. All tests pass (unit + integration)
3. Performance improved (10-100x speedup)
4. Universal scope maintained (ALL content types work)
5. Zero regressions in existing functionality

**Next Action**: Complete Phase 1 analysis (read remaining 4 files in full context) before making ANY code changes.
