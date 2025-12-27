#pragma once

#include "../geometry/tesseract_surface.hpp"
#include "../types/int128.hpp"
#include "unicode_blocks.hpp"
#include <cstdint>
#include <string_view>

namespace hartonomous {

/// Result of mapping a codepoint to the tesseract surface
struct CodepointMapping {
    std::int32_t codepoint;
    TesseractSurfacePoint surface_point;
    UInt128 hilbert_index;
    std::string_view block_name;
    
    constexpr bool is_valid() const noexcept {
        return codepoint >= 0 && codepoint <= TesseractSurface::MAX_CODEPOINT;
    }
};

/// Maps Unicode codepoints to tesseract surface coordinates with full metadata.
/// This is the main entry point for the mapping algorithm.
class CodepointMapper {
public:
    static constexpr int MAX_CODEPOINT = TesseractSurface::MAX_CODEPOINT;
    static constexpr int CODEPOINT_COUNT = TesseractSurface::CODEPOINT_COUNT;

    /// Map a single codepoint to its full representation
    [[nodiscard]] static constexpr CodepointMapping map(std::int32_t codepoint) noexcept {
        if (codepoint < 0 || codepoint > MAX_CODEPOINT) {
            return CodepointMapping{-1, {}, {}, "Invalid"};
        }
        
        auto surface_point = TesseractSurface::map_codepoint(codepoint);
        auto hilbert_index = HilbertCurve4D::coords_to_index(
            surface_point.x, surface_point.y, surface_point.z, surface_point.w);
        auto block_name = get_block_name(codepoint);
        
        return CodepointMapping{codepoint, surface_point, hilbert_index, block_name};
    }

    /// Check if a codepoint is a valid Unicode scalar value
    /// (excludes surrogates and out-of-range values)
    [[nodiscard]] static constexpr bool is_valid_scalar(std::int32_t codepoint) noexcept {
        if (codepoint < 0 || codepoint > MAX_CODEPOINT) return false;
        // Surrogates are not valid scalar values
        if (codepoint >= 0xD800 && codepoint <= 0xDFFF) return false;
        return true;
    }

    /// Check if a codepoint is assigned (not reserved/unassigned)
    /// Note: This is a simplified check; full check requires Unicode data
    [[nodiscard]] static constexpr bool is_assigned(std::int32_t codepoint) noexcept {
        return is_valid_scalar(codepoint) && get_block_name(codepoint) != "Unknown";
    }

    /// Get just the Hilbert index for a codepoint (for quick lookups)
    [[nodiscard]] static constexpr UInt128 get_hilbert_index(std::int32_t codepoint) noexcept {
        return TesseractSurface::codepoint_to_hilbert(codepoint);
    }

    /// Get just the surface point for a codepoint
    [[nodiscard]] static constexpr TesseractSurfacePoint get_surface_point(
        std::int32_t codepoint) noexcept {
        return TesseractSurface::map_codepoint(codepoint);
    }
};

} // namespace hartonomous
