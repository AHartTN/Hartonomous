#pragma once

/// SIMD-OPTIMIZED VECTOR OPERATIONS
///
/// High-performance vector math for embedding similarity computation.
/// Uses AVX2/AVX-512 when available, falls back to portable SIMD.
///
/// Key operations:
/// - Batch dot products (matrix @ matrix.T)
/// - L2 normalization
/// - Cosine similarity matrices
///
/// Cache-aware blocking for large matrices.

#include <cstddef>
#include <cstdint>
#include <cmath>
#include <vector>
#include <algorithm>

#if defined(__AVX2__)
#include <immintrin.h>
#define SIMD_AVX2 1
#elif defined(__SSE4_1__)
#include <smmintrin.h>
#define SIMD_SSE4 1
#elif defined(_MSC_VER) && (defined(_M_X64) || defined(_M_IX86))
#include <intrin.h>
// MSVC: check at runtime
#define SIMD_MSVC_RUNTIME 1
#endif

namespace hartonomous::math {

/// Compute dot product of two float vectors using SIMD
inline double dot_product_f32(const float* a, const float* b, std::size_t n) {
#if defined(SIMD_AVX2) || defined(SIMD_MSVC_RUNTIME)
    // AVX2: 8 floats per operation
    __m256 sum = _mm256_setzero_ps();
    
    std::size_t i = 0;
    for (; i + 8 <= n; i += 8) {
        __m256 va = _mm256_loadu_ps(a + i);
        __m256 vb = _mm256_loadu_ps(b + i);
        sum = _mm256_fmadd_ps(va, vb, sum);
    }
    
    // Horizontal sum
    __m128 hi = _mm256_extractf128_ps(sum, 1);
    __m128 lo = _mm256_castps256_ps128(sum);
    __m128 sum128 = _mm_add_ps(lo, hi);
    sum128 = _mm_hadd_ps(sum128, sum128);
    sum128 = _mm_hadd_ps(sum128, sum128);
    
    float result = _mm_cvtss_f32(sum128);
    
    // Remainder
    for (; i < n; ++i) {
        result += a[i] * b[i];
    }
    
    return static_cast<double>(result);
#else
    // Scalar fallback with loop unrolling
    double sum0 = 0, sum1 = 0, sum2 = 0, sum3 = 0;
    std::size_t i = 0;
    for (; i + 4 <= n; i += 4) {
        sum0 += static_cast<double>(a[i]) * static_cast<double>(b[i]);
        sum1 += static_cast<double>(a[i+1]) * static_cast<double>(b[i+1]);
        sum2 += static_cast<double>(a[i+2]) * static_cast<double>(b[i+2]);
        sum3 += static_cast<double>(a[i+3]) * static_cast<double>(b[i+3]);
    }
    for (; i < n; ++i) {
        sum0 += static_cast<double>(a[i]) * static_cast<double>(b[i]);
    }
    return sum0 + sum1 + sum2 + sum3;
#endif
}

/// Compute L2 norm of a float vector using SIMD
inline double l2_norm_f32(const float* a, std::size_t n) {
    return std::sqrt(dot_product_f32(a, a, n));
}

/// Normalize a float vector in-place (returns original norm)
inline double normalize_f32(float* a, std::size_t n) {
    double norm = l2_norm_f32(a, n);
    if (norm < 1e-12) return 0.0;
    
    float inv_norm = static_cast<float>(1.0 / norm);
    
#if defined(SIMD_AVX2) || defined(SIMD_MSVC_RUNTIME)
    __m256 vn = _mm256_set1_ps(inv_norm);
    std::size_t i = 0;
    for (; i + 8 <= n; i += 8) {
        __m256 va = _mm256_loadu_ps(a + i);
        va = _mm256_mul_ps(va, vn);
        _mm256_storeu_ps(a + i, va);
    }
    for (; i < n; ++i) {
        a[i] *= inv_norm;
    }
#else
    for (std::size_t i = 0; i < n; ++i) {
        a[i] *= inv_norm;
    }
#endif
    
    return norm;
}

/// Batch normalize all rows of a matrix (row-major, n_rows x n_cols)
/// Returns vector of original norms
inline std::vector<double> batch_normalize_f32(float* data, std::size_t n_rows, std::size_t n_cols) {
    std::vector<double> norms(n_rows);
    
    #pragma omp parallel for schedule(static)
    for (std::size_t i = 0; i < n_rows; ++i) {
        norms[i] = normalize_f32(data + i * n_cols, n_cols);
    }
    
    return norms;
}

/// Result of similarity computation - above-threshold pairs
struct SimilarityPair {
    std::uint32_t i;
    std::uint32_t j;
    float similarity;
};

/// Compute pairwise cosine similarities for normalized vectors.
/// Returns only pairs with similarity >= threshold.
/// Uses blocked matrix multiplication for cache efficiency.
///
/// data: row-major matrix, n_rows x n_cols (MUST BE PRE-NORMALIZED)
/// threshold: minimum similarity to include
/// 
/// Complexity: O(n² × d) but cache-optimized with SIMD
inline std::vector<SimilarityPair> pairwise_cosine_above_threshold(
    const float* data,
    std::size_t n_rows,
    std::size_t n_cols,
    float threshold)
{
    std::vector<SimilarityPair> results;
    
    // For normalized vectors, cosine = dot product
    // Block size tuned for L2 cache (256KB typical)
    constexpr std::size_t BLOCK_SIZE = 128;
    
    // Thread-local result buffers
    std::size_t n_threads = 1;
#ifdef _OPENMP
    #pragma omp parallel
    {
        #pragma omp single
        n_threads = static_cast<std::size_t>(omp_get_num_threads());
    }
#endif
    
    std::vector<std::vector<SimilarityPair>> thread_results(n_threads);
    
    // Block over row pairs (upper triangle)
    #pragma omp parallel
    {
        std::size_t tid = 0;
#ifdef _OPENMP
        tid = static_cast<std::size_t>(omp_get_thread_num());
#endif
        auto& local = thread_results[tid];
        local.reserve(n_rows * 10 / n_threads); // Estimate ~10 neighbors per row
        
        #pragma omp for schedule(dynamic, 1) collapse(2)
        for (std::size_t bi = 0; bi < n_rows; bi += BLOCK_SIZE) {
            for (std::size_t bj = 0; bj < n_rows; bj += BLOCK_SIZE) {
                // Skip lower triangle blocks
                if (bj < bi) continue;
                
                std::size_t i_end = std::min(bi + BLOCK_SIZE, n_rows);
                std::size_t j_start = (bi == bj) ? bi : bj;
                std::size_t j_end = std::min(bj + BLOCK_SIZE, n_rows);
                
                // Process block
                for (std::size_t i = bi; i < i_end; ++i) {
                    const float* row_i = data + i * n_cols;
                    
                    // Start from i+1 in diagonal block, bj otherwise
                    std::size_t j_begin = (bi == bj) ? i + 1 : j_start;
                    
                    for (std::size_t j = j_begin; j < j_end; ++j) {
                        const float* row_j = data + j * n_cols;
                        
                        double sim = dot_product_f32(row_i, row_j, n_cols);
                        
                        if (sim >= static_cast<double>(threshold)) {
                            local.push_back({
                                static_cast<std::uint32_t>(i),
                                static_cast<std::uint32_t>(j),
                                static_cast<float>(sim)
                            });
                        }
                    }
                }
            }
        }
    }
    
    // Merge thread-local results
    std::size_t total = 0;
    for (const auto& tr : thread_results) total += tr.size();
    results.reserve(total);
    
    for (auto& tr : thread_results) {
        results.insert(results.end(),
                      std::make_move_iterator(tr.begin()),
                      std::make_move_iterator(tr.end()));
    }
    
    return results;
}

/// Copy embedding matrix and normalize in place.
/// Returns normalized copy and original norms.
inline std::pair<std::vector<float>, std::vector<double>> 
copy_and_normalize(const float* data, std::size_t n_rows, std::size_t n_cols) {
    std::vector<float> normalized(n_rows * n_cols);
    std::memcpy(normalized.data(), data, n_rows * n_cols * sizeof(float));
    
    auto norms = batch_normalize_f32(normalized.data(), n_rows, n_cols);
    
    return {std::move(normalized), std::move(norms)};
}

} // namespace hartonomous::math
