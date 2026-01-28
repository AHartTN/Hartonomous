#pragma once

#include <Eigen/Core>
#include <cstdint>
#include <array>
#include <stdexcept>

// Cross-platform count trailing zeros
#ifdef _MSC_VER
    #include <intrin.h>
    inline uint32_t __builtin_ctz(uint32_t x) {
        unsigned long index;
        _BitScanForward(&index, x);
        return static_cast<uint32_t>(index);
    }
#endif

namespace hartonomous::spatial {

/**
 * @brief 4D Hilbert Space-Filling Curve
 *
 * The Hilbert curve is a continuous fractal space-filling curve that maps
 * a 1-dimensional interval to an N-dimensional space while preserving locality.
 *
 * Key Properties:
 *   - Locality preservation: Nearby points in 4D space → nearby curve values
 *   - Surjective: Covers the entire 4D hypercube
 *   - Fractal structure: Self-similar at all scales
 *   - Better locality than Z-order (Morton) curves
 *
 * Use Cases:
 *   - Spatial indexing for nearest-neighbor queries
 *   - Database key generation for multidimensional data
 *   - Cache-efficient data structures
 *
 * IMPORTANT: This implementation provides ONLY the forward mapping
 * (coordinates → curve value). The inverse is intentionally NOT provided
 * as per requirements.
 *
 * Implementation:
 *   Uses the compact Hilbert curve algorithm with bit interleaving and
 *   Gray code transformations for efficient computation.
 */
class HilbertCurve4D {
public:
    using Vec4i = Eigen::Vector4i;
    using Vec4 = Eigen::Vector4d;

    /**
     * @brief Convert 4D discrete coordinates to Hilbert curve index
     *
     * Maps a 4D point with integer coordinates to a single 64-bit curve index.
     * Higher precision = more bits per dimension.
     *
     * @param coords 4D integer coordinates (each in range [0, 2^bits - 1])
     * @param bits Bits per dimension (max 16 for 4D → 64-bit output)
     * @return uint64_t Hilbert curve index
     *
     * @throws std::invalid_argument if bits > 16 or coords out of range
     */
    static uint64_t encode(const Vec4i& coords, uint32_t bits = 16) {
        if (bits > 16) {
            throw std::invalid_argument("Maximum 16 bits per dimension for 4D (64-bit output)");
        }

        uint32_t max_val = (1U << bits) - 1;
        for (int i = 0; i < 4; ++i) {
            if (coords[i] < 0 || coords[i] > static_cast<int>(max_val)) {
                throw std::invalid_argument("Coordinates out of range for specified bits");
            }
        }

        // Use compact Hilbert curve algorithm
        std::array<uint32_t, 4> x = {
            static_cast<uint32_t>(coords[0]),
            static_cast<uint32_t>(coords[1]),
            static_cast<uint32_t>(coords[2]),
            static_cast<uint32_t>(coords[3])
        };

        return encode_compact(x, bits);
    }

    /**
     * @brief Convert 4D continuous coordinates to Hilbert curve index
     *
     * Maps a 4D point with floating-point coordinates in [0, 1]⁴ to a
     * Hilbert curve index.
     *
     * @param coords 4D coordinates (each in range [0, 1])
     * @param bits Discretization bits per dimension (default 16)
     * @return uint64_t Hilbert curve index
     */
    static uint64_t encode(const Vec4& coords, uint32_t bits = 16) {
        // Discretize to integer coordinates
        uint32_t max_val = (1U << bits) - 1;

        Vec4i discrete_coords;
        for (int i = 0; i < 4; ++i) {
            double clamped = std::clamp(coords[i], 0.0, 1.0);
            discrete_coords[i] = static_cast<int>(clamped * max_val);
        }

        return encode(discrete_coords, bits);
    }

    /**
     * @brief Convert a point on S³ (embedded in 4D hypercube) to Hilbert index
     *
     * Takes a point on the 3-sphere (normalized to [-1, 1]⁴) and maps it
     * to a Hilbert curve index by first embedding it in the unit hypercube [0, 1]⁴.
     *
     * @param s3_point Point on S³ (must be normalized: ||p|| = 1)
     * @param bits Discretization bits per dimension (default 16)
     * @return uint64_t Hilbert curve index
     */
    static uint64_t encode_s3_point(const Vec4& s3_point, uint32_t bits = 16) {
        // Map from [-1, 1]⁴ to [0, 1]⁴
        Vec4 normalized;
        for (int i = 0; i < 4; ++i) {
            normalized[i] = (s3_point[i] + 1.0) / 2.0;
        }

        return encode(normalized, bits);
    }

    /**
     * @brief Estimate the Hilbert distance between two curve indices
     *
     * This provides a lower bound on the actual curve distance.
     * Useful for approximate nearest-neighbor queries.
     *
     * @param index1 First Hilbert index
     * @param index2 Second Hilbert index
     * @return uint64_t Approximate curve distance
     */
    static uint64_t curve_distance(uint64_t index1, uint64_t index2) {
        // Simple L1 distance on the curve (lower bound on actual distance)
        return (index1 > index2) ? (index1 - index2) : (index2 - index1);
    }

private:
    /**
     * @brief Compact Hilbert curve encoding algorithm
     *
     * Implements the fast Hilbert curve algorithm using bit manipulations
     * and Gray code transformations.
     *
     * Based on:
     * "Encoding and Decoding the Hilbert Order" by Xian Liu and Günther Schrack
     */
    static uint64_t encode_compact(const std::array<uint32_t, 4>& coords, uint32_t bits) {
        constexpr uint32_t N = 4; // Dimensions
        uint64_t hilbert_index = 0;

        std::array<uint32_t, N> x = coords;

        // Process bit by bit, from most significant to least significant
        for (int32_t i = bits - 1; i >= 0; --i) {
            // Extract the i-th bit from each coordinate
            uint32_t bit_pattern = 0;
            for (uint32_t dim = 0; dim < N; ++dim) {
                if ((x[dim] & (1U << i)) != 0) {
                    bit_pattern |= (1U << dim);
                }
            }

            // Apply Gray code inverse transformation
            uint32_t gray = bit_pattern;
            uint32_t entry = gray;

            // Compute entry point transformation
            if (entry != 0) {
                uint32_t t = __builtin_ctz(entry); // Count trailing zeros
                entry ^= (entry >> (t + 1)) << (t + 1);
            }

            // Rotate and reflect
            rotate_right(x, entry, N);
            x[0] ^= (entry == (N - 1)) ? ((1U << (i + 1)) - 1) : 0;

            // Accumulate Hilbert index
            hilbert_index = (hilbert_index << N) | gray;
        }

        return hilbert_index;
    }

    /**
     * @brief Rotate coordinates right for Hilbert transformation
     */
    static void rotate_right(std::array<uint32_t, 4>& x, uint32_t n, uint32_t dim) {
        if (n == 0) return;

        std::array<uint32_t, 4> temp = x;
        for (uint32_t i = 0; i < dim; ++i) {
            x[i] = temp[(i + n) % dim];
        }
    }

    /**
     * @brief Transform coordinates using Gray code
     */
    static uint32_t gray_code(uint32_t i) {
        return i ^ (i >> 1);
    }

    /**
     * @brief Inverse Gray code transformation
     */
    static uint32_t inverse_gray_code(uint32_t g) {
        uint32_t i = g;
        for (uint32_t shift = 1; shift < 32; shift <<= 1) {
            i ^= (i >> shift);
        }
        return i;
    }
};

/**
 * @brief Hilbert curve utilities and statistics
 */
class HilbertUtils {
public:
    /**
     * @brief Compute the theoretical maximum Hilbert index for given bit depth
     *
     * @param bits Bits per dimension
     * @param dimensions Number of dimensions (4 for our case)
     * @return uint64_t Maximum index value
     */
    static uint64_t max_index(uint32_t bits, uint32_t dimensions = 4) {
        return (1ULL << (bits * dimensions)) - 1;
    }

    /**
     * @brief Estimate the number of curve segments within a hypercube region
     *
     * Useful for query planning and performance estimation.
     *
     * @param region_size Size of the hypercube region (in normalized coordinates)
     * @param bits Discretization bits
     * @return uint64_t Estimated number of curve segments
     */
    static uint64_t segments_in_region(double region_size, uint32_t bits = 16) {
        // Approximate: Hilbert curve has excellent locality, so a hypercube
        // of side length 's' intersects roughly O(s^(n-1)) segments for n dimensions
        uint32_t discrete_size = static_cast<uint32_t>(region_size * ((1U << bits) - 1));
        return static_cast<uint64_t>(std::pow(discrete_size, 3.0)); // n-1 = 3 for 4D
    }
};

} // namespace hartonomous::spatial
