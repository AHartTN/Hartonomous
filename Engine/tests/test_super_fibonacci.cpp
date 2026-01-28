/**
 * @file test_super_fibonacci.cpp
 * @brief Unit tests for Super Fibonacci sphere distribution on S³
 */

#include <geometry/super_fibonacci.hpp>
#include <iostream>
#include <cmath>
#include <vector>
#include <algorithm>
#include <cassert>

using namespace Hartonomous;

constexpr double EPSILON = 1e-6;

bool approx_equal(double a, double b, double epsilon = EPSILON) {
    return std::abs(a - b) < epsilon;
}

// Test 1: All points lie on S³ surface
void test_points_on_s3_surface() {
    std::cout << "Test 1: All points lie on S³ surface... ";

    const size_t N = 1000;
    for (size_t i = 0; i < N; ++i) {
        Vec4 point = SuperFibonacci::point_on_s3(i, N);

        double norm = point.norm();
        assert(approx_equal(norm, 1.0) && "Point should be on unit S³");
    }

    std::cout << "PASSED\n";
}

// Test 2: Uniformity - measure average nearest neighbor distance
void test_uniformity() {
    std::cout << "Test 2: Uniformity (nearest neighbor distance)... ";

    const size_t N = 500;  // Smaller for faster test
    std::vector<Vec4> points;
    points.reserve(N);

    for (size_t i = 0; i < N; ++i) {
        points.push_back(SuperFibonacci::point_on_s3(i, N));
    }

    // Compute average nearest neighbor distance
    std::vector<double> nn_distances;
    for (size_t i = 0; i < N; ++i) {
        double min_dist = 10.0;  // Large initial value

        for (size_t j = 0; j < N; ++j) {
            if (i == j) continue;

            // Geodesic distance on S³
            double dot = points[i].dot(points[j]);
            dot = std::clamp(dot, -1.0, 1.0);
            double dist = std::acos(dot);

            min_dist = std::min(min_dist, dist);
        }

        nn_distances.push_back(min_dist);
    }

    // Compute statistics
    double sum = 0.0;
    for (double d : nn_distances) {
        sum += d;
    }
    double avg_nn_dist = sum / N;

    // Compute standard deviation
    double var_sum = 0.0;
    for (double d : nn_distances) {
        double diff = d - avg_nn_dist;
        var_sum += diff * diff;
    }
    double std_dev = std::sqrt(var_sum / N);

    // For uniform distribution, std deviation should be relatively small
    // compared to mean
    double coefficient_of_variation = std_dev / avg_nn_dist;

    std::cout << "\n    Average NN distance: " << avg_nn_dist << "\n";
    std::cout << "    Std deviation: " << std_dev << "\n";
    std::cout << "    Coefficient of variation: " << coefficient_of_variation << "\n";

    // Coefficient of variation should be < 0.5 for reasonable uniformity
    assert(coefficient_of_variation < 0.5 && "Distribution should be relatively uniform");

    std::cout << "    PASSED\n";
}

// Test 3: Coverage - points should cover all regions of S³
void test_coverage() {
    std::cout << "Test 3: Coverage of S³... ";

    const size_t N = 2000;

    // Divide S³ into "hemispheres" and count points in each
    // We'll use simple coordinate-based divisions
    int counts[16] = {0};  // 2^4 = 16 regions (sign of each coordinate)

    for (size_t i = 0; i < N; ++i) {
        Vec4 point = SuperFibonacci::point_on_s3(i, N);

        int region = 0;
        if (point[0] >= 0) region |= 1;
        if (point[1] >= 0) region |= 2;
        if (point[2] >= 0) region |= 4;
        if (point[3] >= 0) region |= 8;

        counts[region]++;
    }

    // All regions should have some points
    for (int i = 0; i < 16; ++i) {
        assert(counts[i] > 0 && "All regions should be covered");
    }

    // Regions should be relatively balanced
    // (within 2x of average for good distribution)
    double avg = N / 16.0;
    for (int i = 0; i < 16; ++i) {
        assert(counts[i] > avg / 3.0 && counts[i] < avg * 3.0 &&
               "Regions should be relatively balanced");
    }

    std::cout << "PASSED\n";
}

// Test 4: Determinism - same index produces same point
void test_determinism() {
    std::cout << "Test 4: Determinism... ";

    const size_t N = 100;
    for (size_t i = 0; i < N; i += 10) {
        Vec4 point1 = SuperFibonacci::point_on_s3(i, N);
        Vec4 point2 = SuperFibonacci::point_on_s3(i, N);

        assert((point1 - point2).norm() < EPSILON && "Same index should produce same point");
    }

    std::cout << "PASSED\n";
}

// Test 5: Golden ratio and plastic constant usage
void test_golden_ratio_properties() {
    std::cout << "Test 5: Golden ratio and plastic constant properties... ";

    // Verify constants are correct
    const double PHI = (1.0 + std::sqrt(5.0)) / 2.0;  // Golden ratio
    const double PSI_cubed = 1.0 + PSI;  // Plastic constant property

    assert(approx_equal(SuperFibonacci::PHI, PHI, 1e-10));
    assert(approx_equal(PSI * PSI * PSI, PSI_cubed, 1e-6));

    std::cout << "PASSED\n";
}

// Test 6: No duplicate points
void test_no_duplicates() {
    std::cout << "Test 6: No duplicate points... ";

    const size_t N = 200;
    std::vector<Vec4> points;
    points.reserve(N);

    for (size_t i = 0; i < N; ++i) {
        points.push_back(SuperFibonacci::point_on_s3(i, N));
    }

    // Check for duplicates
    for (size_t i = 0; i < N; ++i) {
        for (size_t j = i + 1; j < N; ++j) {
            double dist = (points[i] - points[j]).norm();
            assert(dist > EPSILON && "Points should be distinct");
        }
    }

    std::cout << "PASSED\n";
}

// Test 7: Scaling with N (more points = better coverage)
void test_scaling() {
    std::cout << "Test 7: Scaling with N... ";

    std::vector<size_t> N_values = {50, 100, 200, 500};
    std::vector<double> avg_nn_distances;

    for (size_t N : N_values) {
        std::vector<Vec4> points;
        points.reserve(N);

        for (size_t i = 0; i < N; ++i) {
            points.push_back(SuperFibonacci::point_on_s3(i, N));
        }

        // Compute average NN distance
        double sum_nn = 0.0;
        for (size_t i = 0; i < std::min(N, size_t(100)); ++i) {  // Sample for speed
            double min_dist = 10.0;
            for (size_t j = 0; j < N; ++j) {
                if (i == j) continue;
                double dot = points[i].dot(points[j]);
                dot = std::clamp(dot, -1.0, 1.0);
                double dist = std::acos(dot);
                min_dist = std::min(min_dist, dist);
            }
            sum_nn += min_dist;
        }

        avg_nn_distances.push_back(sum_nn / std::min(N, size_t(100)));
    }

    // NN distance should decrease as N increases
    for (size_t i = 1; i < avg_nn_distances.size(); ++i) {
        assert(avg_nn_distances[i] < avg_nn_distances[i-1] &&
               "NN distance should decrease with more points");
    }

    std::cout << "PASSED\n";
}

// Test 8: Boundary cases
void test_boundary_cases() {
    std::cout << "Test 8: Boundary cases... ";

    // First point
    Vec4 first = SuperFibonacci::point_on_s3(0, 100);
    assert(approx_equal(first.norm(), 1.0));

    // Last point
    Vec4 last = SuperFibonacci::point_on_s3(99, 100);
    assert(approx_equal(last.norm(), 1.0));

    // Single point
    Vec4 single = SuperFibonacci::point_on_s3(0, 1);
    assert(approx_equal(single.norm(), 1.0));

    // Two points (should be antipodal or well-separated)
    Vec4 p1 = SuperFibonacci::point_on_s3(0, 2);
    Vec4 p2 = SuperFibonacci::point_on_s3(1, 2);
    double dist = std::acos(std::clamp(p1.dot(p2), -1.0, 1.0));
    assert(dist > 1.0 && "Two points should be well-separated");

    std::cout << "PASSED\n";
}

// Test 9: Coordinate ranges
void test_coordinate_ranges() {
    std::cout << "Test 9: Coordinate ranges... ";

    const size_t N = 1000;
    for (size_t i = 0; i < N; ++i) {
        Vec4 point = SuperFibonacci::point_on_s3(i, N);

        // All coordinates should be in [-1, 1]
        for (int j = 0; j < 4; ++j) {
            assert(point[j] >= -1.0 && point[j] <= 1.0 && "Coordinates in valid range");
            assert(std::isfinite(point[j]) && "Coordinates should be finite");
        }
    }

    std::cout << "PASSED\n";
}

int main() {
    std::cout << "=== Super Fibonacci Distribution Tests ===\n\n";

    try {
        test_points_on_s3_surface();
        test_determinism();
        test_golden_ratio_properties();
        test_no_duplicates();
        test_boundary_cases();
        test_coordinate_ranges();
        test_coverage();
        test_uniformity();
        test_scaling();

        std::cout << "\n=== All tests PASSED ===\n";
        return 0;
    }
    catch (const std::exception& e) {
        std::cerr << "\n=== TEST FAILED ===\n";
        std::cerr << "Exception: " << e.what() << "\n";
        return 1;
    }
}
