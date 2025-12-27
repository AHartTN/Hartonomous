#pragma once

#include "surface_point.hpp"
#include "fibonacci_lattice.hpp"
#include "../hilbert/hilbert_curve_4d.hpp"
#include "../unicode/semantic_ordering.hpp"
#include <cstdint>
#include <limits>

namespace hartonomous {

/// Maps Unicode codepoints to points on the exterior surface of a tesseract.
/// CENTER-ORIGIN GEOMETRY: Origin at (0,0,0,0), surface at ±TESSERACT_BOUNDARY.
///
/// Design:
/// - The tesseract has 8 cubic faces (cells)
/// - Codepoints are grouped by semantic similarity (script, category, base character)
/// - Within each face, Fibonacci lattice provides locality-preserving distribution
/// - Similar characters (A, a, Ä, ä) cluster together in 4D space
///
/// The mapping is deterministic: same codepoint always maps to same surface point.
class TesseractSurface {
public:
    static constexpr int MAX_CODEPOINT = 0x10FFFF;
    static constexpr int CODEPOINT_COUNT = MAX_CODEPOINT + 1; // 1,114,112 total
    static constexpr int FACES = 8;
    static constexpr int CODEPOINTS_PER_FACE = (CODEPOINT_COUNT + FACES - 1) / FACES; // ~139,264

    /// Map a Unicode codepoint to a surface point on the tesseract.
    /// CENTER-ORIGIN: coordinates are SIGNED, origin at (0,0,0,0).
    /// The mapping preserves semantic locality: similar characters → nearby points.
    [[nodiscard]] static constexpr TesseractSurfacePoint map_codepoint(
        std::int32_t codepoint) noexcept;

    /// Get the Hilbert index for a codepoint (convenience function)
    [[nodiscard]] static constexpr UInt128 codepoint_to_hilbert(
        std::int32_t codepoint) noexcept;

    /// Reverse lookup: find closest codepoint to a Hilbert index
    /// (Not a perfect inverse due to surface constraint, returns nearest)
    [[nodiscard]] static constexpr std::int32_t hilbert_to_nearest_codepoint(
        UInt128 index) noexcept;

    /// Compute semantic distance between two codepoints.
    /// Lower = more similar. Use for clustering analysis.
    [[nodiscard]] static constexpr std::uint64_t semantic_distance(
        std::int32_t cp1, std::int32_t cp2) noexcept {
        return SemanticOrdering::semantic_distance(cp1, cp2);
    }

    /// Check if two codepoints are semantically related (same base character)
    [[nodiscard]] static constexpr bool are_related(
        std::int32_t cp1, std::int32_t cp2) noexcept {
        return SemanticOrdering::are_related(cp1, cp2);
    }

    /// Compute Euclidean distance squared between two codepoints in 4D space
    [[nodiscard]] static constexpr double euclidean_distance_squared(
        std::int32_t cp1, std::int32_t cp2) noexcept;

private:
    /// Determine which face a codepoint belongs to based on semantic grouping
    [[nodiscard]] static constexpr TesseractFace get_face_for_codepoint(
        std::int32_t codepoint) noexcept {
        return SemanticOrdering::get_semantic_face(codepoint);
    }

    /// Map codepoint to 3D cell coordinates using Fibonacci lattice
    /// Returns SIGNED coordinates (center-origin)
    [[nodiscard]] static constexpr std::tuple<std::int32_t, std::int32_t, std::int32_t>
    get_cell_coords_for_codepoint(std::int32_t codepoint) noexcept {
        auto coords = SemanticOrdering::get_cell_coordinates(codepoint);
        return {coords[0], coords[1], coords[2]};
    }
};

// Implementation using semantic ordering + Fibonacci lattice

constexpr TesseractSurfacePoint TesseractSurface::map_codepoint(std::int32_t codepoint) noexcept {
    if (codepoint < 0 || codepoint > MAX_CODEPOINT) {
        return {}; // Invalid codepoint returns origin
    }

    TesseractFace face = get_face_for_codepoint(codepoint);
    auto [a, b, c] = get_cell_coords_for_codepoint(codepoint);

    return TesseractSurfacePoint::create_on_face(face, a, b, c);
}

constexpr UInt128 TesseractSurface::codepoint_to_hilbert(std::int32_t codepoint) noexcept {
    auto point = map_codepoint(codepoint);
    // Convert signed coords to unsigned for Hilbert curve (offset by INT32_MAX)
    std::uint32_t ux = static_cast<std::uint32_t>(static_cast<std::int64_t>(point.x) + INT32_MAX);
    std::uint32_t uy = static_cast<std::uint32_t>(static_cast<std::int64_t>(point.y) + INT32_MAX);
    std::uint32_t uz = static_cast<std::uint32_t>(static_cast<std::int64_t>(point.z) + INT32_MAX);
    std::uint32_t uw = static_cast<std::uint32_t>(static_cast<std::int64_t>(point.w) + INT32_MAX);
    return HilbertCurve4D::coords_to_index(ux, uy, uz, uw);
}

constexpr std::int32_t TesseractSurface::hilbert_to_nearest_codepoint(UInt128 index) noexcept {
    auto coords = HilbertCurve4D::index_to_coords(index);

    // Convert unsigned Hilbert coords back to signed center-origin
    std::int32_t x = static_cast<std::int32_t>(static_cast<std::int64_t>(coords[0]) - INT32_MAX);
    std::int32_t y = static_cast<std::int32_t>(static_cast<std::int64_t>(coords[1]) - INT32_MAX);
    std::int32_t z = static_cast<std::int32_t>(static_cast<std::int64_t>(coords[2]) - INT32_MAX);
    std::int32_t w = static_cast<std::int32_t>(static_cast<std::int64_t>(coords[3]) - INT32_MAX);

    // Determine face from boundary coordinate and extract free coordinate 'b' for reverse mapping
    TesseractFace face;
    std::int32_t b; // Free coordinate used for reverse Fibonacci lattice lookup
    if (x == -TESSERACT_BOUNDARY) { face = TesseractFace::XNeg; b = z; }
    else if (x == TESSERACT_BOUNDARY) { face = TesseractFace::XPos; b = z; }
    else if (y == -TESSERACT_BOUNDARY) { face = TesseractFace::YNeg; b = z; }
    else if (y == TESSERACT_BOUNDARY) { face = TesseractFace::YPos; b = z; }
    else if (z == -TESSERACT_BOUNDARY) { face = TesseractFace::ZNeg; b = y; }
    else if (z == TESSERACT_BOUNDARY) { face = TesseractFace::ZPos; b = y; }
    else if (w == -TESSERACT_BOUNDARY) { face = TesseractFace::WNeg; b = y; }
    else if (w == TESSERACT_BOUNDARY) { face = TesseractFace::WPos; b = y; }
    else return -1; // Not on surface

    // Reverse Fibonacci lattice: 3D cell coords -> cell index
    // FibonacciLattice uses: a = (i * phi) mod 1, b = i/N, c = sqrt(i/N)
    // For reverse: estimate i from b (most linear relationship)
    std::int64_t b64 = static_cast<std::int64_t>(b) + static_cast<std::int64_t>(TESSERACT_BOUNDARY);
    std::int64_t cell_idx = b64 * static_cast<std::int64_t>(CODEPOINTS_PER_FACE) / (2LL * static_cast<std::int64_t>(TESSERACT_BOUNDARY));
    cell_idx = (cell_idx < 0) ? 0 : (cell_idx >= CODEPOINTS_PER_FACE) ? CODEPOINTS_PER_FACE - 1 : cell_idx;

    // Combine face and cell index to approximate codepoint
    // This is a nearest-neighbor approximation; exact inverse requires lookup table
    std::int32_t face_base = static_cast<std::int32_t>(face) * CODEPOINTS_PER_FACE;
    return face_base + static_cast<std::int32_t>(cell_idx);
}

constexpr double TesseractSurface::euclidean_distance_squared(
    std::int32_t cp1, std::int32_t cp2) noexcept
{
    auto p1 = map_codepoint(cp1);
    auto p2 = map_codepoint(cp2);

    // Normalize to [-1,1] range for distance calculation (center-origin)
    constexpr double scale = 1.0 / static_cast<double>(TESSERACT_BOUNDARY);

    double dx = (static_cast<double>(p1.x) - static_cast<double>(p2.x)) * scale;
    double dy = (static_cast<double>(p1.y) - static_cast<double>(p2.y)) * scale;
    double dz = (static_cast<double>(p1.z) - static_cast<double>(p2.z)) * scale;
    double dw = (static_cast<double>(p1.w) - static_cast<double>(p2.w)) * scale;

    return dx*dx + dy*dy + dz*dz + dw*dw;
}

} // namespace hartonomous
