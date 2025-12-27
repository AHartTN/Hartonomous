#pragma once

#include <numbers>

namespace hartonomous {

/// Golden ratio and related constants for Fibonacci lattice
namespace golden {
    /// φ = (1 + √5) / 2 ≈ 1.6180339887...
    constexpr double PHI = 1.6180339887498948482;

    /// φ² = φ + 1 ≈ 2.6180339887...
    constexpr double PHI_SQUARED = 2.6180339887498948482;

    /// Golden angle in radians: 2π / φ² ≈ 2.39996322972...
    /// This is the angle that creates optimal packing (sunflower pattern)
    constexpr double ANGLE = 2.0 * std::numbers::pi / PHI_SQUARED;

    /// Golden angle in degrees ≈ 137.5077...°
    constexpr double ANGLE_DEGREES = 360.0 / PHI_SQUARED;

    /// Plastic constant (3D analog of golden ratio) ≈ 1.32471795724...
    /// Root of x³ = x + 1
    constexpr double PLASTIC = 1.32471795724474602596;

    /// 1/φ = φ - 1 ≈ 0.6180339887...
    constexpr double PHI_INVERSE = 0.6180339887498948482;

    /// For 3D Fibonacci lattice: α₁ = 1/φ, α₂ = 1/φ², α₃ = 1/φ³
    constexpr double ALPHA_1 = 0.6180339887498948482;  // 1/φ
    constexpr double ALPHA_2 = 0.3819660112501051518;  // 1/φ²
    constexpr double ALPHA_3 = 0.2360679774997896964;  // 1/φ³
}

} // namespace hartonomous
