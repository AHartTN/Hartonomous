#pragma once

#include <cstdint>
#include <compare>
#include <utility>

namespace hartonomous {

/// A 128-bit compound ID split into two 64-bit longs.
/// This is the fundamental identifier for every atom in the system.
///
/// Properties:
/// - Deterministic: Same codepoint → same ID, always
/// - Lossless: ID → codepoint is reversible
/// - Ordered: Lexicographic comparison on (high, low) preserves semantic locality
/// - Database-friendly: Two int64 columns, sortable, indexable
struct AtomId {
    std::int64_t high;  // Upper 64 bits of Hilbert index
    std::int64_t low;   // Lower 64 bits of Hilbert index

    constexpr AtomId() noexcept : high(0), low(0) {}
    constexpr AtomId(std::int64_t h, std::int64_t l) noexcept : high(h), low(l) {}

    /// Lexicographic comparison (preserves Hilbert order)
    constexpr auto operator<=>(const AtomId& other) const noexcept {
        if (high != other.high) return high <=> other.high;
        return low <=> other.low;
    }

    constexpr bool operator==(const AtomId& other) const noexcept {
        return high == other.high && low == other.low;
    }

    /// Check if this ID falls within a range (inclusive)
    /// Useful for semantic range queries
    constexpr bool in_range(const AtomId& min, const AtomId& max) const noexcept {
        return *this >= min && *this <= max;
    }

    /// Compute gap between two IDs (for gap detection)
    /// Returns approximate distance - exact would need 128-bit math
    constexpr std::uint64_t gap_to(const AtomId& other) const noexcept {
        if (high == other.high) {
            return static_cast<std::uint64_t>(
                low > other.low ? low - other.low : other.low - low
            );
        }
        // Cross high boundary - return max to indicate large gap
        return UINT64_MAX;
    }

    /// Convert to unsigned representation
    constexpr std::pair<std::uint64_t, std::uint64_t> to_unsigned() const noexcept {
        return {static_cast<std::uint64_t>(high), static_cast<std::uint64_t>(low)};
    }

    /// Convert from unsigned representation
    static constexpr AtomId from_unsigned(std::uint64_t h, std::uint64_t l) noexcept {
        return AtomId{static_cast<std::int64_t>(h), static_cast<std::int64_t>(l)};
    }
};

} // namespace hartonomous
