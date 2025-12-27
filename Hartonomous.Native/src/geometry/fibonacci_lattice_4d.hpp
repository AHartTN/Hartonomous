#pragma once

#include <cstdint>
#include <array>

namespace hartonomous {

/// 4D Fibonacci Lattice for distributing points in a unit hypercube [0,1]⁴
/// Uses generalized golden ratio for 4 dimensions.
///
/// The 4D generalization uses the roots of x⁵ = x + 1 (the "4D plastic constant")
/// This ensures optimal low-discrepancy distribution in 4D space.
class FibonacciLattice4D {
public:
    // 4D irrationals based on generalized golden ratio
    // These are algebraic numbers from x⁵ = x + 1
    static constexpr double ALPHA_4D_1 = 0.7548776662466927600;  // ≈ 1/ρ where ρ is 4D plastic
    static constexpr double ALPHA_4D_2 = 0.5698402909980532659;  // ≈ 1/ρ²
    static constexpr double ALPHA_4D_3 = 0.4302597090019467341;  // ≈ 1/ρ³
    static constexpr double ALPHA_4D_4 = 0.3246474729273224660;  // ≈ 1/ρ⁴

    /// Generate a point in the unit hypercube [0,1]⁴ from an index
    [[nodiscard]] static constexpr std::array<double, 4>
    point_from_index(std::uint64_t index) noexcept {
        double x = frac(static_cast<double>(index) * ALPHA_4D_1);
        double y = frac(static_cast<double>(index) * ALPHA_4D_2);
        double z = frac(static_cast<double>(index) * ALPHA_4D_3);
        double w = frac(static_cast<double>(index) * ALPHA_4D_4);
        return {x, y, z, w};
    }

    /// Generate a point scaled to [0, max_coord]⁴ with uint32 precision
    [[nodiscard]] static constexpr std::array<std::uint32_t, 4>
    point_from_index_u32(std::uint64_t index) noexcept {
        auto [x, y, z, w] = point_from_index(index);
        return {
            static_cast<std::uint32_t>(x * static_cast<double>(UINT32_MAX)),
            static_cast<std::uint32_t>(y * static_cast<double>(UINT32_MAX)),
            static_cast<std::uint32_t>(z * static_cast<double>(UINT32_MAX)),
            static_cast<std::uint32_t>(w * static_cast<double>(UINT32_MAX))
        };
    }

    /// Generate a point scaled to [-max_coord, +max_coord]⁴ with int32 precision.
    /// CENTER-ORIGIN: (0,0,0,0) is at the center, coordinates range from -INT32_MAX to +INT32_MAX.
    [[nodiscard]] static constexpr std::array<std::int32_t, 4>
    point_from_index_signed(std::uint64_t index) noexcept {
        auto [x, y, z, w] = point_from_index(index);
        // Map [0,1] to [-INT32_MAX, +INT32_MAX]
        // 0.0 -> -INT32_MAX, 0.5 -> 0, 1.0 -> +INT32_MAX
        constexpr double scale = static_cast<double>(INT32_MAX) * 2.0;
        constexpr double offset = static_cast<double>(INT32_MAX);
        return {
            static_cast<std::int32_t>(x * scale - offset),
            static_cast<std::int32_t>(y * scale - offset),
            static_cast<std::int32_t>(z * scale - offset),
            static_cast<std::int32_t>(w * scale - offset)
        };
    }

    /// Euclidean distance squared between two 4D lattice points
    [[nodiscard]] static constexpr double
    distance_squared(std::uint64_t idx1, std::uint64_t idx2) noexcept {
        auto [x1, y1, z1, w1] = point_from_index(idx1);
        auto [x2, y2, z2, w2] = point_from_index(idx2);

        // Toroidal distance in each dimension
        double dx = wrap_distance(x1, x2);
        double dy = wrap_distance(y1, y2);
        double dz = wrap_distance(z1, z2);
        double dw = wrap_distance(w1, w2);

        return dx*dx + dy*dy + dz*dz + dw*dw;
    }

private:
    [[nodiscard]] static constexpr double frac(double x) noexcept {
        double result = x - static_cast<std::int64_t>(x);
        return result < 0.0 ? result + 1.0 : result;
    }

    [[nodiscard]] static constexpr double wrap_distance(double a, double b) noexcept {
        double d = a - b;
        if (d < 0) d = -d;
        if (d > 0.5) d = 1.0 - d;
        return d;
    }
};

} // namespace hartonomous
