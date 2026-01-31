/**
 * @file test_geometry_core.cpp
 * @brief Unit tests for core S3 geometry (vec, distance, bbox)
 */

#include <gtest/gtest.h>
#include <geometry/s3_vec.hpp>
#include <geometry/s3_distance.hpp>
#include <geometry/s3_bbox.hpp>
#include <cmath>

using namespace s3;

TEST(GeometryCoreTest, VectorNormalization) {
    Vec4 v = {1.0, 2.0, 3.0, 4.0};
    normalize(v);
    
    double mag = std::sqrt(dot(v, v));
    EXPECT_NEAR(mag, 1.0, 1e-12);
}

TEST(GeometryCoreTest, GeodesicDistance) {
    // 90 degree separation on S3
    Vec4 a = {1.0, 0.0, 0.0, 0.0};
    Vec4 b = {0.0, 1.0, 0.0, 0.0};
    
    double dist = geodesic_distance(a, b);
    EXPECT_NEAR(dist, M_PI / 2.0, 1e-12);
    
    // Identical points
    EXPECT_NEAR(geodesic_distance(a, a), 0.0, 1e-12);
    
    // Opposite points
    Vec4 c = {-1.0, 0.0, 0.0, 0.0};
    EXPECT_NEAR(geodesic_distance(a, c), M_PI, 1e-12);
}

TEST(GeometryCoreTest, FastGeodesicDistance) {
    Vec4 a = {1.0, 0.0, 0.0, 0.0};
    Vec4 b = {0.0, 1.0, 0.0, 0.0}; // 90 deg
    
    double slow = geodesic_distance(a, b);
    double fast = geodesic_distance_fast_core(a, b);
    
    EXPECT_NEAR(slow, fast, 1e-12);
}

TEST(GeometryCoreTest, EuclideanDistance4D) {
    Vec4 a = {0.0, 0.0, 0.0, 0.0};
    Vec4 b = {1.0, 1.0, 1.0, 1.0};
    
    double dist = euclidean_distance(a, b);
    EXPECT_NEAR(dist, 2.0, 1e-12); // sqrt(1+1+1+1) = 2
}

TEST(GeometryCoreTest, BBoxOperations) {
    Vec4 p1 = {-1.0, -1.0, -1.0, -1.0};
    Vec4 p2 = {1.0, 1.0, 1.0, 1.0};
    
    BBox4 box = bbox_from_point(p1);
    bbox_expand(box, p2);
    
    for(int i=0; i<4; ++i) {
        EXPECT_EQ(box.min[i], -1.0);
        EXPECT_EQ(box.max[i], 1.0);
    }
    
    // Distance from center (0,0,0,0) to box should be 0
    Vec4 origin = {0.0, 0.0, 0.0, 0.0};
    EXPECT_EQ(distance_point_bbox(origin, box), 0.0);
    
    // Distance from far point
    Vec4 far = {2.0, 0.0, 0.0, 0.0};
    EXPECT_EQ(distance_point_bbox(far, box), 1.0); // 2.0 - 1.0 = 1.0
}
