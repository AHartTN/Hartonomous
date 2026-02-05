/**
 * @file s3_centroid.cpp
 * @brief Implementation of centroid calculation for S3 points.
 */

#include <geometry/s3_centroid.hpp>
#include <Eigen/Dense>
#include <vector>

namespace Hartonomous::Geometry {

Eigen::Vector4d compute_s3_centroid(const double* points_4d, size_t count) {
    if (count == 0) {
        return Eigen::Vector4d(1, 0, 0, 0); // Default to a valid point on S3
    }

    Eigen::Vector4d sum = Eigen::Vector4d::Zero();
    for (size_t i = 0; i < count; ++i) {
        sum[0] += points_4d[i * 4 + 0];
        sum[1] += points_4d[i * 4 + 1];
        sum[2] += points_4d[i * 4 + 2];
        sum[3] += points_4d[i * 4 + 3];
    }

    double norm = sum.norm();
    if (norm > 1e-15) {
        sum /= norm;
    } else {
        // Points are perfectly antipodal or zero? Fallback to first point or default.
        sum = Eigen::Vector4d(points_4d[0], points_4d[1], points_4d[2], points_4d[3]);
        norm = sum.norm();
        if (norm > 1e-15) sum /= norm;
        else sum = Eigen::Vector4d(1, 0, 0, 0);
    }

    return sum;
}

} // namespace Hartonomous::Geometry
