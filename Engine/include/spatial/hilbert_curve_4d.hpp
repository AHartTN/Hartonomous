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
     * @brief Represents a 128-bit Hilbert index as 16-byte array (same format as UUID).
     * Stored as UUID in PostgreSQL for optimal performance on 16-byte fixed values.
     */
    using HilbertIndex = std::array<uint8_t, 16>;



    /**
     * @brief Entity type for parity-based ID partitioning.
     */
    enum class EntityType {
        Composition = 0, // Even IDs
        Atom = 1         // Odd IDs
    };

    /**
     * @brief Encodes a 4D point into a 128-bit Hilbert index.
     *
     * @param coords A 4D vector with each component normalized to the range [0.0, 1.0].
     * @param type The type of entity being encoded (Atom or Composition).
     * @return HilbertIndex The resulting 128-bit Hilbert index with enforced parity.
     */
    static HilbertIndex encode(const Vec4& coords, EntityType type = EntityType::Composition) {
        // 1. Discretize the floating-point coordinates into a 4-element array of 32-bit integers.
        constexpr uint64_t max_val = (1ULL << BITS_PER_DIMENSION) - 1;
        std::array<uint32_t, 4> discrete_coords;
        for(int i = 0; i < 4; ++i) {
            double v = std::clamp(coords[i], 0.0, 1.0);
            discrete_coords[i] = static_cast<uint32_t>(v * max_val);
        }

        // 2. Call the PositionToIndex function from the hilbert.hpp library.
        //    The library returns the untransposed index bits in an array of four uint32.
        std::array<uint32_t, 4> index_segments = hilbert::v2::PositionToIndex<uint32_t, 4>(discrete_coords);

        // 3. Pack the segments into a single 128-bit integer.
        unsigned __int128 index_val = 0;
        index_val |= static_cast<unsigned __int128>(index_segments[0]) << 96;
        index_val |= static_cast<unsigned __int128>(index_segments[1]) << 64;
        index_val |= static_cast<unsigned __int128>(index_segments[2]) << 32;
        index_val |= static_cast<unsigned __int128>(index_segments[3]);

        // 4. Enforce Parity Rule: Even = Composition, Odd = Atom.
        if (type == EntityType::Atom) {
            index_val |= 1; // Force Odd
        } else {
            index_val &= ~static_cast<unsigned __int128>(1); // Force Even
        }

        // 5. Convert to 16-byte array (big-endian for consistency)
        HilbertIndex result;
        for (int i = 0; i < 16; ++i) {
            result[15 - i] = static_cast<uint8_t>(index_val >> (i * 8));
        }
        return result;
    }

    /**
     * @brief Calculates the absolute distance between two curve indices.
     * @return HilbertIndex A 128-bit value representing the distance.
     */
    static HilbertIndex curve_distance(const HilbertIndex& a, const HilbertIndex& b) {
        // Convert to __int128 for arithmetic
        unsigned __int128 val_a = 0, val_b = 0;
        for (int i = 0; i < 16; ++i) {
            val_a = (val_a << 8) | a[i];
            val_b = (val_b << 8) | b[i];
        }
        unsigned __int128 diff = (val_a > val_b) ? (val_a - val_b) : (val_b - val_a);
        
        // Convert back to array
        HilbertIndex result;
        for (int i = 0; i < 16; ++i) {
            result[15 - i] = static_cast<uint8_t>(diff >> (i * 8));
        }
        return result;
    }
};

} // namespace hartonomous::spatial

#if defined(__GNUC__) || defined(__clang__)
#pragma GCC diagnostic pop
#endif