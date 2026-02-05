# Hartonomous Optimization Review

## Overview

This document reviews the optimization status of the Hartonomous ingestion pipelines and identifies areas for potential improvement.

---

## ✅ Current Optimizations

### 1. **Math Libraries (Intel OneAPI MKL)**

**Status**: ✅ Fully Configured

- **Interface**: LP64 (32-bit integers)
- **Threading**: SEQUENTIAL (single-threaded MKL)
  - Rationale: Avoids thread contention with OpenMP in application code
  - Application-level parallelism via OpenMP is more flexible
- **Linkage**: mkl_intel_lp64, mkl_sequential, mkl_core
- **Eigen Integration**: EIGEN_USE_MKL_ALL enabled
  - All BLAS/LAPACK operations accelerated
  - VML (Vector Math Library) for transcendentals
  - Optimized eigenvalue solvers

**Impact**:
- Matrix operations (Laplacian, Gram-Schmidt) use MKL BLAS
- `Eigen::SelfAdjointEigenSolver` uses MKL LAPACK (DSYEV/DSYEVD)
- Exponential functions in affinity matrix use MKL VML

### 2. **SIMD Vectorization**

**Status**: ✅ Fully Enabled

**Compiler Flags**:
- `-march=native` (auto-detects AVX2/AVX512/VNNI on build machine)
- Interprocedural Optimization (LTO) enabled

**Eigen Vectorization**:
- `EIGEN_VECTORIZE` enabled
- `EIGEN_VECTORIZE_AVX2` enabled
- `EIGEN_VECTORIZE_FMA` enabled
- AVX512 auto-detected via compiler flags

**Operations Vectorized**:
- Euclidean distance computation (`(a - b).norm()`)
- Matrix-vector products
- Row/column sums
- Element-wise operations (affinity kernel)

### 3. **OpenMP Parallelization**

**Status**: ✅ Implemented in Critical Paths

**spectral_analysis.cpp**:
```cpp
#pragma omp parallel for if(n > 1000)
for (size_t i = 0; i < n; i++) {
    // k-NN distance computation
}
```

**Thread Control**:
- Scripts set `OMP_NUM_THREADS=$(nproc)` for full CPU utilization
- Threshold: Parallelizes only for n > 1000 to avoid overhead

**Parallelized Operations**:
- Affinity matrix k-NN computation (O(n²) bottleneck)
- Future: Tensor weight processing

### 4. **Algorithm Efficiency**

**Status**: ✅ Optimal Algorithms Selected

- **k-NN Search**: `std::partial_sort` instead of full sort (O(n log k) vs O(n log n))
- **Eigenvalue Solver Selection**:
  - Large matrices (n > 500): Spectra (Lanczos iteration, O(nk²))
  - Small matrices: Eigen SelfAdjointEigenSolver (full decomposition)
- **Symmetric Matrix**: Affinity matrix symmetry exploited (`affinity(i,j) = affinity(j,i)`)

### 5. **Memory Efficiency**

**Status**: ✅ Optimized Allocation

- **Reserve Strategy**: `distances.reserve(n)` prevents reallocations
- **Bulk Copy**: Database insertion via PostgreSQL COPY (batch mode)
- **Deduplication**: Weight values deduplicated before storage
- **Move Semantics**: Eigen uses move semantics for large matrices

### 6. **Cache Optimization**

**Status**: ⚠️ Partially Optimized

**Current**:
- Eigen uses column-major storage (cache-friendly for column operations)
- Row-wise iteration in affinity matrix computation (potential cache misses)

**Potential Improvement**:
- Blocked/tiled computation for affinity matrix (better cache locality)

---

## 🔄 Potential Optimizations

### 1. **Affinity Matrix Computation (Blocked/Tiled)**

**Current**: Row-wise iteration with full distance computation

```cpp
for (size_t i = 0; i < n; i++) {
    for (size_t j = 0; j < n; j++) {
        double dist = euclidean_distance(embeddings.row(i), embeddings.row(j));
    }
}
```

**Improvement**: Block-based computation for better cache reuse

```cpp
constexpr size_t BLOCK_SIZE = 64;
for (size_t ib = 0; ib < n; ib += BLOCK_SIZE) {
    for (size_t jb = 0; jb < n; jb += BLOCK_SIZE) {
        // Process BLOCK_SIZE x BLOCK_SIZE sub-matrix
        // Embeddings stay in cache for multiple distance computations
    }
}
```

**Benefit**: Reduces cache misses for large embedding matrices (> 10K samples)

### 2. **SIMD Distance Computation (Manual Intrinsics)**

**Current**: Eigen's vectorized `(a - b).norm()`

**Improvement**: Manual AVX2/AVX512 intrinsics for specific use case

```cpp
#ifdef __AVX2__
#include <immintrin.h>

inline float euclidean_distance_avx2(const float* a, const float* b, size_t dim) {
    __m256 sum = _mm256_setzero_ps();
    for (size_t i = 0; i < dim; i += 8) {
        __m256 va = _mm256_loadu_ps(a + i);
        __m256 vb = _mm256_loadu_ps(b + i);
        __m256 diff = _mm256_sub_ps(va, vb);
        sum = _mm256_fmadd_ps(diff, diff, sum);  // FMA: sum += diff * diff
    }
    // Horizontal sum + sqrt
    return std::sqrt(hsum256_ps(sum));
}
#endif
```

**Benefit**: Marginal (~5-10%) speedup over Eigen (already well-optimized)

### 3. **HNSWLib for Approximate k-NN**

**Current**: Exact k-NN via brute-force distance computation (O(n²))

**Improvement**: Approximate nearest neighbors via HNSW (O(n log n) build, O(log n) query)

```cpp
#include <hnswlib/hnswlib.h>

hnswlib::HierarchicalNSW<float> hnsw_index(space, max_elements, M, ef_construction);
hnsw_index.addPoint(embeddings.row(i).data(), i);

// Query k-NN
auto result = hnsw_index.searchKnn(query, k);
```

**Benefit**: Massive speedup for large vocabularies (> 100K tokens)
**Tradeoff**: Approximate (99% recall with proper parameters)

### 4. **Parallel Tensor Processing**

**Current**: Sequential tensor weight ingestion

**Improvement**: Parallel processing of independent tensors

```cpp
#pragma omp parallel for schedule(dynamic)
for (size_t tensor_idx = 0; tensor_idx < num_tensors; tensor_idx++) {
    ingest_tensor_weights(tensors[tensor_idx]);
}
```

**Benefit**: Scales with number of tensors (models with 100+ layers)

### 5. **BLAKE3 SIMD Hashing**

**Status**: ✅ Already Enabled (BLAKE3 uses SIMD by default)

BLAKE3 library automatically uses:
- AVX512 on capable CPUs
- AVX2 fallback
- Multi-threading for large inputs

**No action needed** - dependency is correctly linked.

### 6. **Database Bulk Copy Batching**

**Current**: Single bulk copy operation per ingestion type

**Improvement**: Configurable batch size for memory-constrained environments

```cpp
constexpr size_t BATCH_SIZE = 10000;
for (size_t offset = 0; offset < total_records; offset += BATCH_SIZE) {
    bulk_copy.insert_batch(records.begin() + offset,
                           records.begin() + std::min(offset + BATCH_SIZE, total_records));
}
```

**Benefit**: Reduces peak memory usage for huge models (> 10GB)

---

## 📊 Performance Expectations

### Small Models (< 1GB, < 50K params)
- **Spectral Decomposition**: < 5 seconds (with MKL + OpenMP)
- **Tensor Ingestion**: < 10 seconds
- **Total Ingestion**: < 30 seconds

### Medium Models (1-10GB, 1-10B params)
- **Spectral Decomposition**: 30-60 seconds
- **Tensor Ingestion**: 1-5 minutes
- **Total Ingestion**: 2-10 minutes

### Large Models (> 10GB, > 10B params)
- **Spectral Decomposition**: 2-10 minutes (vocabulary size dependent)
- **Tensor Ingestion**: 10-60 minutes (weight count dependent)
- **Total Ingestion**: 15 minutes - 2 hours

**Note**: With HNSWLib approximate k-NN, large model spectral decomposition would drop to < 1 minute.

---

## 🎯 Recommendations

### Priority 1 (High Impact, Low Effort)
1. ✅ **OpenMP Parallelization** - Already implemented
2. ✅ **MKL Integration** - Already implemented
3. ⚠️ **Parallel Tensor Processing** - Easy win for multi-layer models

### Priority 2 (Medium Impact, Medium Effort)
1. **Blocked Affinity Matrix** - Improves cache locality for large vocabularies
2. **HNSWLib Approximate k-NN** - Massive speedup for vocabularies > 100K

### Priority 3 (Low Impact, High Effort)
1. **Manual SIMD Intrinsics** - Marginal gain over Eigen's vectorization
2. **Database Batching** - Only needed for memory-constrained environments

---

## 🔧 Build Verification

To verify optimizations are active, check build output:

```bash
./scripts/linux/build.sh | grep -E "(MKL|SIMD|OpenMP|AVX)"
```

Expected output:
```
-- MKL: Using LP64 interface (32-bit integers)
-- MKL: Using sequential (single-threaded) mode
-- Eigen: MKL-accelerated with BLAS/LAPACK/VML backend + SIMD vectorization
-- HNSWLib: SIMD level AUTO (detected AVX512)
```

Runtime verification:
```bash
export OMP_DISPLAY_ENV=TRUE
./build/tools/ingest_model <model_path>
```

Should show:
```
OPENMP DISPLAY ENVIRONMENT BEGIN
  _OPENMP = '201511'
  OMP_NUM_THREADS = '16'  (or your core count)
```

---

## 📚 References

- [Intel MKL Documentation](https://www.intel.com/content/www/us/en/docs/onemkl/developer-reference-c/2024-0/overview.html)
- [Eigen Performance](https://eigen.tuxfamily.org/index.php?title=FAQ#How_does_Eigen_compare_to_BLAS.2FLAPACK.3F)
- [OpenMP Best Practices](https://www.openmp.org/wp-content/uploads/openmp-4.5.pdf)
- [HNSWLib Paper](https://arxiv.org/abs/1603.09320)
- [BLAKE3 Specification](https://github.com/BLAKE3-team/BLAKE3-specs/blob/master/blake3.pdf)
