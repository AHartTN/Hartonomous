/**
 * @file test_spatial_index.cpp
 * @brief Unit tests for 4D Hilbert curve and ANN indexing
 */

#include <gtest/gtest.h>
#include <spatial/hilbert_curve_4d.hpp>
#include <ml/s3_hnsw.hpp>
#include <vector>
#include <Eigen/Core>
#include <cstring>

using namespace hartonomous::spatial;
using namespace s3::ann;

// Helper: Convert HilbertIndex to __int128 for tests (big-endian order)
static unsigned __int128 to_uint128(const HilbertCurve4D::HilbertIndex& idx) {
    unsigned __int128 result = 0;
    for (int i = 0; i < 16; ++i) {
        result = (result << 8) | idx[i];
    }
    return result;
}

TEST(SpatialIndexTest, HilbertDeterminism) {
    Eigen::Vector4d p = {0.1, 0.2, 0.3, 0.4};
    auto h1 = HilbertCurve4D::encode(p);
    auto h2 = HilbertCurve4D::encode(p);
    
    EXPECT_EQ(h1, h2);
}

TEST(SpatialIndexTest, HilbertBoundary) {
    // 0 and 1 should encode to min and max values
    Eigen::Vector4d p_min = {0.0, 0.0, 0.0, 0.0};
    Eigen::Vector4d p_max = {1.0, 1.0, 1.0, 1.0};
    
    auto h_min = HilbertCurve4D::encode(p_min);
    auto h_max = HilbertCurve4D::encode(p_max);
    
    EXPECT_LT(to_uint128(h_min), to_uint128(h_max));
}

TEST(SpatialIndexTest, HilbertLocality) {
    // Use points away from the center (0.5) to avoid quadrant boundary discontinuities
    Eigen::Vector4d p1 = {0.25, 0.25, 0.25, 0.25};
    Eigen::Vector4d p2 = {0.250001, 0.25, 0.25, 0.25}; // Very close
    Eigen::Vector4d p3 = {0.75, 0.75, 0.75, 0.75};      // Very far
    
    auto h1 = to_uint128(HilbertCurve4D::encode(p1));
    auto h2 = to_uint128(HilbertCurve4D::encode(p2));
    auto h3 = to_uint128(HilbertCurve4D::encode(p3));
    
    unsigned __int128 d12_val = (h1 > h2) ? (h1 - h2) : (h2 - h1);
    unsigned __int128 d13_val = (h1 > h3) ? (h1 - h3) : (h3 - h1);
    
    double d12 = static_cast<double>(d12_val);
    double d13 = static_cast<double>(d13_val);
    
    EXPECT_LT(d12, d13);
}

// HilbertStringConversion test removed - HilbertIndex is now binary UUID

TEST(SpatialIndexTest, HNSWIndexPlaceholder) {
    // Current implementation is a placeholder, test basic lifecycle
    std::vector<s3::Vec4> points = {
        {1,0,0,0}, {0,1,0,0}
    };
    
    auto handle = build_index(points);
    // Even if it returns nullptr (placeholder), we should be able to call free
    free_index(handle);
}
