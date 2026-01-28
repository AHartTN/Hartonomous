/**
 * @file test_codepoint_projection.cpp
 * @brief Integration test for complete Unicode → 4D pipeline
 */

#include <unicode/codepoint_projection.hpp>
#include <iostream>
#include <string>
#include <vector>
#include <cassert>

using namespace Hartonomous;

constexpr double EPSILON = 1e-6;

// Test 1: Project single codepoint
void test_single_codepoint() {
    std::cout << "Test 1: Project single codepoint... ";

    // Test 'A' = U+0041
    char32_t codepoint = U'A';

    auto result = CodepointProjection::project(codepoint);

    // Verify hash is 32 bytes
    assert(result.hash.size() == 32 && "Hash should be 32 bytes");

    // Verify position is on S³ surface
    double norm = result.s3_position.norm();
    assert(std::abs(norm - 1.0) < EPSILON && "Position should be on S³ surface");

    // Verify Hilbert index is valid (non-zero for most inputs)
    assert(result.hilbert_index >= 0 && "Hilbert index should be non-negative");

    std::cout << "PASSED\n";
    std::cout << "    Codepoint: U+" << std::hex << (uint32_t)codepoint << std::dec << "\n";
    std::cout << "    Hilbert index: " << result.hilbert_index << "\n";
}

// Test 2: Project multiple codepoints (entire string)
void test_string_projection() {
    std::cout << "Test 2: Project string... ";

    std::u32string text = U"Hello, World!";
    std::vector<CodepointProjection::ProjectionResult> results;

    for (char32_t cp : text) {
        results.push_back(CodepointProjection::project(cp));
    }

    assert(results.size() == text.size() && "Should project all codepoints");

    // All should have valid results
    for (const auto& result : results) {
        assert(result.hash.size() == 32);
        assert(std::abs(result.s3_position.norm() - 1.0) < EPSILON);
    }

    std::cout << "PASSED\n";
}

// Test 3: Determinism (same codepoint → same projection)
void test_determinism() {
    std::cout << "Test 3: Determinism... ";

    char32_t codepoint = U'Z';

    auto result1 = CodepointProjection::project(codepoint);
    auto result2 = CodepointProjection::project(codepoint);

    // Hash should be identical
    assert(result1.hash == result2.hash && "Hash should be deterministic");

    // Position should be identical
    assert((result1.s3_position - result2.s3_position).norm() < EPSILON &&
           "Position should be deterministic");

    // Hilbert index should be identical
    assert(result1.hilbert_index == result2.hilbert_index &&
           "Hilbert index should be deterministic");

    std::cout << "PASSED\n";
}

// Test 4: Different codepoints → different projections
void test_uniqueness() {
    std::cout << "Test 4: Uniqueness... ";

    std::vector<char32_t> codepoints = {U'A', U'B', U'C', U'a', U'b', U'c', U'0', U'1', U'你', U'好'};

    std::vector<CodepointProjection::ProjectionResult> results;
    for (char32_t cp : codepoints) {
        results.push_back(CodepointProjection::project(cp));
    }

    // All hashes should be unique
    for (size_t i = 0; i < results.size(); ++i) {
        for (size_t j = i + 1; j < results.size(); ++j) {
            assert(results[i].hash != results[j].hash && "Different codepoints should have different hashes");
        }
    }

    std::cout << "PASSED\n";
}

// Test 5: Full Unicode range (sample)
void test_unicode_range() {
    std::cout << "Test 5: Full Unicode range (sample)... ";

    std::vector<char32_t> test_codepoints = {
        0x0000,      // NULL
        0x0041,      // 'A'
        0x007F,      // DEL
        0x0080,      // Start of Latin-1 Supplement
        0x4E00,      // CJK Unified Ideograph (一)
        0x1F600,     // Emoji: 😀
        0x10FFFF,    // Max valid Unicode codepoint
    };

    for (char32_t cp : test_codepoints) {
        auto result = CodepointProjection::project(cp);

        assert(result.hash.size() == 32);
        assert(std::abs(result.s3_position.norm() - 1.0) < EPSILON);

        // Verify all coordinates in valid range
        for (int i = 0; i < 4; ++i) {
            assert(result.s3_position[i] >= -1.0 && result.s3_position[i] <= 1.0);
            assert(std::isfinite(result.s3_position[i]));
        }
    }

    std::cout << "PASSED\n";
}

// Test 6: "Call me Ishmael" - The critical test case
void test_call_me_ishmael() {
    std::cout << "Test 6: \"Call me Ishmael\" (critical test)... ";

    std::u32string text = U"Call me Ishmael";

    std::vector<CodepointProjection::ProjectionResult> results;
    for (char32_t cp : text) {
        results.push_back(CodepointProjection::project(cp));
    }

    assert(results.size() == text.size());

    // Verify 'C' and 'I' (capital letters) have different projections
    auto C_result = CodepointProjection::project(U'C');
    auto I_result = CodepointProjection::project(U'I');
    assert(C_result.hash != I_result.hash);

    // Verify lowercase letters
    auto a_result = CodepointProjection::project(U'a');
    auto e_result = CodepointProjection::project(U'e');
    assert(a_result.hash != e_result.hash);

    // Verify space
    auto space_result = CodepointProjection::project(U' ');
    assert(space_result.hash.size() == 32);

    std::cout << "PASSED\n";
    std::cout << "    Projected " << text.size() << " codepoints\n";
}

// Test 7: Hash collision resistance (birthday paradox)
void test_collision_resistance() {
    std::cout << "Test 7: Hash collision resistance... ";

    const int N = 10000;
    std::unordered_set<std::string> seen_hashes;

    for (int i = 0; i < N; ++i) {
        // Use sequential codepoints
        char32_t cp = (char32_t)i;
        if (cp > 0x10FFFF) break;  // Max Unicode

        auto result = CodepointProjection::project(cp);

        // Convert hash to string for storage
        std::string hash_str(result.hash.begin(), result.hash.end());
        seen_hashes.insert(hash_str);
    }

    // Should have no collisions with BLAKE3
    assert(seen_hashes.size() == std::min(N, 0x110000) && "No hash collisions expected");

    std::cout << "PASSED\n";
}

// Test 8: Performance (can we project fast enough?)
void test_performance() {
    std::cout << "Test 8: Performance... ";

    const int N = 10000;
    auto start = std::chrono::high_resolution_clock::now();

    for (int i = 0; i < N; ++i) {
        char32_t cp = (char32_t)(i % 0x10000);  // Stay in BMP for speed
        CodepointProjection::project(cp);
    }

    auto end = std::chrono::high_resolution_clock::now();
    auto duration = std::chrono::duration_cast<std::chrono::microseconds>(end - start);

    double projections_per_second = (double)N / duration.count() * 1e6;

    std::cout << "\n    Projected " << N << " codepoints in " << duration.count() << " μs\n";
    std::cout << "    Performance: " << projections_per_second << " projections/second\n";

    // Should be reasonably fast (>100k/sec on modern CPU)
    assert(projections_per_second > 100000 && "Should achieve >100k projections/sec");

    std::cout << "    PASSED\n";
}

int main() {
    std::cout << "=== Unicode Codepoint Projection Integration Tests ===\n\n";

    try {
        test_single_codepoint();
        test_string_projection();
        test_determinism();
        test_uniqueness();
        test_unicode_range();
        test_call_me_ishmael();
        test_collision_resistance();
        test_performance();

        std::cout << "\n=== All tests PASSED ===\n";
        std::cout << "\nPipeline: Unicode → BLAKE3 → Super Fibonacci → S³ → Hilbert → Index\n";
        std::cout << "Ready for database integration!\n";
        return 0;
    }
    catch (const std::exception& e) {
        std::cerr << "\n=== TEST FAILED ===\n";
        std::cerr << "Exception: " << e.what() << "\n";
        return 1;
    }
}
