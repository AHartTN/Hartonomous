/**
 * @file test_hashing.cpp
 * @brief Unit tests for BLAKE3 hashing pipeline
 */

#include <gtest/gtest.h>
#include <hashing/blake3_pipeline.hpp>
#include <vector>
#include <string>

using namespace Hartonomous;

TEST(HashingTest, Determinism) {
    std::string data = "Hartonomous Semantic Substrate 2026";
    auto hash1 = BLAKE3Pipeline::hash(data);
    auto hash2 = BLAKE3Pipeline::hash(data);
    
    EXPECT_EQ(hash1, hash2);
}

TEST(HashingTest, CollisionResistance) {
    auto hash1 = BLAKE3Pipeline::hash("test1");
    auto hash2 = BLAKE3Pipeline::hash("test2");
    
    EXPECT_NE(hash1, hash2);
}

TEST(HashingTest, CodepointHashing) {
    // Unicode codepoints should hash deterministically
    auto h1 = BLAKE3Pipeline::hash_codepoint(0x1F600); // 😀
    auto h2 = BLAKE3Pipeline::hash_codepoint(0x1F600);
    auto h3 = BLAKE3Pipeline::hash_codepoint(0x1F601); // 😁
    
    EXPECT_EQ(h1, h2);
    EXPECT_NE(h1, h3);
}

TEST(HashingTest, HexConversion) {
    std::string data = "hex_test";
    auto hash = BLAKE3Pipeline::hash(data);
    std::string hex = BLAKE3Pipeline::to_hex(hash);
    auto hash_rt = BLAKE3Pipeline::from_hex(hex);
    
    EXPECT_EQ(hash, hash_rt);
    EXPECT_EQ(hex.length(), 64); // 32 bytes * 2
}

TEST(HashingTest, BatchHashing) {
    std::vector<std::string> inputs = {"a", "b", "c"};
    auto hashes = BLAKE3Pipeline::hash_batch(inputs);
    
    EXPECT_EQ(hashes.size(), 3);
    EXPECT_EQ(hashes[0], BLAKE3Pipeline::hash("a"));
    EXPECT_EQ(hashes[1], BLAKE3Pipeline::hash("b"));
    EXPECT_EQ(hashes[2], BLAKE3Pipeline::hash("c"));
}
