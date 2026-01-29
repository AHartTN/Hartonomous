#pragma once

#include <Eigen/Core>
#include <cstdint>
#include <random>
#include <array>
#include <cmath>
#include <cstring> // For std::memcpy
#include "xoshiro.hpp"
#include <vector>

namespace hartonomous::geometry {

/**
 * @brief Maps a 128-bit hash to a uniform point on the 3-sphere (S³).
 *
 * This utility class uses the Gaussian vector normalization method, which is a
 * standard and mathematically robust technique for generating points with a
 * uniform distribution on the surface of an N-dimensional sphere.
 *
 * It uses the high-performance xoshiro256++ PRNG for fast, high-quality
 * random number generation, seeded deterministically from the input hash.
 */
class SuperFibonacci {
public:
    using Vec4 = Eigen::Vector4d;

    // Mathematical Constants
    static const double PHI; // Golden Ratio
    static const double PSI; // Plastic Constant

    /**
     * @brief Generates the i-th point out of N points on the S³ surface
     *        using a Super Fibonacci spiral distribution.
     * @param i The index of the point to generate (0 to N-1).
     * @param N The total number of points.
     * @return Vec4 The 4D vector representing the point on S³.
     */
    static Vec4 point_on_s3(size_t i, size_t N);

    /**
     * @brief Generates N points on the S³ surface using a Super Fibonacci
     *        spiral distribution.
     * @param N The total number of points to generate.
     * @return std::vector<Vec4> A vector of 4D points on S³.
     */
    static std::vector<Vec4> generate_points(size_t N);

    /**
     * @brief Maps a 128-bit hash to a uniform point on the S³ hypersphere.
     *
     * @param hash_bytes A pointer to an array of 16 bytes (128 bits)
     *                   representing the content hash.
     * @return Vec4 A 4D vector representing a point on the unit hypersphere.
     *              The vector is guaranteed to be normalized.
     */
    static Vec4 hash_to_point(const uint8_t* hash_bytes) {
        // 1. Extract two 64-bit integers from the 128-bit hash to seed the PRNG.
        uint64_t seed_hi, seed_lo;
        std::memcpy(&seed_hi, hash_bytes, sizeof(uint64_t));
        std::memcpy(&seed_lo, hash_bytes + sizeof(uint64_t), sizeof(uint64_t));

        // 2. Instantiate the fast xoshiro256++ PRNG with the seed.
        prng::xoshiro256pp rng(seed_hi, seed_lo);

        // 3. Define a standard normal distribution (mean=0.0, stddev=1.0).
        std::normal_distribution<double> dist(0.0, 1.0);

        // 4. Generate four random variates to form a 4D vector.
        Vec4 point(
            dist(rng),
            dist(rng),
            dist(rng),
            dist(rng)
        );

        // 5. Normalize the vector to project it onto the S³ sphere.
        // We handle the edge case where the vector could be zero, though the
        // probability is astronomically low for a 64-bit float distribution.
        double norm = point.norm();
        if (norm > 1e-9) { // A small tolerance for floating point safety
            point.normalize();
        } else {
            // In the vanishingly rare case of a zero vector, return a default point.
            return Vec4(1.0, 0.0, 0.0, 0.0);
        }
        
        return point;
    }
};



} // namespace hartonomous::geometry

