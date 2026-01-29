/**
 * @file test_hopf_fibration.cpp
 * @brief Unit tests for Hopf fibration (S³ → S² projection) using Google Test
 */

#include <gtest/gtest.h>
#include <geometry/hopf_fibration.hpp>
#include <vector>
#include <cmath>

using namespace hartonomous::geometry;
using Vec3 = HopfFibration::Vec3;
using Vec4 = HopfFibration::Vec4;

TEST(HopfFibrationTest, ForwardMappingProducesS2Points) {
    std::vector<Vec4> test_points = {
        Vec4(1.0, 0.0, 0.0, 0.0),
        Vec4(0.0, 1.0, 0.0, 0.0),
        Vec4(0.0, 0.0, 1.0, 0.0),
        Vec4(0.0, 0.0, 0.0, 1.0),
        Vec4(0.5, 0.5, 0.5, 0.5).normalized(),
        Vec4(0.7, 0.1, -0.5, 0.5).normalized(),
    };

    for (const auto& p : test_points) {
        Vec3 s2_point = HopfFibration::forward(p);
        EXPECT_NEAR(s2_point.norm(), 1.0, 1e-9);
    }
}

TEST(HopfFibrationTest, FiberConsistency) {
    Vec4 p = Vec4(0.6, 0.3, 0.5, 0.4).normalized();
    
    // A point on the same fiber is obtained by right-multiplying by a complex phase e^(i*theta)
    // z' = z * e^(i*theta)
    // (z1', z2') = (z1*cos(t)-z2*sin(t), z1*sin(t)+z2*cos(t)) is not it.
    // The fiber action is p -> p * q where q is a unit quaternion, which corresponds to p -> exp(i*theta) * p
    // (x1,x2,x3,x4) -> (x1*cos(t)-x2*sin(t), x1*sin(t)+x2*cos(t), x3, x4) is a rotation in a plane.
    
    // The Hopf fibration maps great circles on S³ to points on S².
    // Let's test a simpler property: H(p) = H(p * e^(i*theta)) where multiplication is complex.
    // Let p = (z1, z2). Then p' = (z1*e^(i*t), z2*e^(i*t)).
    // |z1'|^2-|z2'|^2 = |z1|^2|e^(i*t)|^2 - |z2|^2|e^(i*t)|^2 = |z1|^2-|z2|^2. (Same x)
    // 2*Re(z1'*conj(z2')) = 2*Re(z1*e^(i*t)*conj(z2)*e^(-i*t)) = 2*Re(z1*conj(z2)). (Same y,z)
    
    Vec3 s2_point1 = HopfFibration::forward(p);
    
    // Create another point on the same fiber
    double theta = M_PI / 4.0;
    HopfFibration::Complex z1(p[0], p[1]);
    HopfFibration::Complex z2(p[2], p[3]);
    HopfFibration::Complex phase = std::polar(1.0, theta);
    
    z1 *= phase;
    z2 *= phase;
    
    Vec4 p_fiber(z1.real(), z1.imag(), z2.real(), z2.imag());
    
    Vec3 s2_point2 = HopfFibration::forward(p_fiber);

    EXPECT_NEAR((s2_point1 - s2_point2).norm(), 0.0, 1e-9);
}


TEST(HopfFibrationTest, KnownMappings) {
    // North pole of S³ -> North pole of S²
    Vec4 north_s3(1.0, 0.0, 0.0, 0.0); // z1=1, z2=0
    Vec3 image_north = HopfFibration::forward(north_s3);
    EXPECT_NEAR((image_north - Vec3(1.0, 0.0, 0.0)).norm(), 0.0, 1e-9);

    // South pole of S³ -> North pole of S²
    Vec4 south_s3(0.0, 0.0, 1.0, 0.0); // z1=0, z2=1
    Vec3 image_south = HopfFibration::forward(south_s3);
    EXPECT_NEAR((image_south - Vec3(-1.0, 0.0, 0.0)).norm(), 0.0, 1e-9);

    // A point on the equator of S³
    Vec4 equator_s3 = Vec4(1.0/sqrt(2.0), 0.0, 1.0/sqrt(2.0), 0.0).normalized();
    Vec3 image_equator = HopfFibration::forward(equator_s3);
    EXPECT_NEAR(image_equator[0], 0.0, 1e-9); // |z1|^2 - |z2|^2 = 0.5 - 0.5 = 0
}

TEST(HopfFibrationTest, InverseMapping) {
    Vec3 s2_point = Vec3(0.5, 0.5, 1.0/sqrt(2.0)).normalized();
    
    // Test inverse for a few fiber angles
    for (double angle = 0; angle < 2*M_PI; angle += M_PI/2) {
        Vec4 s3_point = HopfFibration::inverse(s2_point, angle);
        
        // Check if it's on S³
        EXPECT_NEAR(s3_point.norm(), 1.0, 1e-9);
        
        // Check that it maps back to the original S² point
        Vec3 s2_point_rt = HopfFibration::forward(s3_point);
        EXPECT_NEAR((s2_point - s2_point_rt).norm(), 0.0, 1e-9);
    }
}

TEST(HopfFibrationTest, Continuity) {
    Vec4 p1 = Vec4(0.6, 0.3, 0.5, 0.4).normalized();
    Vec3 s2_p1 = HopfFibration::forward(p1);

    // Create a very small perturbation on S³
    double delta = 1e-6; // Ensure a very small S³ distance
    Vec4 p2 = (p1 + Vec4(delta, -delta, delta, -delta)).normalized();
    Vec3 s2_p2 = HopfFibration::forward(p2);

    double s3_distance = HopfFibration::distance_s3(p1, p2);
    double s2_distance = std::acos(std::clamp(s2_p1.dot(s2_p2), -1.0, 1.0));

    // For a continuous map, a very small input distance should result in a very small output distance.
    // The exact relationship s2_distance <= s3_distance is not universally guaranteed due to distortion.
    // However, if s3_distance is very small, s2_distance must also be very small.
    // We expect both distances to be small and within a reasonable order of magnitude of each other.
    EXPECT_LT(s3_distance, 1e-5); // Ensure that the S3 points are indeed very close
    EXPECT_LT(s2_distance, 1e-4); // S2 distance must also be very small (e.g., within a factor of 10)
    EXPECT_GT(s2_distance, 0.0);  // Ensure points are different and distance is non-zero
}
