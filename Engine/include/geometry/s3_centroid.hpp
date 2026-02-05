#pragma once

#include <Eigen/Core>
#include <stddef.h>

namespace Hartonomous::Geometry {

/**
 * @brief Computes the centroid of a set of points on the 3-sphere (S3).
 * 
 * The centroid is computed by summing the 4D vectors and projecting the 
 * result back onto the surface of the hypersphere.
 * 
 * @param points_4d Pointer to a flat array of 4D coordinates (x, y, z, w)
 * @param count Number of 4D points in the array
 * @return Eigen::Vector4d The normalized centroid on S3
 */
Eigen::Vector4d compute_s3_centroid(const double* points_4d, size_t count);

} // namespace Hartonomous::Geometry
