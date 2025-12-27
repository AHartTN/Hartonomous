#include "tesseract_surface.hpp"
#include <limits>

namespace hartonomous {

// Compile-time verification of mapping properties
// CENTER-ORIGIN: coordinates are SIGNED, origin at (0,0,0,0)
// Surface faces are at ±TESSERACT_BOUNDARY (INT32_MAX)

// Origin codepoint maps to a valid surface point
static_assert([] {
    auto point = TesseractSurface::map_codepoint(0);
    // Should be on XNeg face (first Latin character), X = -INT32_MAX
    return point.face == TesseractFace::XNeg && point.x == -TESSERACT_BOUNDARY;
}(), "Codepoint 0 mapping failed");

// 'A' (U+0041) should map near other Basic Latin
static_assert([] {
    auto point_A = TesseractSurface::map_codepoint(0x0041);
    auto point_B = TesseractSurface::map_codepoint(0x0042);
    // Both should be on same face (XNeg = Latin face)
    return point_A.face == point_B.face && point_A.face == TesseractFace::XNeg;
}(), "Basic Latin clustering failed");

// CJK ideograph should be on YNeg face
static_assert([] {
    auto point = TesseractSurface::map_codepoint(0x4E00); // First CJK ideograph
    return point.face == TesseractFace::YNeg;
}(), "CJK face assignment failed");

// Emoji (supplementary plane) should be on WPos face
static_assert([] {
    auto point = TesseractSurface::map_codepoint(0x1F600); // Emoji
    return point.face == TesseractFace::WPos;
}(), "Emoji face assignment failed");

// Round-trip through Hilbert index
static_assert([] {
    auto idx_A = TesseractSurface::codepoint_to_hilbert(0x0041);
    auto idx_B = TesseractSurface::codepoint_to_hilbert(0x0042);
    // Different codepoints must have different indices
    return idx_A != idx_B;
}(), "Hilbert uniqueness failed");

} // namespace hartonomous
