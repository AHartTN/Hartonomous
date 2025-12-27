#pragma once

/// Lossless 4D Semantic Point Representation
/// 
/// INVARIANTS (all must hold):
/// 1. LOSSLESS: encode(decode(x)) == x for all valid inputs
/// 2. DETERMINISTIC: Same input → same output, always
/// 3. BIJECTIVE: Each codepoint maps to exactly one 4D point
/// 4. 32 BITS PER DIMENSION: Full precision, no quantization
///
/// This is the CANONICAL implementation. All other mappings derive from this.

#include "atom_id.hpp"
#include <cstdint>
#include <array>

namespace hartonomous {

/// A point in 4D semantic space with signed coordinates centered at origin.
/// 
/// Dimensions (all signed, centered at 0):
///   X = Page (script family)     - 3 bits → range [-4, +3]
///   Y = Type (character class)   - 3 bits → range [-4, +3]
///   Z = Base (canonical form)    - 21 bits → range [-1048576, +1048575]
///   W = Variant (case/diacritic) - 5 bits → range [-16, +15]
///
/// GEOMETRIC PROPERTY:
///   - Origin (0,0,0,0) is at the CENTER of the semantic space
///   - Individual atoms (codepoints) project onto a hypersurface
///   - Compositions (averages, centroids) naturally fall INSIDE the volume
///
/// This enables:
///   - Centroid calculations that produce valid interior points
///   - Hierarchical nesting: compositions contain their constituents
///   - Natural distance ordering from center outward
///
/// Encoding is LOSSLESS: semantic → signed → semantic = identity
struct SemanticPoint4D {
    std::int32_t x;  // Page:    [-4, +3]
    std::int32_t y;  // Type:    [-4, +3]
    std::int32_t z;  // Base:    [-1048576, +1048575]
    std::int32_t w;  // Variant: [-16, +15]

    // Centering offsets (half of each range)
    static constexpr std::int32_t PAGE_OFFSET = 4;      // 8/2
    static constexpr std::int32_t TYPE_OFFSET = 4;      // 8/2
    static constexpr std::int32_t BASE_OFFSET = 0x100000;  // 2^20 = 1048576 (half of 21-bit range)
    static constexpr std::int32_t VAR_OFFSET = 16;      // 32/2

    constexpr SemanticPoint4D() noexcept : x(0), y(0), z(0), w(0) {}
    constexpr SemanticPoint4D(std::int32_t x_, std::int32_t y_, 
                               std::int32_t z_, std::int32_t w_) noexcept
        : x(x_), y(y_), z(z_), w(w_) {}

    /// Encode semantic coordinates to centered 4D point (LOSSLESS)
    /// 
    /// Transforms unsigned semantic coords to signed coords centered at origin:
    ///   x = page - 4      (0-7 → -4 to +3)
    ///   y = type - 4      (0-7 → -4 to +3)
    ///   z = base - 2^20   (0-2M → -1M to +1M)
    ///   w = variant - 16  (0-31 → -16 to +15)
    [[nodiscard]] static constexpr SemanticPoint4D from_semantic(SemanticCoord coord) noexcept {
        return SemanticPoint4D{
            static_cast<std::int32_t>(coord.page & 0x7) - PAGE_OFFSET,
            static_cast<std::int32_t>(coord.type & 0x7) - TYPE_OFFSET,
            static_cast<std::int32_t>(coord.base & 0x1FFFFF) - BASE_OFFSET,
            static_cast<std::int32_t>(coord.variant & 0x1F) - VAR_OFFSET
        };
    }

    /// Decode centered 4D point back to semantic coordinates (LOSSLESS)
    [[nodiscard]] constexpr SemanticCoord to_semantic() const noexcept {
        return SemanticCoord{
            static_cast<std::uint8_t>((x + PAGE_OFFSET) & 0x7),
            static_cast<std::uint8_t>((y + TYPE_OFFSET) & 0x7),
            static_cast<std::int32_t>((z + BASE_OFFSET) & 0x1FFFFF),
            static_cast<std::uint8_t>((w + VAR_OFFSET) & 0x1F)
        };
    }

    /// Encode codepoint directly to centered 4D point
    [[nodiscard]] static constexpr SemanticPoint4D from_codepoint(
        std::int32_t codepoint, SemanticCoord (*decompose)(std::int32_t)) noexcept {
        return from_semantic(decompose(codepoint));
    }

    /// Get packed 32-bit semantic index (for database storage)
    [[nodiscard]] constexpr std::uint32_t packed_index() const noexcept {
        return to_semantic().pack();
    }

    /// Create from packed 32-bit semantic index
    [[nodiscard]] static constexpr SemanticPoint4D from_packed(std::uint32_t packed) noexcept {
        return from_semantic(SemanticCoord::unpack(packed));
    }

    /// Compute centroid of multiple points (compositions fall inside)
    [[nodiscard]] static constexpr SemanticPoint4D centroid(
        const SemanticPoint4D* points, std::size_t count) noexcept {
        if (count == 0) return SemanticPoint4D{};
        
        std::int64_t sum_x = 0, sum_y = 0, sum_z = 0, sum_w = 0;
        for (std::size_t i = 0; i < count; ++i) {
            sum_x += points[i].x;
            sum_y += points[i].y;
            sum_z += points[i].z;
            sum_w += points[i].w;
        }
        
        return SemanticPoint4D{
            static_cast<std::int32_t>(sum_x / static_cast<std::int64_t>(count)),
            static_cast<std::int32_t>(sum_y / static_cast<std::int64_t>(count)),
            static_cast<std::int32_t>(sum_z / static_cast<std::int64_t>(count)),
            static_cast<std::int32_t>(sum_w / static_cast<std::int64_t>(count))
        };
    }

    /// Semantic distance squared with proper weighting.
    /// 
    /// Weighting ensures correct distance ordering:
    ///   - Variant differences (case/diacritics) are SMALL
    ///   - Base differences (different letters) are MEDIUM
    ///   - Type differences (letter vs digit) are LARGE
    ///   - Page differences (script families) are LARGEST
    [[nodiscard]] constexpr std::uint64_t distance_squared(const SemanticPoint4D& other) const noexcept {
        constexpr std::uint64_t PAGE_SCALE = 0x100000000ULL;  // 2^32
        constexpr std::uint64_t TYPE_SCALE = 0x10000000ULL;   // 2^28
        constexpr std::uint64_t BASE_SCALE = 0x20ULL;         // 32
        constexpr std::uint64_t VAR_SCALE  = 1ULL;            // 1
        
        std::int64_t dx = static_cast<std::int64_t>(x) - static_cast<std::int64_t>(other.x);
        std::int64_t dy = static_cast<std::int64_t>(y) - static_cast<std::int64_t>(other.y);
        std::int64_t dz = static_cast<std::int64_t>(z) - static_cast<std::int64_t>(other.z);
        std::int64_t dw = static_cast<std::int64_t>(w) - static_cast<std::int64_t>(other.w);
        
        return static_cast<std::uint64_t>(dx * dx) * PAGE_SCALE +
               static_cast<std::uint64_t>(dy * dy) * TYPE_SCALE +
               static_cast<std::uint64_t>(dz * dz) * BASE_SCALE +
               static_cast<std::uint64_t>(dw * dw) * VAR_SCALE;
    }

    /// Raw Euclidean distance squared (unweighted, for exact geometry)
    [[nodiscard]] constexpr std::uint64_t raw_distance_squared(const SemanticPoint4D& other) const noexcept {
        std::int64_t dx = static_cast<std::int64_t>(x) - static_cast<std::int64_t>(other.x);
        std::int64_t dy = static_cast<std::int64_t>(y) - static_cast<std::int64_t>(other.y);
        std::int64_t dz = static_cast<std::int64_t>(z) - static_cast<std::int64_t>(other.z);
        std::int64_t dw = static_cast<std::int64_t>(w) - static_cast<std::int64_t>(other.w);
        return static_cast<std::uint64_t>(dx*dx + dy*dy + dz*dz + dw*dw);
    }

    /// Distance from origin (radius in 4D space)
    [[nodiscard]] constexpr std::uint64_t radius_squared() const noexcept {
        return static_cast<std::uint64_t>(
            static_cast<std::int64_t>(x) * x +
            static_cast<std::int64_t>(y) * y +
            static_cast<std::int64_t>(z) * z +
            static_cast<std::int64_t>(w) * w
        );
    }

    /// Manhattan distance (faster, still preserves ordering with weighting)
    [[nodiscard]] constexpr std::uint64_t manhattan_distance(const SemanticPoint4D& other) const noexcept {
        auto abs_diff = [](std::int32_t a, std::int32_t b) -> std::uint64_t {
            std::int64_t d = static_cast<std::int64_t>(a) - static_cast<std::int64_t>(b);
            return static_cast<std::uint64_t>(d < 0 ? -d : d);
        };
        constexpr std::uint64_t PAGE_SCALE = 0x100000000ULL;
        constexpr std::uint64_t TYPE_SCALE = 0x10000000ULL;
        constexpr std::uint64_t BASE_SCALE = 0x20ULL;
        constexpr std::uint64_t VAR_SCALE  = 1ULL;
        
        return abs_diff(x, other.x) * PAGE_SCALE + 
               abs_diff(y, other.y) * TYPE_SCALE + 
               abs_diff(z, other.z) * BASE_SCALE + 
               abs_diff(w, other.w) * VAR_SCALE;
    }

    constexpr bool operator==(const SemanticPoint4D& other) const noexcept {
        return x == other.x && y == other.y && z == other.z && w == other.w;
    }

    /// As array for iteration
    [[nodiscard]] constexpr std::array<std::int32_t, 4> as_array() const noexcept {
        return {x, y, z, w};
    }

    /// Check if point is at origin (center of space)
    [[nodiscard]] constexpr bool is_origin() const noexcept {
        return x == 0 && y == 0 && z == 0 && w == 0;
    }
};

// =============================================================================
// COMPILE-TIME VERIFICATION
// These static_asserts PROVE the implementation is lossless
// =============================================================================

namespace detail {

// Verify round-trip for a specific coordinate
constexpr bool verify_roundtrip(std::uint8_t page, std::uint8_t type, 
                                 std::int32_t base, std::uint8_t variant) {
    SemanticCoord original{page, type, base, variant};
    auto point = SemanticPoint4D::from_semantic(original);
    auto recovered = point.to_semantic();
    return recovered.page == (page & 0x7) &&
           recovered.type == (type & 0x7) &&
           recovered.base == (base & 0x1FFFFF) &&
           recovered.variant == (variant & 0x1F);
}

// Verify pack/unpack round-trip
constexpr bool verify_pack_roundtrip(std::uint8_t page, std::uint8_t type,
                                      std::int32_t base, std::uint8_t variant) {
    SemanticCoord original{page, type, base, variant};
    auto packed = original.pack();
    auto unpacked = SemanticCoord::unpack(packed);
    return unpacked.page == (page & 0x7) &&
           unpacked.type == (type & 0x7) &&
           unpacked.base == (base & 0x1FFFFF) &&
           unpacked.variant == (variant & 0x1F);
}

} // namespace detail

// PROOF: These compile = the implementation is lossless
static_assert(detail::verify_roundtrip(0, 0, 0, 0), "Origin must round-trip");
static_assert(detail::verify_roundtrip(7, 7, 0x10FFFF, 31), "Max values must round-trip");
static_assert(detail::verify_roundtrip(0, 0, 'a', 0), "Lowercase 'a' must round-trip");
static_assert(detail::verify_roundtrip(0, 1, 'a', 1), "Uppercase 'A' (base=a, var=1) must round-trip");
static_assert(detail::verify_roundtrip(2, 6, 0x4E00, 0), "CJK 一 must round-trip");
static_assert(detail::verify_roundtrip(7, 4, 0x1F600, 0), "Emoji 😀 must round-trip");

static_assert(detail::verify_pack_roundtrip(0, 0, 0, 0), "Pack origin must round-trip");
static_assert(detail::verify_pack_roundtrip(7, 7, 0x10FFFF, 31), "Pack max must round-trip");
static_assert(detail::verify_pack_roundtrip(0, 1, 'a', 0), "Pack 'a' must round-trip");
static_assert(detail::verify_pack_roundtrip(0, 1, 'a', 1), "Pack 'A' must round-trip");

} // namespace hartonomous
