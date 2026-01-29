/**
 * @file test_codepoint_projection.cpp
 * @brief Integration test for complete Unicode → 4D pipeline using Google Test
 */

#include <gtest/gtest.h>
#include <unicode/codepoint_projection.hpp>
#include <vector>
#include <string>
#include <set>

using namespace hartonomous::unicode;

TEST(CodepointProjectionTest, SingleCodepointProjection) {
    char32_t cp = U'A';
    auto result = CodepointProjection::project(cp);

    EXPECT_EQ(result.codepoint, cp);
    EXPECT_EQ(result.hash.size(), 32);
    EXPECT_NEAR(result.s3_position.norm(), 1.0, 1e-9);
    EXPECT_NEAR(result.s2_projection.norm(), 1.0, 1e-9);
    
    // Hypercube coordinates should be in [0, 1]
    for (int i = 0; i < 4; ++i) {
        EXPECT_GE(result.hypercube_coords[i], 0.0);
        EXPECT_LE(result.hypercube_coords[i], 1.0);
    }
}

TEST(CodepointProjectionTest, Determinism) {
    char32_t cp = U'Z';
    auto result1 = CodepointProjection::project(cp);
    auto result2 = CodepointProjection::project(cp);

    EXPECT_EQ(result1.hash, result2.hash);
    EXPECT_NEAR((result1.s3_position - result2.s3_position).norm(), 0.0, 1e-9);
    EXPECT_EQ(result1.hilbert_index.hi, result2.hilbert_index.hi);
    EXPECT_EQ(result1.hilbert_index.lo, result2.hilbert_index.lo);
}

TEST(CodepointProjectionTest, Uniqueness) {
    std::vector<char32_t> codepoints = {U'A', U'B', U'C', U'a', U'b', U'c', U'0', U'1', U'你', U'好'};
    std::set<std::string> seen_hashes;
    for (char32_t cp : codepoints) {
        auto result = CodepointProjection::project(cp);
        seen_hashes.insert(std::string(result.hash.begin(), result.hash.end()));
    }
    EXPECT_EQ(seen_hashes.size(), codepoints.size());
}

TEST(CodepointProjectionTest, ContextSensitivity) {
    char32_t cp = U'A';
    auto r1 = CodepointProjection::project(cp, "context1");
    auto r2 = CodepointProjection::project(cp, "context2");
    auto r3 = CodepointProjection::project(cp, "");
    
    EXPECT_NE(r1.hash, r2.hash);
    EXPECT_NE(r1.hash, r3.hash);
    EXPECT_NE(r2.hash, r3.hash);
}

TEST(CodepointProjectionTest, DistanceMetrics) {
    auto p1 = CodepointProjection::project(U'A');
    auto p2 = CodepointProjection::project(U'B');
    auto p3 = CodepointProjection::project(U'A');

    // Geometric distance
    double dist_ab = CodepointProjection::geometric_distance(p1, p2);
    double dist_aa = CodepointProjection::geometric_distance(p1, p3);
    EXPECT_GT(dist_ab, 0.0);
    EXPECT_NEAR(dist_aa, 0.0, 1e-9);

    // Hilbert distance
    auto hdist_ab = CodepointProjection::hilbert_distance(p1, p2);
    auto hdist_aa = CodepointProjection::hilbert_distance(p1, p3);
    EXPECT_TRUE(hdist_ab.hi > 0 || hdist_ab.lo > 0);
    EXPECT_EQ(hdist_aa.hi, 0);
    EXPECT_EQ(hdist_aa.lo, 0);
}

TEST(CodepointProjectionTest, UTF8StringProcessing) {
    std::string text = "Hello 你好";
    auto results = CodepointProjection::project_string(text);
    // "Hello 你好" has 8 codepoints: H, e, l, l, o,  , 你, 好
    EXPECT_EQ(results.size(), 8);
    
    // The context ("Hello 你好") should make the projection for 'l' different
    // from a standalone 'l'
    auto single_l = CodepointProjection::project('l');
    EXPECT_NE(results[2].hash, single_l.hash);
    EXPECT_NE(results[3].hash, single_l.hash);
    // The two 'l's in "Hello" should be identical because their codepoint and context are the same.
    EXPECT_EQ(results[2].hash, results[3].hash);
}

TEST(CodepointProjectionTest, InvalidCodepoint) {
    uint32_t invalid_cp = 0x110000; // Max is 0x10FFFF
    EXPECT_THROW(CodepointProjection::project(invalid_cp), std::invalid_argument);
}
