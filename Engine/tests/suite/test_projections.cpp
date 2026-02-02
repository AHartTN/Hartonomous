/**
 * @file test_projections.cpp
 * @brief Unit tests for S3 projections (SuperFibonacci, Hopf)
 */

#include <gtest/gtest.h>
#include <geometry/super_fibonacci.hpp>
#include <geometry/hopf_fibration.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <vector>
#include <cmath>

using namespace hartonomous::geometry;
using namespace Hartonomous;

TEST(ProjectionTest, SuperFibonacciNormalization) {
    const size_t N = 500;
    for (size_t i = 0; i < N; ++i) {
        auto p = SuperFibonacci::point_on_s3(i, N);
        EXPECT_NEAR(p.norm(), 1.0, 1e-12);
    }
}

TEST(ProjectionTest, HashToPointDeterminism) {
    std::string data = "deterministic_test";
    auto hash = BLAKE3Pipeline::hash(data);
    
    // Hash is 16 bytes (128-bit)
    auto p1 = SuperFibonacci::hash_to_point(hash.data());
    auto p2 = SuperFibonacci::hash_to_point(hash.data());
    
    EXPECT_EQ(p1, p2);
    EXPECT_NEAR(p1.norm(), 1.0, 1e-12);
}

TEST(ProjectionTest, HopfRoundTrip) {
    // S3 -> S2 -> S3 (rt)
    Eigen::Vector4d p_s3 = {0.5, 0.5, 0.5, 0.5}; // normalized
    auto p_s2 = HopfFibration::forward(p_s3);
    
    EXPECT_NEAR(p_s2.norm(), 1.0, 1e-12);
    
    // The inverse doesn't return the same point (fiber bundle)
    // but the round-trip back to S2 must be exact
    auto p_s3_rt = HopfFibration::inverse(p_s2, 0.123); // arbitrary angle
    auto p_s2_rt = HopfFibration::forward(p_s3_rt);
    
    EXPECT_NEAR((p_s2 - p_s2_rt).norm(), 0.0, 1e-12);
}

TEST(ProjectionTest, DistributionStability) {
    // Ensure that small changes in input (hash) don't produce massive shifts on S3
    // This is hard to guarantee with hashing, but we can check if it's "sane"
    unsigned char h1[16] = {0};
    unsigned char h2[16] = {0};
    h2[15] = 1; // 1 bit flip at the end
    
    auto p1 = SuperFibonacci::hash_to_point(h1);
    auto p2 = SuperFibonacci::hash_to_point(h2);
    
    // They should be different
    EXPECT_GT((p1 - p2).norm(), 1e-15);
    
    // Check magnitudes
    EXPECT_NEAR(p1.norm(), 1.0, 1e-12);
    EXPECT_NEAR(p2.norm(), 1.0, 1e-12);
}
