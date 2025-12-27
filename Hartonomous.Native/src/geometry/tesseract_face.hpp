#pragma once

#include <cstdint>
#include <limits>
#include <utility>

namespace hartonomous {

/// Boundary constant for tesseract surface.
/// Origin is at (0,0,0,0). Surface faces are at ±BOUNDARY.
constexpr std::int32_t TESSERACT_BOUNDARY = std::numeric_limits<std::int32_t>::max();

/// Identifies one of the 8 cubic cells (3D boundary faces) of a tesseract.
/// Origin at (0,0,0,0). Each face is at coordinate = ±TESSERACT_BOUNDARY.
enum class TesseractFace : std::uint8_t {
    XNeg = 0,  // X = -BOUNDARY (negative X face)
    XPos = 1,  // X = +BOUNDARY (positive X face)
    YNeg = 2,  // Y = -BOUNDARY
    YPos = 3,  // Y = +BOUNDARY
    ZNeg = 4,  // Z = -BOUNDARY
    ZPos = 5,  // Z = +BOUNDARY
    WNeg = 6,  // W = -BOUNDARY
    WPos = 7   // W = +BOUNDARY
};

/// Returns the fixed dimension (0-3) and whether it's positive boundary
constexpr std::pair<int, bool> get_boundary_info(TesseractFace face) noexcept {
    switch (face) {
        case TesseractFace::XNeg: return {0, false};
        case TesseractFace::XPos: return {0, true};
        case TesseractFace::YNeg: return {1, false};
        case TesseractFace::YPos: return {1, true};
        case TesseractFace::ZNeg: return {2, false};
        case TesseractFace::ZPos: return {2, true};
        case TesseractFace::WNeg: return {3, false};
        case TesseractFace::WPos: return {3, true};
        default: return {0, false};
    }
}

} // namespace hartonomous
