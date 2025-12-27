#pragma once

#include "hilbert_encoder.hpp"
#include "../types/int128.hpp"
#include <array>
#include <cstdint>

namespace hartonomous {

/// 4D Hilbert curve encoder/decoder for 128-bit precision.
/// 
/// This is a thin wrapper around HilbertEncoder that provides a UInt128
/// interface for compatibility with existing code.
///
/// Properties guaranteed:
/// - Bijective: Every coordinate maps to exactly one index, and vice versa
/// - Deterministic: Same input always produces same output
/// - Locality-preserving: Nearby indices correspond to nearby coordinates
class HilbertCurve4D {
public:
    static constexpr int DIMENSIONS = 4;
    static constexpr int BITS_PER_DIM = 32;
    static constexpr int TOTAL_BITS = DIMENSIONS * BITS_PER_DIM; // 128

    /// Convert 4D coordinates to Hilbert index
    /// Each coordinate is 32-bit, result is 128-bit index
    [[nodiscard]] static constexpr UInt128 coords_to_index(
        std::uint32_t x, std::uint32_t y, std::uint32_t z, std::uint32_t w) noexcept 
    {
        auto id = HilbertEncoder::encode(x, y, z, w);
        auto [h, l] = id.to_unsigned();
        return UInt128{h, l};
    }

    /// Convert Hilbert index back to 4D coordinates
    [[nodiscard]] static constexpr std::array<std::uint32_t, 4> index_to_coords(
        UInt128 index) noexcept 
    {
        auto id = AtomId::from_unsigned(index.high, index.low);
        return HilbertEncoder::decode(id);
    }

    /// Convenience overload taking coordinate array
    [[nodiscard]] static constexpr UInt128 coords_to_index(
        const std::array<std::uint32_t, 4>& coords) noexcept {
        return coords_to_index(coords[0], coords[1], coords[2], coords[3]);
    }
};

} // namespace hartonomous
