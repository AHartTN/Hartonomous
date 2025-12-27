#pragma once

#include "golden_constants.hpp"
#include <cstdint>
#include <array>

namespace hartonomous {

/// 3D Fibonacci Lattice for distributing points in a unit cube [0,1]³
/// with optimal spacing properties.
///
/// The key insight: for N points, point i is placed at:
///   x_i = frac(i × α₁)
///   y_i = frac(i × α₂)
///   z_i = frac(i × α₃)
/// where frac() is the fractional part and α values are based on the golden ratio.
///
/// This creates a quasi-random low-discrepancy sequence where nearby indices
/// produce nearby points (with some wrapping at boundaries).
class FibonacciLattice3D {
public:
    /// Generate a point in the unit cube [0,1]³ from an index
    /// Points with nearby indices will tend to be nearby in space.
    [[nodiscard]] static constexpr std::array<double, 3>
    point_from_index(std::uint64_t index) noexcept {
        // Use the generalized Fibonacci/golden ratio sequence
        double x = frac(static_cast<double>(index) * golden::ALPHA_1);
        double y = frac(static_cast<double>(index) * golden::ALPHA_2);
        double z = frac(static_cast<double>(index) * golden::ALPHA_3);
        return {x, y, z};
    }

    /// Generate a point scaled to [0, max_coord]³ with uint32 precision
    [[nodiscard]] static constexpr std::array<std::uint32_t, 3>
    point_from_index_u32(std::uint64_t index) noexcept {
        auto [x, y, z] = point_from_index(index);
        return {
            static_cast<std::uint32_t>(x * static_cast<double>(UINT32_MAX)),
            static_cast<std::uint32_t>(y * static_cast<double>(UINT32_MAX)),
            static_cast<std::uint32_t>(z * static_cast<double>(UINT32_MAX))
        };
    }

    /// Generate a point scaled to [-max_coord, +max_coord]³ with int32 precision.
    /// CENTER-ORIGIN: (0,0,0) is at the center, coordinates range from -INT32_MAX to +INT32_MAX.
    [[nodiscard]] static constexpr std::array<std::int32_t, 3>
    point_from_index_signed(std::uint64_t index) noexcept {
        auto [x, y, z] = point_from_index(index);
        // Map [0,1] to [-INT32_MAX, +INT32_MAX]
        // 0.0 -> -INT32_MAX, 0.5 -> 0, 1.0 -> +INT32_MAX
        constexpr double scale = static_cast<double>(INT32_MAX) * 2.0;
        constexpr double offset = static_cast<double>(INT32_MAX);
        return {
            static_cast<std::int32_t>(x * scale - offset),
            static_cast<std::int32_t>(y * scale - offset),
            static_cast<std::int32_t>(z * scale - offset)
        };
    }

    /// Compute approximate index from a point (inverse mapping)
    /// This is approximate because the lattice is quasi-random
    [[nodiscard]] static constexpr std::uint64_t
    approximate_index_from_point(double px, double /*py*/, double /*pz*/) noexcept {
        // Use the x coordinate as primary (most significant for index recovery)
        // This gives us the approximate index modulo precision limits
        double idx_estimate = px / golden::ALPHA_1;
        return static_cast<std::uint64_t>(idx_estimate + 0.5);
    }

    /// Euclidean distance squared between two lattice points
    [[nodiscard]] static constexpr double
    distance_squared(std::uint64_t idx1, std::uint64_t idx2) noexcept {
        auto [x1, y1, z1] = point_from_index(idx1);
        auto [x2, y2, z2] = point_from_index(idx2);

        // Handle wrap-around: use toroidal distance
        double dx = wrap_distance(x1, x2);
        double dy = wrap_distance(y1, y2);
        double dz = wrap_distance(z1, z2);

        return dx*dx + dy*dy + dz*dz;
    }

    /// Distance in coordinate space (non-toroidal)
    [[nodiscard]] static constexpr double
    euclidean_distance_squared(std::uint64_t idx1, std::uint64_t idx2) noexcept {
        auto [x1, y1, z1] = point_from_index(idx1);
        auto [x2, y2, z2] = point_from_index(idx2);

        double dx = x1 - x2;
        double dy = y1 - y2;
        double dz = z1 - z2;

        return dx*dx + dy*dy + dz*dz;
    }

private:
    /// Fractional part of a number (always positive)
    [[nodiscard]] static constexpr double frac(double x) noexcept {
        double result = x - static_cast<std::int64_t>(x);
        return result < 0.0 ? result + 1.0 : result;
    }

    /// Distance on a torus (wrapping at 0 and 1)
    [[nodiscard]] static constexpr double wrap_distance(double a, double b) noexcept {
        double d = a - b;
        if (d < 0) d = -d;
        if (d > 0.5) d = 1.0 - d;
        return d;
    }
};

} // namespace hartonomous
