#include "ml/s3_hnsw.hpp"

// Placeholder implementation. Link against hnswlib and implement serialization/deserialization
// in production. Keep this file small and optional; guard with -DS3_USE_HNSW if needed.

namespace s3::ann
{
    struct HnswIndexHandle { /* opaque */ };

    HnswIndexHandle* build_index(const std::vector<Vec4>&)
    {
        return nullptr;
    }

    void free_index(HnswIndexHandle* )
    {
    }

    std::vector<std::pair<int, double>> query_index(HnswIndexHandle*, const Vec4&, int)
    {
        return {};
    }
}
