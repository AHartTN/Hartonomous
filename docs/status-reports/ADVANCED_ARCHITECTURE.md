# Advanced Architecture: Columnar + GPU + SIMD + In-Memory OLTP

## Executive Summary

Current bottleneck: **Row-based INSERT operations with Python dict overhead**

Proposed solution: **Multi-tiered columnar architecture with GPU acceleration, SIMD vectorization, and optional in-memory staging**

Expected performance: **100-500x overall speedup** (10-50x from columnar COPY + 10x from GPU/SIMD)

---

## 1. Columnar Storage Architecture

### Current Problem
- Row-based INSERT: One transaction per batch
- Python dict cache: Object overhead, no SIMD
- Multiple format conversions: NumPy → list → Decimal → dict → arrays → SQL

### Solution: Citus Columnar Extension

**Why Columnar for Atoms:**
- Atoms are **immutable** (perfect for columnar)
- Atoms are **write-once, read-many** (OLAP pattern)
- High compression (6-10x reported by cstore_fdw)
- Skip indexes for hash lookups
- Vectorized scans

**Implementation:**
```sql
-- Create columnar atom table
CREATE TABLE atom_columnar (
    atom_id BIGSERIAL PRIMARY KEY,
    content_hash BYTEA NOT NULL,
    canonical_text TEXT,
    metadata JSONB,
    reference_count INTEGER DEFAULT 1,
    valid_from TIMESTAMPTZ DEFAULT now()
) USING columnar;

-- Columnar composition table (append-only analytics)
CREATE TABLE composition_columnar (
    parent_atom_id BIGINT NOT NULL,
    component_atom_id BIGINT NOT NULL,
    sequence_idx INTEGER NOT NULL,
    PRIMARY KEY (parent_atom_id, component_atom_id)
) USING columnar;
```

**Benefits:**
- **2-4x compression** (fewer disk I/O)
- **Skip indexes** (min/max content_hash per stripe → faster lookups)
- **Column projection** (only read columns needed)
- **Stripe-based batching** (natural batch boundaries for GPU)

**Research Source:**
- Citus blog: "Columnar storage is now part of Citus extension"
- cstore_fdw: 150K rows/stripe default, 10K rows/block
- Uses ORC-inspired format (Optimized Row Columnar)
- Better compression than cstore_fdw (zstd support)

---

## 2. GPU Acceleration with PG-Strom

### What is PG-Strom?

**PG-Strom** = PostgreSQL extension for GPU-accelerated query processing
- **1.4K GitHub stars** (production-ready)
- Supports CUDA, Apache Arrow columnar format
- Offloads: scans, joins, aggregations to GPU
- Transparent to application (query planner decides)

**Key Features:**
- **GPU Scan:** Filter large tables on GPU
- **GPU Join:** Hash joins accelerated
- **GPU Aggregation:** GROUP BY on GPU
- **Arrow FDW:** Columnar data directly to GPU (zero-copy)

### Integration Strategy

**Phase 1: Deduplicate on GPU (Already Done)**
```python
# Current hierarchical GPU deduplication
weights_gpu = cp.array(compressed_weights, dtype=cp.float32)
unique_values_gpu = cp.unique(weights_gpu)  # CUDA kernel
```

**Phase 2: Bulk Hash Computation on GPU**
```python
# Vectorized SHA256 on GPU
import hashlib
from cupy import ElementwiseKernel

# Custom CUDA kernel for batch hashing (if CuPy supports, else CPU vectorized)
def batch_sha256_gpu(weights_gpu):
    # Option A: If hashlib can be vectorized with Numba/CuPy
    # Option B: Transfer to CPU, use NumPy vectorization
    weights_cpu = weights_gpu.get()  # DMA transfer once
    hashes = np.array([hashlib.sha256(str(w).encode()).digest() for w in weights_cpu])
    return hashes  # 32 bytes per weight
```

**Phase 3: GPU-Accelerated Lookups (Future)**
- Once atoms in columnar table, PG-Strom can accelerate queries:
```sql
-- This query would run on GPU with PG-Strom
SELECT atom_id 
FROM atom_columnar 
WHERE content_hash = ANY($1::bytea[])
```

**Performance Gains:**
- **10-100x for large scans** (PG-Strom benchmarks)
- **Zero-copy Arrow → GPU** (if columnar)
- **Parallel GPU execution** (multiple CUDA streams)

**Research Source:**
- PG-Strom GitHub: https://github.com/heterodb/pg-strom
- Supports PostgreSQL 12-18
- Apache Arrow integration for columnar
- CUDA 11+ required

---

## 3. SIMD/AVX Vectorization (NumPy + PostgreSQL)

### NumPy SIMD Support

**Current State:**
- NumPy has **built-in SIMD dispatch** (NEP 38)
- Supports: SSE, AVX, AVX2, AVX512, ARM NEON
- **Runtime detection** (chooses best instruction set)
- Applied to: ufuncs, reductions, searches

**Our Use Case:**
```python
# These operations are ALREADY SIMD-accelerated in NumPy:
sorted_indices = np.argsort(unique_weights)  # SIMD quicksort
sorted_weights = unique_weights[sorted_indices]  # SIMD gather
indices = np.searchsorted(sorted_weights, all_weights)  # SIMD binary search
```

**How to Verify SIMD Usage:**
```python
import numpy as np
np.__config__.show()  # Shows SIMD features compiled in

# Expected output:
# cpu_baseline = SSE SSE2 SSE3
# cpu_dispatch = SSSE3 SSE41 POPCNT SSE42 AVX F16C FMA3 AVX2 AVX512F ...
```

**Build for Maximum SIMD:**
```bash
# When building NumPy from source (optional, pre-built wheels have good defaults)
pip install numpy --no-binary :all: --config-settings=setup-args="-Dcpu-baseline=native"
```

### PostgreSQL Parallel Query

**Native Parallel Execution:**
```sql
-- PostgreSQL automatically parallelizes these queries:
SELECT parent_atom_id, COUNT(*) 
FROM composition_columnar 
GROUP BY parent_atom_id;

-- Set parallel workers
SET max_parallel_workers_per_gather = 8;
SET parallel_setup_cost = 100;
SET parallel_tuple_cost = 0.01;
```

**Parallel Aggregation:**
- Each worker builds partial hash table
- Final merge on coordinator
- Works best with columnar (less data to scan)

**Research Source:**
- PostgreSQL Chapter 15: Parallel Query
- Parallel scans, joins, aggregations all supported
- Can combine with PG-Strom for GPU+CPU parallelism

---

## 4. In-Memory Staging (Hekaton-Style)

### Concept: Temporal Memory-Optimized Tables

**Inspiration from SQL Server Hekaton:**
- In-memory tables for high-throughput writes
- Lock-free data structures (MVCC)
- Eventually flush to disk (durable tables)

**Our Application:**
```sql
-- In-memory staging table (PostgreSQL unlogged table)
CREATE UNLOGGED TABLE atom_staging (
    content_hash BYTEA PRIMARY KEY,
    canonical_text TEXT,
    metadata JSONB
);

-- Fast bulk insert (no WAL)
COPY atom_staging FROM STDIN;

-- Periodic merge to durable columnar table
INSERT INTO atom_columnar 
SELECT * FROM atom_staging
ON CONFLICT (content_hash) DO UPDATE SET reference_count = atom_columnar.reference_count + 1;

TRUNCATE atom_staging;
```

**Benefits:**
- **No WAL overhead** during atomization
- **Faster batch inserts** (still use COPY)
- **Lock-free lookups** (hash index on staging)
- **Periodic consolidation** (background job)

**Trade-offs:**
- Data in staging lost on crash (acceptable for derived data)
- Need merge strategy (how often?)
- Additional complexity

**Alternative: pgmemcache / Redis**
```python
# Option: Use Redis for in-memory cache during atomization
import redis
r = redis.Redis()

# Cache weight→atom_id in Redis
r.hset(f"tensor:{tensor_id}:weights", weight_hash, atom_id)

# Batch retrieve
atom_ids = r.hmget(f"tensor:{tensor_id}:weights", weight_hashes)
```

---

## 5. Complete Optimized Architecture

### Data Flow (End-to-End)

```
┌─────────────────────────────────────────────────────────────────┐
│ 1. GGUF File → NumPy Array (Pre-load entire tensor)            │
│    - Already optimized: 0.04s for 53M weights                  │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 2. GPU Processing (CuPy)                                        │
│    - RLE encoding: 0.43s (124M weights/s)                       │
│    - Transfer to GPU: cp.array()                                │
│    - Hierarchical deduplication: batched cp.unique()            │
│    - Result: 53M → 255 unique weights                           │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 3. Columnar Atom Preparation (NumPy SIMD)                       │
│    - Stay in NumPy arrays (no Python objects)                   │
│    - Vectorized hash computation (CPU, but batched)             │
│      hashes = np.array([sha256(w) for w in unique_weights])     │
│    - Vectorized text conversion                                 │
│      texts = unique_weights.astype(str)                         │
│    - Build columnar arrays: hashes[], texts[], metadata[]       │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 4. Bulk Insert with PostgreSQL COPY (Columnar Table)           │
│    - Use psycopg3 copy.write_row() API                          │
│    - Target: atom_columnar (USING columnar)                     │
│    - Single transaction, batched writes                         │
│    - Expected: 100-200x faster than INSERT                      │
│    - Citus columnar provides 2-4x compression                   │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 5. Vectorized Weight→Atom Mapping (NumPy SIMD)                 │
│    - Sort unique weights and atom IDs (SIMD quicksort)          │
│      sorted_idx = np.argsort(unique_weights)                    │
│    - Binary search across all weights (SIMD)                    │
│      indices = np.searchsorted(sorted_weights, all_weights)     │
│    - Array indexing (no dict, pure vectorized)                  │
│      atom_ids = sorted_atom_ids[indices]                        │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 6. Columnar Composition Build (NumPy)                           │
│    - Build columnar arrays directly                             │
│      parent_ids = np.full(len(non_sparse), tensor_atom_id)      │
│      component_ids = atom_ids[non_sparse_indices]               │
│      sequence_idx = non_sparse_indices.astype(np.int32)         │
└─────────────────────────────────────────────────────────────────┘
                              ↓
┌─────────────────────────────────────────────────────────────────┐
│ 7. Bulk Insert Compositions (PostgreSQL COPY)                  │
│    - Target: composition_columnar (USING columnar)              │
│    - Use psycopg3 COPY again                                    │
│    - Expected: 100-200x faster than INSERT                      │
└─────────────────────────────────────────────────────────────────┘
```

### Code Implementation

**api/services/model_atomization_advanced.py:**

```python
import cupy as cp
import numpy as np
import hashlib
from psycopg import AsyncConnection
from typing import List, Tuple

class AdvancedGGUFAtomizer:
    """
    Columnar + GPU + SIMD optimized atomization pipeline
    """
    
    async def _atomize_weight_batch_columnar(
        self, 
        conn: AsyncConnection, 
        weights: np.ndarray,
        threshold: float
    ) -> Tuple[np.ndarray, np.ndarray]:
        """
        Fully columnar pipeline: stay in NumPy/CuPy throughout
        
        Returns:
            sorted_weights: np.ndarray of unique weights (sorted)
            sorted_atom_ids: np.ndarray of atom IDs (corresponding)
        """
        # Step 1: GPU deduplication (hierarchical, with progress)
        weights_gpu = cp.array(weights, dtype=cp.float32)
        unique_values_gpu = self._hierarchical_dedup_gpu(weights_gpu)
        unique_weights = unique_values_gpu.get()  # Transfer to CPU once
        
        # Step 2: Build columnar data (NumPy SIMD-accelerated)
        hashes = np.array([
            hashlib.sha256(str(w).encode()).digest() 
            for w in unique_weights
        ])  # TODO: Vectorize hash computation if possible
        
        texts = unique_weights.astype(str)  # Vectorized conversion
        
        # Step 3: Bulk insert with COPY to columnar table
        cur = await conn.cursor()
        
        # Use COPY for 100-200x speedup
        atom_ids = []
        async with cur.copy(
            "COPY atom_columnar (content_hash, canonical_text, metadata) "
            "FROM STDIN "
            "RETURNING atom_id"  # If PostgreSQL supports, else query after
        ) as copy:
            for i in range(len(unique_weights)):
                await copy.write_row((
                    hashes[i],
                    texts[i],
                    '{"type": "numeric"}'  # JSON metadata
                ))
        
        # Query atom IDs (if RETURNING not supported in COPY)
        # Alternative: Query by content_hash after insert
        await cur.execute(
            "SELECT atom_id FROM atom_columnar WHERE content_hash = ANY(%s) ORDER BY content_hash",
            (hashes.tolist(),)
        )
        atom_ids = np.array([row[0] for row in await cur.fetchall()])
        
        # Step 4: Sort for vectorized lookup
        sorted_indices = np.argsort(unique_weights)  # SIMD quicksort
        sorted_weights = unique_weights[sorted_indices]
        sorted_atom_ids = atom_ids[sorted_indices]
        
        return sorted_weights, sorted_atom_ids
    
    async def _create_compositions_columnar(
        self,
        conn: AsyncConnection,
        tensor_atom_id: int,
        all_weights: np.ndarray,
        sorted_weights: np.ndarray,
        sorted_atom_ids: np.ndarray,
        threshold: float
    ):
        """
        Vectorized composition creation with COPY
        """
        # Step 1: Identify non-sparse weights
        non_sparse_mask = np.abs(all_weights) >= threshold
        non_sparse_indices = np.where(non_sparse_mask)[0]
        non_sparse_weights = all_weights[non_sparse_mask]
        
        # Step 2: Vectorized binary search (SIMD)
        indices = np.searchsorted(sorted_weights, non_sparse_weights)
        component_atom_ids = sorted_atom_ids[indices]
        
        # Step 3: Build columnar composition arrays
        parent_ids = np.full(len(non_sparse_indices), tensor_atom_id, dtype=np.int64)
        sequence_indices = non_sparse_indices.astype(np.int32)
        
        # Step 4: Bulk insert with COPY
        cur = await conn.cursor()
        async with cur.copy(
            "COPY composition_columnar (parent_atom_id, component_atom_id, sequence_idx) "
            "FROM STDIN"
        ) as copy:
            for i in range(len(parent_ids)):
                await copy.write_row((
                    int(parent_ids[i]),
                    int(component_atom_ids[i]),
                    int(sequence_indices[i])
                ))
        
        logger.info(f"  Created {len(parent_ids):,} compositions via COPY")
    
    def _hierarchical_dedup_gpu(self, weights_gpu: cp.ndarray) -> cp.ndarray:
        """
        Batched GPU deduplication (already implemented)
        """
        DEDUP_BATCH_SIZE = 1_000_000
        
        if len(weights_gpu) <= DEDUP_BATCH_SIZE:
            return cp.unique(weights_gpu)
        
        # Hierarchical batching
        num_batches = (len(weights_gpu) + DEDUP_BATCH_SIZE - 1) // DEDUP_BATCH_SIZE
        batch_uniques = []
        
        for i in range(num_batches):
            start = i * DEDUP_BATCH_SIZE
            end = min((i + 1) * DEDUP_BATCH_SIZE, len(weights_gpu))
            batch = weights_gpu[start:end]
            batch_unique = cp.unique(batch)
            batch_uniques.append(batch_unique)
            logger.info(f"    Batch {i+1}/{num_batches}: {len(batch):,} → {len(batch_unique):,}")
        
        # Final merge
        all_uniques = cp.concatenate(batch_uniques)
        return cp.unique(all_uniques)
```

---

## 6. Performance Estimates

### Baseline (Current)
- Pre-load: 0.04s ✅
- RLE: 0.43s ✅
- GPU dedup: 32s (now batched with progress) ✅
- **Weight atomization: ~60s** (INSERT + dict cache)
- **Composition creation: ~120s** (INSERT)
- **Total: ~212s**

### Optimized (Proposed)
- Pre-load: 0.04s (unchanged)
- RLE: 0.43s (unchanged)
- GPU dedup: 2-3s (batched, better GPU utilization)
- **Weight atomization: 0.6s** (COPY 100x faster + columnar)
- **Composition creation: 1.2s** (COPY 100x faster + vectorized)
- **Total: ~4.3s**

**Overall Speedup: 50x** (212s → 4.3s)

### Additional Gains with PG-Strom (Future)
- GPU-accelerated queries for composition traversal
- Arrow-based zero-copy GPU data transfer
- Parallel GPU execution
- **Potential additional 10x** for query-heavy workloads

---

## 7. Implementation Roadmap

### Phase 1: Columnar Tables (Week 1)
- [ ] Install Citus extension (`CREATE EXTENSION citus;`)
- [ ] Create `atom_columnar` table (USING columnar)
- [ ] Create `composition_columnar` table
- [ ] Migrate data (INSERT INTO ... SELECT)
- [ ] Benchmark: Check compression ratio, query performance

### Phase 2: COPY-Based Bulk Inserts (Week 1)
- [ ] Replace `atomize_numeric_batch()` stored procedure with COPY
- [ ] Implement psycopg3 `copy.write_row()` for atoms
- [ ] Implement psycopg3 `copy.write_row()` for compositions
- [ ] Remove dict cache (use sorted arrays)
- [ ] Benchmark: Measure INSERT → COPY speedup

### Phase 3: Vectorized NumPy Pipeline (Week 2)
- [ ] Implement `np.searchsorted()` for weight→atom mapping
- [ ] Remove all Python list/Decimal conversions
- [ ] Stay in NumPy arrays throughout
- [ ] Verify SIMD usage (`np.__config__.show()`)
- [ ] Benchmark: Measure overall speedup

### Phase 4: PG-Strom Integration (Week 3)
- [ ] Install PG-Strom extension
- [ ] Configure GPU memory limits
- [ ] Test GPU-accelerated queries on columnar tables
- [ ] Benchmark: Compare CPU vs GPU query execution
- [ ] Monitor GPU utilization

### Phase 5: In-Memory Staging (Optional - Week 4)
- [ ] Create unlogged staging tables
- [ ] Implement periodic merge strategy
- [ ] Benchmark: Measure write throughput improvement
- [ ] Evaluate crash recovery implications

---

## 8. Hardware Requirements

### Minimum (Current Setup)
- NVIDIA GPU with CUDA support (RTX 4060 ✅)
- 16GB RAM (you have this ✅)
- PostgreSQL 12+ (you have 16 ✅)
- CuPy installed (you have this ✅)

### Recommended for Full Stack
- **GPU:** RTX 4080/4090 or datacenter GPU (A100, H100)
- **RAM:** 32GB+ (for larger models)
- **Storage:** NVMe SSD (PG-Strom can use GPU Direct Storage)
- **PostgreSQL:** 14+ (better parallel query)
- **Extensions:**
  - Citus (columnar storage)
  - PG-Strom (GPU acceleration)
  - pg_stat_statements (profiling)

### Software Stack
```bash
# Install Citus (columnar)
sudo apt-get install postgresql-16-citus-columnar

# Install PG-Strom (GPU)
# See: https://github.com/heterodb/pg-strom/blob/master/docs/install.md
sudo apt-get install postgresql-16-pg-strom

# Verify NumPy SIMD
python -c "import numpy as np; np.__config__.show()"

# Check CuPy
python -c "import cupy; print(cupy.__version__)"
```

---

## 9. Monitoring & Profiling

### PostgreSQL Query Performance
```sql
-- Enable query timing
\timing on

-- Check columnar compression
SELECT pg_size_pretty(pg_table_size('atom_columnar'));
SELECT pg_size_pretty(pg_table_size('atom'));  -- Compare with row-based

-- Monitor parallel workers
SELECT * FROM pg_stat_activity WHERE backend_type = 'parallel worker';

-- Check PG-Strom usage (if installed)
SELECT * FROM pgstrom.gpu_devices;
```

### CuPy GPU Profiling
```python
from cupyx.profiler import benchmark

# Benchmark GPU operations
print(benchmark(cp.unique, (weights_gpu,), n_repeat=10))

# Check GPU memory
print(f"GPU memory: {cp.get_default_memory_pool().used_bytes() / 1e9:.2f} GB")
```

### NumPy SIMD Verification
```python
import numpy as np
import timeit

# Compare SIMD vs non-SIMD (force scalar)
a = np.random.random(10_000_000)
b = np.random.random(10_000_000)

# This uses SIMD
t1 = timeit.timeit(lambda: np.add(a, b), number=100)

# Force scalar (for comparison - not exposed in public API)
# Just measure and trust NumPy dispatches correctly
print(f"Vectorized add: {t1:.3f}s")
```

---

## 10. References & Further Reading

### Columnar Storage
- **Citus Columnar:** https://github.com/citusdata/citus
  - Blog: "Citus 10: Columnar Compression for Postgres"
- **cstore_fdw:** https://github.com/citusdata/cstore_fdw
  - ORC-inspired format, 6-10x compression
- **Apache Arrow:** https://arrow.apache.org/
  - Columnar in-memory format (PG-Strom compatible)

### GPU Acceleration
- **PG-Strom:** https://github.com/heterodb/pg-strom
  - PostgreSQL GPU extension
  - CUDA-based query acceleration
- **CuPy:** https://docs.cupy.dev/
  - Performance guide: https://docs.cupy.dev/en/stable/user_guide/performance.html

### SIMD/Vectorization
- **NumPy SIMD:** https://numpy.org/doc/stable/reference/simd/
  - NEP 38: SIMD optimization instructions
- **PostgreSQL Parallel Query:** https://www.postgresql.org/docs/current/parallel-query.html
  - Chapter 15: How parallel query works

### In-Memory OLTP
- **PostgreSQL Unlogged Tables:** https://www.postgresql.org/docs/current/sql-createtable.html
- **SQL Server Hekaton:** (Conceptual inspiration)
  - Lock-free data structures
  - Memory-optimized tables

---

## Conclusion

This architecture brings together:
1. ✅ **Columnar storage** (Citus) → 2-4x compression, skip indexes
2. ✅ **GPU acceleration** (CuPy + PG-Strom) → 10-100x for scans/joins
3. ✅ **SIMD vectorization** (NumPy) → Already optimized, just verify
4. ✅ **PostgreSQL COPY** → 100-200x faster than INSERT
5. ✅ **Hierarchical GPU dedup** → Already implemented

**Next Step:** Implement Phase 1 (columnar tables) + Phase 2 (COPY) to validate **50x speedup** estimate.

**Conservative Estimate:** 50x overall (212s → 4s per tensor)
**Optimistic Estimate:** 100-500x with full PG-Strom + in-memory staging

The test currently running will validate current synchronous baseline. Once complete, we implement columnar COPY first (biggest win), then add PG-Strom if needed.
