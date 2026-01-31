/**
 * @file test_spatial_index.cpp
 * @brief Unit tests for 4D Hilbert curve and ANN indexing
 */

#include <gtest/gtest.h>
#include <spatial/hilbert_curve_4d.hpp>
#include <ml/s3_hnsw.hpp>
#include <vector>
#include <Eigen/Core>

using namespace hartonomous::spatial;
using namespace s3::ann;

TEST(SpatialIndexTest, HilbertDeterminism) {
    Eigen::Vector4d p = {0.1, 0.2, 0.3, 0.4};
    auto h1 = HilbertCurve4D::encode(p);
    auto h2 = HilbertCurve4D::encode(p);
    
    EXPECT_EQ(h1.hi, h2.hi);
    EXPECT_EQ(h1.lo, h2.lo);
}

TEST(SpatialIndexTest, HilbertBoundary) {
    // 0 and 1 should encode to min and max values
    Eigen::Vector4d p_min = {0.0, 0.0, 0.0, 0.0};
    Eigen::Vector4d p_max = {1.0, 1.0, 1.0, 1.0};
    
    auto h_min = HilbertCurve4D::encode(p_min);
    auto h_max = HilbertCurve4D::encode(p_max);
    
    EXPECT_LT(h_min.to_uint128(), h_max.to_uint128());
}

TEST(SpatialIndexTest, HilbertLocality) {
    // Use points away from the center (0.5) to avoid quadrant boundary discontinuities
    Eigen::Vector4d p1 = {0.25, 0.25, 0.25, 0.25};
    Eigen::Vector4d p2 = {0.250001, 0.25, 0.25, 0.25}; // Very close
    Eigen::Vector4d p3 = {0.75, 0.75, 0.75, 0.75};      // Very far
    
    auto h1 = HilbertCurve4D::encode(p1).to_uint128();
    auto h2 = HilbertCurve4D::encode(p2).to_uint128();
    auto h3 = HilbertCurve4D::encode(p3).to_uint128();
    
    unsigned __int128 d12_val = (h1 > h2) ? (h1 - h2) : (h2 - h1);
    unsigned __int128 d13_val = (h1 > h3) ? (h1 - h3) : (h3 - h1);
    
    double d12 = static_cast<double>(d12_val);
    double d13 = static_cast<double>(d13_val);
    
    EXPECT_LT(d12, d13);
}

TEST(SpatialIndexTest, HilbertStringConversion) {
    HilbertCurve4D::HilbertIndex h = {0x1234, 0x5678};
    std::string s = h.to_string();
    
    EXPECT_FALSE(s.empty());
    for(char c : s) {
        EXPECT_TRUE(isdigit(c));
    }
}

TEST(SpatialIndexTest, HNSWIndexPlaceholder) {
    // Current implementation is a placeholder, test basic lifecycle
    std::vector<s3::Vec4> points = {
        {1,0,0,0}, {0,1,0,0}
    };
    
    auto handle = build_index(points);
    // Even if it returns nullptr (placeholder), we should be able to call free
    free_index(handle);
}
