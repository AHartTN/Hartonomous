#pragma once
#include "geometry/s3_vec.hpp"
#include "interop_api.h"

namespace s3
{
    HARTONOMOUS_API double geodesic_distance(const Vec4& a, const Vec4& b) noexcept;
    HARTONOMOUS_API double geodesic_distance_fast_core(const Vec4& a, const Vec4& b) noexcept;
    HARTONOMOUS_API double euclidean_distance(const Vec4& a, const Vec4& b) noexcept;
}
