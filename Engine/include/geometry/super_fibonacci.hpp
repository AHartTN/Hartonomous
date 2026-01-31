#pragma once

#include <Eigen/Core>
#include <vector>
#include <cstdint>

// Define API export macro if not already available
#ifndef HARTONOMOUS_API
    #if defined(_WIN32)
        #if defined(HARTONOMOUS_EXPORT)
            #define HARTONOMOUS_API __declspec(dllexport)
        #else
            #define HARTONOMOUS_API __declspec(dllimport)
        #endif
    #else
        #define HARTONOMOUS_API __attribute__((visibility("default")))
    #endif
#endif

namespace hartonomous::geometry {

/**
 * @brief Deterministic Super Fibonacci Spiral Generator for S³.
 *
 * This class generates quasi-random (low discrepancy) sequences on the 3-sphere.
 * It replaces probabilistic methods with a rigorous number-theoretic approach
 * using the Hopf Fibration.
 *
 * It maps a 2D Fibonacci lattice on the base S² sphere to S³ by coupling it 
 * with an irrational rotation on the S¹ fiber (using the Plastic Constant).
 */
class HARTONOMOUS_API SuperFibonacci {
public:
    using Vec4 = Eigen::Vector4d;

    // Mathematical Constants
    static const double PHI; // Golden Ratio (Controls S² distribution)
    static const double PSI; // Plastic Constant (Controls S¹ fiber phase)

    /**
     * @brief Computes the i-th point of an N-point Super Fibonacci spiral.
     * @param i Zero-based index of the point.
     * @param N Total number of points in the sequence.
     * @return Vec4 Normalized vector on S³.
     */
    static Vec4 point_on_s3(size_t i, size_t N);

    /**
     * @brief Generates a full sequence of N points.
     * @param N Total points to generate.
     * @return std::vector<Vec4> The sequence of points.
     */
    static std::vector<Vec4> generate_points(size_t N);

    /**
     * @brief Deterministically maps a 128-bit hash to a point on S³.
     * * Treats the hash as a high-entropy index into the spiral. This guarantees
     * that the same hash always produces the exact same geometric point,
     * unlike the previous probabilistic implementation.
     *
     * @param hash_bytes Pointer to 16 bytes (128 bits) of data.
     * @return Vec4 Normalized vector on S³.
     */
    static Vec4 hash_to_point(const unsigned char* hash_bytes);
};

} // namespace hartonomous::geometry