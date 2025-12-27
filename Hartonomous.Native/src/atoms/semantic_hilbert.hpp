#pragma once

#include "atom_id.hpp"
#include "semantic_coord.hpp"
#include "../hilbert/hilbert_encoder.hpp"
#include <cstdint>

namespace hartonomous {

/// Semantic coordinate to Hilbert ID conversion.
/// Maps the 4D semantic space to a 128-bit Hilbert index.
class SemanticHilbert {
public:
    /// Convert semantic coordinates to Hilbert AtomId.
    /// This is the primary entry point for codepoint → AtomId conversion.
    [[nodiscard]] static constexpr AtomId from_semantic(SemanticCoord coord) noexcept {
        // Map semantic dimensions to 32-bit coordinates
        // Each dimension uses part of its 32-bit range based on semantic precision

        // X: Page (3 bits) scaled to use lower portion for grouping
        //    Page 0 uses range 0x00000000 - 0x1FFFFFFF
        //    Page 7 uses range 0xE0000000 - 0xFFFFFFFF
        std::uint32_t x = static_cast<std::uint32_t>(coord.page) << 29;

        // Y: Type (3 bits) similarly distributed
        std::uint32_t y = static_cast<std::uint32_t>(coord.type) << 29;

        // Z: Base character (21 bits) - use the codepoint value directly
        //    Scaled to 32 bits for even distribution
        std::uint32_t z = static_cast<std::uint32_t>(coord.base) << 11;

        // W: Variant (5 bits) - spread across 32 bits
        std::uint32_t w = static_cast<std::uint32_t>(coord.variant) << 27;

        return HilbertEncoder::encode(x, y, z, w);
    }

    /// Convert codepoint directly to Hilbert AtomId.
    /// Computes semantic coordinates internally.
    /// NOTE: Include semantic_decompose.hpp to use this function.
    [[nodiscard]] static constexpr AtomId from_codepoint(std::int32_t codepoint) noexcept;

    /// Decode AtomId back to approximate semantic coordinates.
    /// Note: Some precision may be lost in variant due to bit distribution.
    [[nodiscard]] static constexpr SemanticCoord to_semantic(AtomId id) noexcept {
        auto coords = HilbertEncoder::decode(id);

        SemanticCoord result;
        result.page = static_cast<std::uint8_t>(coords[0] >> 29);
        result.type = static_cast<std::uint8_t>(coords[1] >> 29);
        result.base = static_cast<std::int32_t>(coords[2] >> 11);
        result.variant = static_cast<std::uint8_t>(coords[3] >> 27);

        return result;
    }
};

} // namespace hartonomous
