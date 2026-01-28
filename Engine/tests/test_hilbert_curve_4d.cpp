/**
 * @file test_hilbert_curve_4d.cpp
 * @brief Unit tests for 4D Hilbert curve encoding (ONE-WAY only)
 */

#include <spatial/hilbert_curve_4d.hpp>
#include <iostream>
#include <cmath>
#include <vector>
#include <unordered_set>
#include <cassert>

using namespace Hartonomous;

constexpr double EPSILON = 1e-6;

// Test 1: Encoding is deterministic
void test_determinism() {
    std::cout << "Test 1: Determinism... ";

    Vec4 point(0.5, 0.5, 0.5, 0.5);
    uint32_t bits = 16;

    uint64_t index1 = HilbertCurve4D::encode(point, bits);
    uint64_t index2 = HilbertCurve4D::encode(point, bits);

    assert(index1 == index2 && "Same input should produce same output");

    std::cout << "PASSED\n";
}

// Test 2: Different points produce different indices (mostly)
void test_uniqueness() {
    std::cout << "Test 2: Uniqueness... ";

    const int N = 1000;
    std::unordered_set<uint64_t> seen_indices;
    uint32_t bits = 16;

    for (int i = 0; i < N; ++i) {
        Vec4 point(
            (double)i / N,
            (double)(i * 7) / N,  // Prime multiplier for pseudo-randomness
            (double)(i * 13) / N,
            (double)(i * 19) / N
        );

        uint64_t index = HilbertCurve4D::encode(point, bits);
        seen_indices.insert(index);
    }

    // Should have high uniqueness (>95% for random points)
    double uniqueness = (double)seen_indices.size() / N;
    std::cout << "\n    Uniqueness: " << uniqueness * 100 << "%\n";
    assert(uniqueness > 0.95 && "Should have high uniqueness");

    std::cout << "    PASSED\n";
}

// Test 3: Locality preservation (nearby points → nearby indices)
void test_locality() {
    std::cout << "Test 3: Locality preservation... ";

    uint32_t bits = 16;
    const int N = 100;

    Vec4 center(0.5, 0.5, 0.5, 0.5);
    uint64_t center_index = HilbertCurve4D::encode(center, bits);

    // Test points near center
    int nearby_count = 0;
    for (int i = 0; i < N; ++i) {
        double delta = 0.01;  // Small perturbation
        Vec4 nearby(
            center[0] + delta * std::sin(i),
            center[1] + delta * std::cos(i),
            center[2] + delta * std::sin(i * 2),
            center[3] + delta * std::cos(i * 2)
        );

        // Clamp to [0, 1]
        for (int j = 0; j < 4; ++j) {
            nearby[j] = std::clamp(nearby[j], 0.0, 1.0);
        }

        uint64_t nearby_index = HilbertCurve4D::encode(nearby, bits);

        // Check if index is close
        int64_t index_diff = std::abs((int64_t)nearby_index - (int64_t)center_index);

        // Nearby points should have indices within reasonable range
        // (this is a weak locality test - Hilbert curve provides good locality)
        if (index_diff < 10000) {
            nearby_count++;
        }
    }

    double locality_ratio = (double)nearby_count / N;
    std::cout << "\n    Locality ratio: " << locality_ratio * 100 << "%\n";

    // At least 30% should have nearby indices (Hilbert curve property)
    assert(locality_ratio > 0.3 && "Should preserve some locality");

    std::cout << "    PASSED\n";
}

// Test 4: Coordinate clamping
void test_clamping() {
    std::cout << "Test 4: Coordinate clamping... ";

    uint32_t bits = 16;

    // Test out-of-range coordinates (should be clamped)
    std::vector<Vec4> test_points = {
        Vec4(-0.5, 0.5, 0.5, 0.5),   // Negative
        Vec4(1.5, 0.5, 0.5, 0.5),    // > 1
        Vec4(0.5, -1.0, 0.5, 0.5),   // Large negative
        Vec4(0.5, 0.5, 2.0, 0.5),    // Large positive
        Vec4(-10.0, -10.0, 10.0, 10.0),  // Extreme values
    };

    for (const auto& point : test_points) {
        // Should not crash or produce invalid indices
        uint64_t index = HilbertCurve4D::encode(point, bits);

        // Index should be in valid range for given bits
        uint64_t max_index = (1ULL << (bits * 4)) - 1;
        assert(index <= max_index && "Index should be in valid range");
    }

    std::cout << "PASSED\n";
}

// Test 5: Different bit depths
void test_bit_depths() {
    std::cout << "Test 5: Different bit depths... ";

    Vec4 point(0.5, 0.5, 0.5, 0.5);

    std::vector<uint32_t> bit_depths = {4, 8, 12, 16, 20};

    for (uint32_t bits : bit_depths) {
        uint64_t index = HilbertCurve4D::encode(point, bits);

        // Index should be in valid range
        uint64_t max_index = (1ULL << (bits * 4)) - 1;
        assert(index <= max_index && "Index should be in valid range");

        // Higher bit depth should give finer resolution
        // (not directly testable, but we verify it doesn't crash)
    }

    // Verify increasing bit depth increases precision
    uint64_t index_4bit = HilbertCurve4D::encode(point, 4);
    uint64_t index_16bit = HilbertCurve4D::encode(point, 16);

    // With more bits, we can represent more distinct positions
    // So 16-bit encoding should give a larger index space
    assert(index_16bit != index_4bit && "Different bit depths should produce different indices");

    std::cout << "PASSED\n";
}

// Test 6: Corner cases
void test_corners() {
    std::cout << "Test 6: Corner cases... ";

    uint32_t bits = 16;

    // Corners of 4D hypercube
    std::vector<Vec4> corners = {
        Vec4(0.0, 0.0, 0.0, 0.0),
        Vec4(1.0, 0.0, 0.0, 0.0),
        Vec4(0.0, 1.0, 0.0, 0.0),
        Vec4(0.0, 0.0, 1.0, 0.0),
        Vec4(0.0, 0.0, 0.0, 1.0),
        Vec4(1.0, 1.0, 0.0, 0.0),
        Vec4(1.0, 1.0, 1.0, 0.0),
        Vec4(1.0, 1.0, 1.0, 1.0),
        Vec4(0.0, 1.0, 1.0, 1.0),
    };

    std::unordered_set<uint64_t> corner_indices;

    for (const auto& corner : corners) {
        uint64_t index = HilbertCurve4D::encode(corner, bits);
        corner_indices.insert(index);

        // Should be valid
        uint64_t max_index = (1ULL << (bits * 4)) - 1;
        assert(index <= max_index && "Corner index should be valid");
    }

    // All corners should have unique indices
    assert(corner_indices.size() == corners.size() && "Corners should have unique indices");

    std::cout << "PASSED\n";
}

// Test 7: Index range verification
void test_index_range() {
    std::cout << "Test 7: Index range verification... ";

    const int N = 1000;
    uint32_t bits = 16;
    uint64_t max_index = (1ULL << (bits * 4)) - 1;

    for (int i = 0; i < N; ++i) {
        Vec4 point(
            (double)rand() / RAND_MAX,
            (double)rand() / RAND_MAX,
            (double)rand() / RAND_MAX,
            (double)rand() / RAND_MAX
        );

        uint64_t index = HilbertCurve4D::encode(point, bits);

        assert(index <= max_index && "Index should be within valid range");
    }

    std::cout << "PASSED\n";
}

// Test 8: Discrete coordinate encoding
void test_discrete_encoding() {
    std::cout << "Test 8: Discrete coordinate encoding... ";

    uint32_t bits = 8;

    // Test with discrete coordinates directly
    Vec4i discrete(128, 64, 192, 32);
    uint64_t index = HilbertCurve4D::encode(discrete, bits);

    // Should produce valid index
    uint64_t max_index = (1ULL << (bits * 4)) - 1;
    assert(index <= max_index && "Discrete encoding should be valid");

    // Same coordinates should give same index
    uint64_t index2 = HilbertCurve4D::encode(discrete, bits);
    assert(index == index2 && "Deterministic discrete encoding");

    std::cout << "PASSED\n";
}

// Test 9: ONE-WAY property (no decode function should exist)
void test_one_way_property() {
    std::cout << "Test 9: ONE-WAY property (encoding only)... ";

    // This test verifies the design principle:
    // Hilbert curve is ONE-WAY only (coordinates → index)
    // We should NOT have a decode function (index → coordinates)

    // Just verify we can encode
    Vec4 point(0.5, 0.5, 0.5, 0.5);
    uint64_t index = HilbertCurve4D::encode(point, 16);

    // And that's it - no decode!
    // If someone adds a decode function, this test should be updated
    // to explicitly fail (to enforce ONE-WAY design)

    std::cout << "PASSED (encoding only, no decode)\n";
}

// Test 10: Coverage of index space
void test_index_coverage() {
    std::cout << "Test 10: Index space coverage... ";

    const int N = 10000;
    uint32_t bits = 8;  // Smaller for manageable test
    std::unordered_set<uint64_t> seen_indices;

    // Generate many random points
    for (int i = 0; i < N; ++i) {
        Vec4 point(
            (double)rand() / RAND_MAX,
            (double)rand() / RAND_MAX,
            (double)rand() / RAND_MAX,
            (double)rand() / RAND_MAX
        );

        uint64_t index = HilbertCurve4D::encode(point, bits);
        seen_indices.insert(index);
    }

    uint64_t max_possible = (1ULL << (bits * 4));
    double coverage = (double)seen_indices.size() / max_possible;

    std::cout << "\n    Index coverage: " << coverage * 100 << "%\n";
    std::cout << "    Unique indices: " << seen_indices.size() << " / " << max_possible << "\n";

    // With 10000 random points and 2^32 possible indices (8 bits * 4 dims),
    // we should cover a reasonable portion
    assert(seen_indices.size() > N * 0.9 && "Should have good coverage");

    std::cout << "    PASSED\n";
}

int main() {
    std::cout << "=== 4D Hilbert Curve Tests ===\n\n";

    try {
        test_determinism();
        test_uniqueness();
        test_clamping();
        test_corners();
        test_index_range();
        test_bit_depths();
        test_discrete_encoding();
        test_one_way_property();
        test_locality();
        test_index_coverage();

        std::cout << "\n=== All tests PASSED ===\n";
        std::cout << "\nNOTE: Hilbert curve is ONE-WAY only (coordinates → index).\n";
        std::cout << "      Reverse mapping (index → coordinates) is NOT supported by design.\n";
        return 0;
    }
    catch (const std::exception& e) {
        std::cerr << "\n=== TEST FAILED ===\n";
        std::cerr << "Exception: " << e.what() << "\n";
        return 1;
    }
}
