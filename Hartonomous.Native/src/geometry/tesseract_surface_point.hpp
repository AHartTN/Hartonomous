#pragma once

#include "tesseract_face.hpp"
#include <cstdint>
#include <tuple>
#include <cmath>

namespace hartonomous {

/// A point on the exterior surface of a 4D hypercube (tesseract).
/// ORIGIN AT (0,0,0,0). Coordinates are SIGNED.
/// Surface faces are at ±TESSERACT_BOUNDARY.
/// A point lies on the surface when exactly one coordinate is at ±BOUNDARY.
struct alignas(16) TesseractSurfacePoint {
    std::int32_t x;  // SIGNED: range [-BOUNDARY, +BOUNDARY]
    std::int32_t y;
    std::int32_t z;
    std::int32_t w;
    TesseractFace face;

    constexpr TesseractSurfacePoint() noexcept 
        : x(-TESSERACT_BOUNDARY), y(0), z(0), w(0), face(TesseractFace::XNeg) {}

    constexpr TesseractSurfacePoint(std::int32_t x_, std::int32_t y_, 
                                     std::int32_t z_, std::int32_t w_,
                                     TesseractFace face_) noexcept
        : x(x_), y(y_), z(z_), w(w_), face(face_) {}

    /// Create a surface point with the appropriate boundary constraint enforced.
    /// a, b, c are the 3 free coordinates (range [-BOUNDARY, +BOUNDARY]).
    static constexpr TesseractSurfacePoint create_on_face(
        TesseractFace face,
        std::int32_t a, std::int32_t b, std::int32_t c) noexcept 
    {
        auto [dim, is_positive] = get_boundary_info(face);
        std::int32_t boundary = is_positive ? TESSERACT_BOUNDARY : -TESSERACT_BOUNDARY;

        switch (dim) {
            case 0: return {boundary, a, b, c, face}; // X fixed at boundary
            case 1: return {a, boundary, b, c, face}; // Y fixed at boundary
            case 2: return {a, b, boundary, c, face}; // Z fixed at boundary
            case 3: return {a, b, c, boundary, face}; // W fixed at boundary
            default: return {};
        }
    }

    /// Get the 3 varying coordinates (the cell coordinates)
    constexpr std::tuple<std::int32_t, std::int32_t, std::int32_t> 
    get_cell_coords() const noexcept {
        auto [dim, _] = get_boundary_info(face);
        switch (dim) {
            case 0: return {y, z, w}; // X fixed
            case 1: return {x, z, w}; // Y fixed
            case 2: return {x, y, w}; // Z fixed
            case 3: return {x, y, z}; // W fixed
            default: return {0, 0, 0};
        }
    }

    /// Distance from origin (should be >= BOUNDARY for valid surface points)
    constexpr double distance_from_origin() const noexcept {
        double dx = static_cast<double>(x);
        double dy = static_cast<double>(y);
        double dz = static_cast<double>(z);
        double dw = static_cast<double>(w);
        return std::sqrt(dx*dx + dy*dy + dz*dz + dw*dw);
    }

    constexpr bool operator==(const TesseractSurfacePoint& other) const noexcept {
        return x == other.x && y == other.y && z == other.z && w == other.w && face == other.face;
    }
};

} // namespace hartonomous
