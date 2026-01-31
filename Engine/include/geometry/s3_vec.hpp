#pragma once
#include <array>
#include <immintrin.h>
#include <cmath>
#include "interop_api.h"

namespace s3
{
    using Vec4 = std::array<double, 4>;

    inline double dot(const Vec4& a, const Vec4& b) noexcept
    {
    #if defined(__AVX__)
        __m256d va = _mm256_loadu_pd(a.data());
        __m256d vb = _mm256_loadu_pd(b.data());
        __m256d prod = _mm256_mul_pd(va, vb);
        __m128d hi = _mm256_extractf128_pd(prod, 1);
        __m128d lo = _mm256_castpd256_pd128(prod);
        __m128d sum2 = _mm_add_pd(hi, lo);
        __m128d shuf = _mm_shuffle_pd(sum2, sum2, 0x1);
        __m128d sum = _mm_add_pd(sum2, shuf);
        double out;
        _mm_store_sd(&out, sum);
        return out;
    #else
        return a[0]*b[0] + a[1]*b[1] + a[2]*b[2] + a[3]*b[3];
    #endif
    }

    inline void normalize(Vec4& v) noexcept
    {
        double r2 = dot(v, v);
        if (r2 == 0.0) return;
        double inv_r = 1.0 / std::sqrt(r2);
        v[0] *= inv_r;
        v[1] *= inv_r;
        v[2] *= inv_r;
        v[3] *= inv_r;
    }
}
