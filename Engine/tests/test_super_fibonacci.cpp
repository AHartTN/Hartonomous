/**
 * @file test_super_fibonacci.cpp
 * @brief Unit tests for Super Fibonacci sphere distribution on S³ using Google Test
 */

#include <gtest/gtest.h>
#include <geometry/super_fibonacci.hpp>
#include <vector>
#include <set>
#include <cmath>

using namespace hartonomous::geometry;
using Vec4 = SuperFibonacci::Vec4;

TEST(SuperFibonacciTest, PointsAreOnS3Surface) {
    const size_t N = 100;
    for (size_t i = 0; i < N; ++i) {
        Vec4 point = SuperFibonacci::point_on_s3(i, N);
        EXPECT_NEAR(point.norm(), 1.0, 1e-9);
    }
}

TEST(SuperFibonacciTest, Determinism) {
    const size_t N = 100;
    for (size_t i = 0; i < N; i += 10) {
        Vec4 point1 = SuperFibonacci::point_on_s3(i, N);
        Vec4 point2 = SuperFibonacci::point_on_s3(i, N);
        EXPECT_NEAR((point1 - point2).norm(), 0.0, 1e-9);
    }
}

TEST(SuperFibonacciTest, MathematicalConstants) {
    const double PHI = (1.0 + std::sqrt(5.0)) / 2.0;
    const double PSI = 1.324717957244746;
    EXPECT_NEAR(SuperFibonacci::PHI, PHI, 1e-10);
    EXPECT_NEAR(SuperFibonacci::PSI, PSI, 1e-10);
    // Property of plastic constant: PSI^3 = PSI + 1
    EXPECT_NEAR(SuperFibonacci::PSI * SuperFibonacci::PSI * SuperFibonacci::PSI, SuperFibonacci::PSI + 1.0, 1e-9);
}

TEST(SuperFibonacciTest, BoundaryCases) {
    // Single point
    Vec4 single = SuperFibonacci::point_on_s3(0, 1);
    EXPECT_NEAR(single.norm(), 1.0, 1e-9);

    // Two points
    Vec4 p1 = SuperFibonacci::point_on_s3(0, 2);
    Vec4 p2 = SuperFibonacci::point_on_s3(1, 2);
    EXPECT_NEAR(p1.norm(), 1.0, 1e-9);
    EXPECT_NEAR(p2.norm(), 1.0, 1e-9);
    // They should be reasonably far apart
    double dist = std::acos(std::clamp(p1.dot(p2), -1.0, 1.0));
    EXPECT_GT(dist, 1.0);
}

TEST(SuperFibonacciTest, HashToPoint) {
    std::array<uint8_t, 16> hash1;
    hash1.fill(0);
    Vec4 p1 = SuperFibonacci::hash_to_point(hash1.data());
    EXPECT_NEAR(p1.norm(), 1.0, 1e-9);

    std::array<uint8_t, 16> hash2;
    hash2.fill(0xFF);
    Vec4 p2 = SuperFibonacci::hash_to_point(hash2.data());
    EXPECT_NEAR(p2.norm(), 1.0, 1e-9);

    // Different hashes should produce different points
    EXPECT_GT((p1 - p2).norm(), 1e-6);
}

TEST(SuperFibonacciTest, Uniformity) {
    const size_t N = 200;
    auto points = SuperFibonacci::generate_points(N);

    std::vector<double> nn_distances;
    for (size_t i = 0; i < N; ++i) {
        double min_dist = 10.0;
        for (size_t j = 0; j < N; ++j) {
            if (i == j) continue;
            double dist = std::acos(std::clamp(points[i].dot(points[j]), -1.0, 1.0));
            min_dist = std::min(min_dist, dist);
        }
        nn_distances.push_back(min_dist);
    }

    double sum = 0.0;
    for (double d : nn_distances) {
        sum += d;
    }
    double avg_nn_dist = sum / N;

    double var_sum = 0.0;
    for (double d : nn_distances) {
        double diff = d - avg_nn_dist;
        var_sum += diff * diff;
    }
    double std_dev = std::sqrt(var_sum / N);

    double coefficient_of_variation = std_dev / avg_nn_dist;
    // This is a statistical test, but it's a good sanity check.
    // A perfectly uniform distribution would have a very low CoV.
    EXPECT_LT(coefficient_of_variation, 0.5);
}