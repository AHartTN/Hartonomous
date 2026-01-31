#pragma once

#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic push
#pragma GCC diagnostic ignored "-Wpedantic"
#endif

#include <Eigen/Core>
#include <cstdint>
#include <array>
#include <stdexcept>
#include <algorithm> // For std::clamp

// Correctly include the header from the 'spectral3d/hilbert_hpp' submodule.
#include "hilbert.hpp"

namespace hartonomous::spatial {

/**
 * @brief 4D Hilbert Space-Filling Curve
 *
 * This class maps 4D coordinates to a 128-bit Hilbert curve index.
 * This implementation uses the 'spectral3d/hilbert_hpp' library, which is a
 * high-performance C++ implementation of the algorithm described in the paper
 * "Programming the Hilbert Curve" by John Skilling.
 *
 * The v2 implementation (template metaprogramming) is used for maximum
 * performance.
 */
class HilbertCurve4D {
public:
    using Vec4 = Eigen::Vector4d;

    // The number of bits per dimension for the Hilbert curve.
    // 32 bits per dimension * 4 dimensions = 128 bits total index size.
    static constexpr uint32_t BITS_PER_DIMENSION = 32;

    /**
     * @brief Represents a 128-bit Hilbert index.
     * Stored as two 64-bit integers for database compatibility.
     */
    struct HilbertIndex {
        uint64_t hi;
        uint64_t lo;

        // Use the non-standard but widely supported `unsigned __int128` for arithmetic.
        unsigned __int128 to_uint128() const {
            return (static_cast<unsigned __int128>(hi) << 64) | lo;
        }

        std::string to_string() const {
            unsigned __int128 val = to_uint128();
            if (val == 0) return "0";
            std::string s;
            while (val > 0) {
                s += (char)('0' + (val % 10));
                val /= 10;
            }
            std::reverse(s.begin(), s.end());
            return s;
        }

        // Comparison operators
        bool operator==(const HilbertIndex& other) const {
            return hi == other.hi && lo == other.lo;
        }
        bool operator!=(const HilbertIndex& other) const {
            return !(*this == other);
        }
        bool operator>(const HilbertIndex& other) const {
            return to_uint128() > other.to_uint128();
        }
        bool operator<(const HilbertIndex& other) const {
            return to_uint128() < other.to_uint128();
        }
        bool operator>=(const HilbertIndex& other) const {
            return to_uint128() >= other.to_uint128();
        }
        bool operator<=(const HilbertIndex& other) const {
            return to_uint128() <= other.to_uint128();
        }
    };

    /**
     * @brief Encodes a 4D point into a 128-bit Hilbert index.
     *
     * @param coords A 4D vector with each component normalized to the range [0.0, 1.0].
     * @return HilbertIndex The resulting 128-bit Hilbert index.
     */
    static HilbertIndex encode(const Vec4& coords) {
        // 1. Discretize the floating-point coordinates into a 4-element array of 32-bit integers.
        constexpr uint64_t max_val = (1ULL << BITS_PER_DIMENSION) - 1;
        std::array<uint32_t, 4> discrete_coords;
        for(int i = 0; i < 4; ++i) {
            double v = std::clamp(coords[i], 0.0, 1.0);
            discrete_coords[i] = static_cast<uint32_t>(v * max_val);
        }

        // 2. Call the PositionToIndex function from the hilbert.hpp library.
        //    We use the v2 (template metaprogramming) version for performance.
        //    It returns the Hilbert index in a "transposed" format, as an array of four 32-bit integers.
        std::array<uint32_t, 4> transposed_index = hilbert::v2::PositionToIndex<uint32_t, 4>(discrete_coords);

        // 3. Pack the transposed array into a single 128-bit integer.
        //    The library documentation states the index is lexographically sorted
        //    with the most significant objects first.
        unsigned __int128 index_val = 0;
        index_val |= static_cast<unsigned __int128>(transposed_index[0]) << 96;
        index_val |= static_cast<unsigned __int128>(transposed_index[1]) << 64;
        index_val |= static_cast<unsigned __int128>(transposed_index[2]) << 32;
        index_val |= static_cast<unsigned __int128>(transposed_index[3]);

        // 4. Split the 128-bit index into two 64-bit parts for storage.
        return {
            static_cast<uint64_t>(index_val >> 64),
            static_cast<uint64_t>(index_val)
        };
    }

    /**
     * @brief Calculates the absolute distance between two curve indices.
     * @return HilbertIndex A 128-bit value representing the distance.
     */
    static HilbertIndex curve_distance(HilbertIndex a, HilbertIndex b) {
        unsigned __int128 val_a = a.to_uint128();
        unsigned __int128 val_b = b.to_uint128();
        unsigned __int128 diff = (val_a > val_b) ? (val_a - val_b) : (val_b - val_a);

        return {
            static_cast<uint64_t>(diff >> 64),
            static_cast<uint64_t>(diff)
        };
    }
};

} // namespace hartonomous::spatial

#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic pop
#endif