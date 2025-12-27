#pragma once

#include "golden_constants.hpp"
#include <cmath>
#include <numbers>
#include <array>
#include <utility>
#include <cstdint>

namespace hartonomous {

/// Sunflower spiral for 2D distribution on a disk
/// Classic Vogel's model: r_n = c√n, θ_n = n × golden_angle
/// Useful for projecting onto circular regions
class SunflowerSpiral {
public:
    /// Generate point on unit disk from index n out of total N points
    [[nodiscard]] static constexpr std::pair<double, double>
    point_from_index(std::uint64_t n, std::uint64_t total) noexcept {
        // Vogel's model
        double theta = static_cast<double>(n) * golden::ANGLE;
        double r = std::sqrt(static_cast<double>(n) / static_cast<double>(total));

        // Convert to cartesian
        double x = r * cos_approx(theta);
        double y = r * sin_approx(theta);

        return {x, y};
    }

    /// Generate point on unit sphere using Fibonacci lattice
    /// This distributes points nearly uniformly on a sphere
    [[nodiscard]] static constexpr std::array<double, 3>
    sphere_point_from_index(std::uint64_t n, std::uint64_t total) noexcept {
        // z goes from -1 to 1 (poles)
        double z = 1.0 - (2.0 * static_cast<double>(n) + 1.0) / static_cast<double>(total);

        // radius at this z level
        double r = std::sqrt(1.0 - z * z);

        // angle around z-axis using golden angle
        double theta = static_cast<double>(n) * golden::ANGLE;

        double x = r * cos_approx(theta);
        double y = r * sin_approx(theta);

        return {x, y, z};
    }

private:
    /// Fast constexpr cosine approximation using Taylor series
    [[nodiscard]] static constexpr double cos_approx(double x) noexcept {
        // Normalize to [-π, π]
        while (x > std::numbers::pi) x -= 2.0 * std::numbers::pi;
        while (x < -std::numbers::pi) x += 2.0 * std::numbers::pi;

        // Taylor series: cos(x) ≈ 1 - x²/2! + x⁴/4! - x⁶/6! + x⁸/8!
        double x2 = x * x;
        double x4 = x2 * x2;
        double x6 = x4 * x2;
        double x8 = x6 * x2;

        return 1.0
            - x2 / 2.0
            + x4 / 24.0
            - x6 / 720.0
            + x8 / 40320.0;
    }

    /// Fast constexpr sine approximation using Taylor series
    [[nodiscard]] static constexpr double sin_approx(double x) noexcept {
        // Normalize to [-π, π]
        while (x > std::numbers::pi) x -= 2.0 * std::numbers::pi;
        while (x < -std::numbers::pi) x += 2.0 * std::numbers::pi;

        // Taylor series: sin(x) ≈ x - x³/3! + x⁵/5! - x⁷/7! + x⁹/9!
        double x2 = x * x;
        double x3 = x2 * x;
        double x5 = x3 * x2;
        double x7 = x5 * x2;
        double x9 = x7 * x2;

        return x
            - x3 / 6.0
            + x5 / 120.0
            - x7 / 5040.0
            + x9 / 362880.0;
    }
};

} // namespace hartonomous
