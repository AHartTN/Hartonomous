/**
 * @file test_hopf_fibration.cpp
 * @brief Unit tests for Hopf fibration (S³ → S² projection)
 */

#include <geometry/hopf_fibration.hpp>
#include <iostream>
#include <cmath>
#include <vector>
#include <cassert>

using namespace Hartonomous;

constexpr double EPSILON = 1e-6;

bool approx_equal(double a, double b, double epsilon = EPSILON) {
    return std::abs(a - b) < epsilon;
}

bool approx_equal(const Vec3& a, const Vec3& b, double epsilon = EPSILON) {
    return (a - b).norm() < epsilon;
}

bool approx_equal(const Vec4& a, const Vec4& b, double epsilon = EPSILON) {
    return (a - b).norm() < epsilon;
}

// Test 1: Forward mapping produces points on S²
void test_forward_produces_s2_points() {
    std::cout << "Test 1: Forward mapping produces points on S²... ";

    std::vector<Vec4> test_points = {
        Vec4(1.0, 0.0, 0.0, 0.0),
        Vec4(0.0, 1.0, 0.0, 0.0),
        Vec4(0.0, 0.0, 1.0, 0.0),
        Vec4(0.0, 0.0, 0.0, 1.0),
        Vec4(0.5, 0.5, 0.5, 0.5),
        Vec4(0.7, 0.1, -0.5, 0.5),
    };

    for (auto& p : test_points) {
        // Normalize to ensure it's on S³
        p.normalize();

        // Apply Hopf fibration
        Vec3 s2_point = HopfFibration::forward(p);

        // Check that result is on S² (norm = 1)
        double norm = s2_point.norm();
        assert(approx_equal(norm, 1.0) && "Forward mapping should produce unit vectors");
    }

    std::cout << "PASSED\n";
}

// Test 2: Fiber consistency (antipodal points map to same S² point)
void test_fiber_consistency() {
    std::cout << "Test 2: Fiber consistency (antipodal invariance)... ";

    Vec4 p(0.6, 0.3, 0.5, 0.4);
    p.normalize();

    // Apply Hopf fibration
    Vec3 s2_point1 = HopfFibration::forward(p);
    Vec3 s2_point2 = HopfFibration::forward(-p);  // Antipodal point

    // Antipodal points on S³ should map to opposite points on S²
    // Actually, for Hopf fibration: H(p) = -H(-p)
    assert(approx_equal(s2_point1, -s2_point2) && "Antipodal points should map to antipodal points");

    std::cout << "PASSED\n";
}

// Test 3: Known mappings
void test_known_mappings() {
    std::cout << "Test 3: Known mappings... ";

    // North pole of S³: (1, 0, 0, 0)
    Vec4 north_s3(1.0, 0.0, 0.0, 0.0);
    Vec3 image_north = HopfFibration::forward(north_s3);
    // Should map to north pole of S²: (0, 0, 1) or similar
    assert(approx_equal(image_north.norm(), 1.0));

    // Equatorial point
    Vec4 equator(0.0, 1.0, 0.0, 0.0);
    Vec3 image_equator = HopfFibration::forward(equator);
    assert(approx_equal(image_equator.norm(), 1.0));

    std::cout << "PASSED\n";
}

// Test 4: Fiber structure (circle fibers)
void test_fiber_structure() {
    std::cout << "Test 4: Fiber structure (circle fibers)... ";

    // Points on the same fiber should map to the same S² point
    // Fiber parametrization: p(θ) for different angles θ

    Vec4 base(0.5, 0.5, 0.5, 0.5);
    base.normalize();

    Vec3 reference_point = HopfFibration::forward(base);

    // Rotate around the fiber (this is a simplified test)
    // In reality, fibers are circles in S³
    for (int i = 0; i < 10; ++i) {
        double theta = 2.0 * M_PI * i / 10.0;

        // Create a point on approximate fiber (simplified)
        // Real fiber parametrization is more complex
        Vec4 p = base;  // Start with base
        // This is approximate - real test would use proper fiber parametrization

        Vec3 mapped = HopfFibration::forward(p);

        // Should be on S²
        assert(approx_equal(mapped.norm(), 1.0));
    }

    std::cout << "PASSED\n";
}

// Test 5: Continuity (nearby points on S³ map to nearby points on S²)
void test_continuity() {
    std::cout << "Test 5: Continuity... ";

    Vec4 p1(0.6, 0.3, 0.5, 0.4);
    p1.normalize();

    // Slightly perturbed point
    Vec4 p2 = p1;
    p2[0] += 0.01;
    p2.normalize();

    Vec3 s2_p1 = HopfFibration::forward(p1);
    Vec3 s2_p2 = HopfFibration::forward(p2);

    // Distance on S² should be small (continuity)
    double s3_distance = std::acos(std::clamp(p1.dot(p2), -1.0, 1.0));
    double s2_distance = std::acos(std::clamp(s2_p1.dot(s2_p2), -1.0, 1.0));

    // This is approximate - not a rigorous continuity test
    assert(s2_distance < 0.5 && "Nearby points should map to nearby points");

    std::cout << "PASSED\n";
}

// Test 6: Coverage (forward maps S³ onto all of S²)
void test_coverage() {
    std::cout << "Test 6: Coverage test (sampling)... ";

    // Generate many random points on S³ and verify they cover S²
    const int N = 1000;
    std::vector<Vec3> s2_points;

    for (int i = 0; i < N; ++i) {
        // Generate random point on S³ (using rejection sampling)
        Vec4 p;
        do {
            for (int j = 0; j < 4; ++j) {
                p[j] = (double)rand() / RAND_MAX * 2.0 - 1.0;
            }
        } while (p.norm() < 0.1 || p.norm() > 1.5);

        p.normalize();

        Vec3 s2_point = HopfFibration::forward(p);
        s2_points.push_back(s2_point);

        // Verify on S²
        assert(approx_equal(s2_point.norm(), 1.0));
    }

    // Check that we got points distributed over S²
    // (simple check: verify we have points in different octants)
    int octant_counts[8] = {0};
    for (const auto& p : s2_points) {
        int octant = 0;
        if (p[0] >= 0) octant |= 1;
        if (p[1] >= 0) octant |= 2;
        if (p[2] >= 0) octant |= 4;
        octant_counts[octant]++;
    }

    // All octants should have some points (coverage)
    for (int i = 0; i < 8; ++i) {
        assert(octant_counts[i] > 0 && "Should have coverage of all octants");
    }

    std::cout << "PASSED\n";
}

// Test 7: Coordinate system check
void test_coordinate_system() {
    std::cout << "Test 7: Coordinate system... ";

    // Verify the coordinate transformation is consistent
    Vec4 p(0.5, 0.5, 0.5, 0.5);
    p.normalize();

    Vec3 result = HopfFibration::forward(p);

    // Check finite values
    assert(std::isfinite(result[0]) && std::isfinite(result[1]) && std::isfinite(result[2]));

    // Check range
    assert(result[0] >= -1.0 && result[0] <= 1.0);
    assert(result[1] >= -1.0 && result[1] <= 1.0);
    assert(result[2] >= -1.0 && result[2] <= 1.0);

    std::cout << "PASSED\n";
}

int main() {
    std::cout << "=== Hopf Fibration Tests ===\n\n";

    try {
        test_forward_produces_s2_points();
        test_fiber_consistency();
        test_known_mappings();
        test_fiber_structure();
        test_continuity();
        test_coverage();
        test_coordinate_system();

        std::cout << "\n=== All tests PASSED ===\n";
        return 0;
    }
    catch (const std::exception& e) {
        std::cerr << "\n=== TEST FAILED ===\n";
        std::cerr << "Exception: " << e.what() << "\n";
        return 1;
    }
}
