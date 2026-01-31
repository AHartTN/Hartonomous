#pragma once

// Lightweight wrapper header for optional HNSW integration.
// Implementation should be guarded by build flags and optional third_party/hnswlib.
#include <vector>
#include "geometry/s3_vec.hpp"
#include "interop_api.h"

namespace s3::ann
{
    struct HnswIndexHandle;
    HARTONOMOUS_API HnswIndexHandle* build_index(const std::vector<Vec4>& points);
    HARTONOMOUS_API void free_index(HnswIndexHandle* h);
    HARTONOMOUS_API std::vector<std::pair<int, double>> query_index(HnswIndexHandle* h, const Vec4& q, int k);
}
