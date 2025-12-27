#pragma once

#include "canonical_base.hpp"
#include "semantic_face_assignment.hpp"
#include "../geometry/fibonacci_lattice.hpp"
#include "../geometry/surface_point.hpp"
#include <cstdint>
#include <array>

namespace hartonomous {

/// Semantic ordering for Unicode codepoints.
///
/// The goal: similar characters should have nearby Hilbert indices.
/// - 'A' near 'a' near 'Ä' near 'ä' near 'À' near 'á' ...
/// - '0' near '1' near '2' ... near '9'
/// - All punctuation together
/// - All CJK together (with radical/stroke ordering preserved)
///
/// Strategy:
/// 1. Assign codepoint to a tesseract face based on Unicode region
/// 2. Within the face, order by: Category → Base Character → Variant
/// 3. Use Fibonacci lattice to map this ordering to 3D cell coordinates
/// 4. The Hilbert curve then provides the final 1D index with locality
class SemanticOrdering {
public:
    static constexpr int MAX_CODEPOINT = 0x10FFFF;
    static constexpr int CODEPOINT_COUNT = MAX_CODEPOINT + 1;

    /// Compute semantic position index for a codepoint.
    /// Codepoints with nearby semantic indices should be visually similar.
    [[nodiscard]] static constexpr std::uint64_t
    get_semantic_index(std::int32_t codepoint) noexcept {
        if (codepoint < 0 || codepoint > MAX_CODEPOINT) {
            return 0;
        }

        // Get canonical decomposition
        auto decomp = CanonicalBase::decompose(codepoint);

        // Build semantic index:
        // [Face:3][Category:4][BaseChar:21][Variant:8][Original LSB:12] = 48 bits
        // This leaves room for expansion and ensures locality

        std::uint64_t face = static_cast<std::uint64_t>(get_semantic_face(codepoint));
        std::uint64_t category = static_cast<std::uint64_t>(decomp.category);
        std::uint64_t base = static_cast<std::uint64_t>(decomp.base_codepoint & 0x1FFFFF);
        std::uint64_t variant = static_cast<std::uint64_t>(decomp.variant_index);
        std::uint64_t original_lsb = static_cast<std::uint64_t>(codepoint & 0xFFF);

        std::uint64_t index = 0;
        index |= (face & 0x7) << 45;           // 3 bits for face (8 faces)
        index |= (category & 0xF) << 41;       // 4 bits for category (16 categories)
        index |= (base & 0x1FFFFF) << 20;      // 21 bits for base char
        index |= (variant & 0xFF) << 12;       // 8 bits for variant
        index |= (original_lsb & 0xFFF);       // 12 bits for disambiguation

        return index;
    }

    /// Map a codepoint to 3D cell coordinates within its assigned face.
    /// Uses Fibonacci lattice for locality-preserving distribution.
    /// Returns SIGNED coordinates in range [-INT32_MAX, +INT32_MAX] - CENTER-ORIGIN.
    [[nodiscard]] static constexpr std::array<std::int32_t, 3>
    get_cell_coordinates(std::int32_t codepoint) noexcept {
        std::uint64_t sem_idx = get_semantic_index(codepoint);
        return FibonacciLattice3D::point_from_index_signed(sem_idx);
    }

    /// Get the tesseract face for a codepoint based on Unicode regions.
    /// Delegates to SemanticFaceAssignment for the actual mapping.
    [[nodiscard]] static constexpr TesseractFace
    get_semantic_face(std::int32_t codepoint) noexcept {
        return SemanticFaceAssignment::get_face(codepoint);
    }

    /// Compute semantic distance between two codepoints.
    /// Lower distance = more similar characters.
    [[nodiscard]] static constexpr std::uint64_t
    semantic_distance(std::int32_t cp1, std::int32_t cp2) noexcept {
        auto d1 = CanonicalBase::decompose(cp1);
        auto d2 = CanonicalBase::decompose(cp2);

        auto f1 = get_semantic_face(cp1);
        auto f2 = get_semantic_face(cp2);
        std::uint64_t face_dist = (f1 != f2) ? 0x1000000000000ULL : 0;
        std::uint64_t cat_dist = (d1.category != d2.category) ? 0x100000000ULL : 0;

        std::uint64_t base_diff = 0;
        if (d1.base_codepoint != d2.base_codepoint) {
            base_diff = static_cast<std::uint64_t>(
                (d1.base_codepoint > d2.base_codepoint)
                    ? (d1.base_codepoint - d2.base_codepoint)
                    : (d2.base_codepoint - d1.base_codepoint)
            );
            base_diff <<= 16;
        }

        std::uint64_t var_diff = static_cast<std::uint64_t>(
            (d1.variant_index > d2.variant_index)
                ? (d1.variant_index - d2.variant_index)
                : (d2.variant_index - d1.variant_index)
        );

        return face_dist + cat_dist + base_diff + var_diff;
    }

    /// Check if two codepoints are "semantically adjacent"
    /// (same base character, different variants or cases)
    [[nodiscard]] static constexpr bool
    are_related(std::int32_t cp1, std::int32_t cp2) noexcept {
        auto d1 = CanonicalBase::decompose(cp1);
        auto d2 = CanonicalBase::decompose(cp2);
        return d1.base_codepoint == d2.base_codepoint;
    }

    /// Get the "neighborhood" of related codepoints (up to 16)
    [[nodiscard]] static constexpr std::array<std::int32_t, 16>
    get_related_codepoints(std::int32_t codepoint) noexcept {
        std::array<std::int32_t, 16> result = {};
        int count = 0;

        auto decomp = CanonicalBase::decompose(codepoint);
        std::int32_t base = decomp.base_codepoint;

        // If base is lowercase a-z, include uppercase and common diacritics
        if (base >= 0x0061 && base <= 0x007A) {
            result[count++] = base;         // lowercase itself
            result[count++] = base - 0x20;  // uppercase

            // Common diacritical variants (Latin-1 Supplement)
            if (base == 0x0061) { // 'a'
                result[count++] = 0x00E0; result[count++] = 0x00E1;
                result[count++] = 0x00E2; result[count++] = 0x00E4;
                result[count++] = 0x00C0; result[count++] = 0x00C1;
            } else if (base == 0x0065) { // 'e'
                result[count++] = 0x00E8; result[count++] = 0x00E9;
                result[count++] = 0x00EA; result[count++] = 0x00EB;
            } else if (base == 0x006F) { // 'o'
                result[count++] = 0x00F2; result[count++] = 0x00F3;
                result[count++] = 0x00F4; result[count++] = 0x00F6;
            } else if (base == 0x0075) { // 'u'
                result[count++] = 0x00F9; result[count++] = 0x00FA;
                result[count++] = 0x00FB; result[count++] = 0x00FC;
            }
        }

        while (count < 16) result[count++] = -1;
        return result;
    }
};

} // namespace hartonomous
