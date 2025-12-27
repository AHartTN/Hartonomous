#pragma once

#include <cstdint>

namespace hartonomous {

/// Semantic coordinates for a Unicode codepoint.
/// These four dimensions capture the semantic hierarchy:
///
/// 1. PAGE (3 bits): Major Unicode region / script family
///    - Determines which tesseract face
///    - Latin, Greek, CJK, Arabic, Symbols, etc.
///
/// 2. TYPE (3 bits): Functional category
///    - Letter, Number, Punctuation, Symbol, System, etc.
///
/// 3. BASE (21 bits): Canonical base character
///    - For diacritical clustering: Ä → A, é → e
///    - Max value 0x10FFFF (full Unicode range)
///
/// 4. VARIANT (5 bits): Case and modification
///    - 0 = base form
///    - 1 = uppercase
///    - 2-31 = diacritical/stylistic variants
struct SemanticCoord {
    std::uint8_t page;     // 0-7: Major Unicode region
    std::uint8_t type;     // 0-7: Character type
    std::int32_t base;     // Base character codepoint
    std::uint8_t variant;  // 0-31: Variant index

    /// Pack into 32-bit semantic index
    /// Layout: [page:3][type:3][base_high:10][base_low:11][variant:5] = 32 bits
    [[nodiscard]] constexpr std::uint32_t pack() const noexcept {
        std::uint32_t result = 0;
        result |= (static_cast<std::uint32_t>(page & 0x7) << 29);      // 3 bits
        result |= (static_cast<std::uint32_t>(type & 0x7) << 26);      // 3 bits
        result |= (static_cast<std::uint32_t>(base & 0x1FFFFF) << 5);  // 21 bits
        result |= (static_cast<std::uint32_t>(variant & 0x1F));        // 5 bits
        return result;
    }

    /// Unpack from 32-bit semantic index
    [[nodiscard]] static constexpr SemanticCoord unpack(std::uint32_t packed) noexcept {
        SemanticCoord c;
        c.page = static_cast<std::uint8_t>((packed >> 29) & 0x7);
        c.type = static_cast<std::uint8_t>((packed >> 26) & 0x7);
        c.base = static_cast<std::int32_t>((packed >> 5) & 0x1FFFFF);
        c.variant = static_cast<std::uint8_t>(packed & 0x1F);
        return c;
    }
};

} // namespace hartonomous
