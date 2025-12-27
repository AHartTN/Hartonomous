#pragma once

#include <cstdint>
#include <compare>
#include <bit>
#include <array>

namespace hartonomous {

/// Represents a 128-bit unsigned integer as two 64-bit components.
/// Used for Hilbert curve indices and coordinate packing.
/// Designed for deterministic, reproducible computation.
struct alignas(16) UInt128 {
    std::uint64_t high;
    std::uint64_t low;

    constexpr UInt128() noexcept : high(0), low(0) {}
    constexpr UInt128(std::uint64_t h, std::uint64_t l) noexcept : high(h), low(l) {}
    
    /// Construct from a single 64-bit value (zero-extends high)
    constexpr explicit UInt128(std::uint64_t value) noexcept : high(0), low(value) {}

    /// Pack four 32-bit coordinates into 128 bits
    static constexpr UInt128 from_coords(std::uint32_t x, std::uint32_t y, 
                                          std::uint32_t z, std::uint32_t w) noexcept {
        return UInt128{
            (static_cast<std::uint64_t>(x) << 32) | static_cast<std::uint64_t>(y),
            (static_cast<std::uint64_t>(z) << 32) | static_cast<std::uint64_t>(w)
        };
    }

    /// Unpack 128 bits into four 32-bit coordinates
    constexpr std::array<std::uint32_t, 4> to_coords() const noexcept {
        return {
            static_cast<std::uint32_t>(high >> 32),
            static_cast<std::uint32_t>(high & 0xFFFFFFFF),
            static_cast<std::uint32_t>(low >> 32),
            static_cast<std::uint32_t>(low & 0xFFFFFFFF)
        };
    }

    /// Bitwise operations
    constexpr UInt128 operator&(const UInt128& other) const noexcept {
        return UInt128{high & other.high, low & other.low};
    }

    constexpr UInt128 operator|(const UInt128& other) const noexcept {
        return UInt128{high | other.high, low | other.low};
    }

    constexpr UInt128 operator^(const UInt128& other) const noexcept {
        return UInt128{high ^ other.high, low ^ other.low};
    }

    constexpr UInt128 operator~() const noexcept {
        return UInt128{~high, ~low};
    }

    /// Left shift (handles cross-boundary shifts)
    constexpr UInt128 operator<<(int shift) const noexcept {
        if (shift == 0) return *this;
        if (shift >= 128) return UInt128{0, 0};
        if (shift >= 64) {
            return UInt128{low << (shift - 64), 0};
        }
        return UInt128{
            (high << shift) | (low >> (64 - shift)),
            low << shift
        };
    }

    /// Right shift (handles cross-boundary shifts)
    constexpr UInt128 operator>>(int shift) const noexcept {
        if (shift == 0) return *this;
        if (shift >= 128) return UInt128{0, 0};
        if (shift >= 64) {
            return UInt128{0, high >> (shift - 64)};
        }
        return UInt128{
            high >> shift,
            (low >> shift) | (high << (64 - shift))
        };
    }

    /// Comparison
    constexpr auto operator<=>(const UInt128& other) const noexcept {
        if (auto cmp = high <=> other.high; cmp != 0) return cmp;
        return low <=> other.low;
    }

    constexpr bool operator==(const UInt128& other) const noexcept {
        return high == other.high && low == other.low;
    }

    /// Get bit at position (0 = LSB)
    constexpr bool bit(int pos) const noexcept {
        if (pos < 64) {
            return (low >> pos) & 1;
        }
        return (high >> (pos - 64)) & 1;
    }

    /// Set bit at position
    constexpr UInt128 set_bit(int pos, bool value) const noexcept {
        UInt128 result = *this;
        if (pos < 64) {
            if (value) {
                result.low |= (1ULL << pos);
            } else {
                result.low &= ~(1ULL << pos);
            }
        } else {
            int hpos = pos - 64;
            if (value) {
                result.high |= (1ULL << hpos);
            } else {
                result.high &= ~(1ULL << hpos);
            }
        }
        return result;
    }

    /// Check if zero
    constexpr bool is_zero() const noexcept {
        return high == 0 && low == 0;
    }
};

} // namespace hartonomous
