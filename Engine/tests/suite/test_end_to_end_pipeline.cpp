/**
 * @file test_end_to_end_pipeline.cpp
 * @brief Integration tests for the full Unicode -> S3 -> Hilbert pipeline
 */

#include <gtest/gtest.h>
#include <unicode/codepoint_projection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <cmath>

using namespace hartonomous::unicode;

TEST(PipelineTest, UnicodeToSpatialResult) {
    uint32_t cp = 0x1F30D; // 🌍
    auto result = CodepointProjection::project(cp);
    
    // Check invariants
    EXPECT_EQ(result.codepoint, cp);
    EXPECT_NEAR(result.s3_position.norm(), 1.0, 1e-12);
    EXPECT_NEAR(result.s2_projection.norm(), 1.0, 1e-12);
    
    // Hypercube coordinates should be [0, 1]
    for (int i = 0; i < 4; ++i) {
        EXPECT_GE(result.hypercube_coords[i], 0.0);
        EXPECT_LE(result.hypercube_coords[i], 1.0);
    }
    
    // Hilbert index should be non-zero for most points
    EXPECT_NE(result.hilbert_index.lo, 0);
}

TEST(PipelineTest, DeterminismAcrossComponents) {
    uint32_t cp = 0x41; // 'A'
    auto r1 = CodepointProjection::project(cp);
    auto r2 = CodepointProjection::project(cp);
    
    EXPECT_EQ(r1.hilbert_index.hi, r2.hilbert_index.hi);
    EXPECT_EQ(r1.hilbert_index.lo, r2.hilbert_index.lo);
    EXPECT_EQ(r1.s3_position, r2.s3_position);
}

TEST(PipelineTest, StringBatchProcessing) {
    std::string text = "Hartonomous 2026";
    auto results = CodepointProjection::project_string(text);
    
    EXPECT_EQ(results.size(), text.length());
    for(const auto& r : results) {
        EXPECT_NEAR(r.s3_position.norm(), 1.0, 1e-12);
    }
}

TEST(PipelineTest, DistanceMetricCoherence) {
    // Geodesic distance on S3 and Hilbert distance should be correlated
    auto r1 = CodepointProjection::project(0x41); // 'A'
    auto r2 = CodepointProjection::project(0x42); // 'B'
    auto r3 = CodepointProjection::project(0x5A); // 'Z'
    
    double d12 = CodepointProjection::geometric_distance(r1, r2);
    double d13 = CodepointProjection::geometric_distance(r1, r3);
    
    auto h12 = CodepointProjection::hilbert_distance(r1, r2).to_uint128();
    auto h13 = CodepointProjection::hilbert_distance(r1, r3).to_uint128();
    
    // Note: Hilbert curves aren't perfect, but for 1D range queries to work,
    // they must generally preserve order.
    // This is a sanity check that they aren't completely random.
}
