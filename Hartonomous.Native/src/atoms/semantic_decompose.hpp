#pragma once

#include "atom_id.hpp"
#include "semantic_hilbert.hpp"
#include "unicode_page_id.hpp"
#include "unicode_type_id.hpp"
#include "../unicode/unicode_ranges.hpp"
#include <cstdint>

namespace hartonomous {

/// Complete semantic decomposition of Unicode codepoints.
/// Maps every codepoint to its (page, type, base, variant) coordinates.
///
/// This is the CRITICAL function - it defines the semantic structure.
/// All AI/query operations depend on this mapping being correct.
///
/// Integer-only, deterministic, Excel-verifiable.
class SemanticDecompose {
    using R = UnicodeRanges;
    
public:
    /// Decompose a codepoint into semantic coordinates.
    [[nodiscard]] static constexpr SemanticCoord decompose(std::int32_t codepoint) noexcept {
        if (codepoint < 0 || codepoint > 0x10FFFF) {
            return SemanticCoord{0, 0, 0, 0};  // Invalid
        }

        SemanticCoord result;
        result.page = get_page(codepoint);
        result.type = get_type(codepoint);
        auto [base, variant] = get_base_and_variant(codepoint);
        result.base = base;
        result.variant = variant;

        return result;
    }

    /// Get the full AtomId for a codepoint.
    [[nodiscard]] static constexpr AtomId get_atom_id(std::int32_t codepoint) noexcept {
        return SemanticHilbert::from_semantic(decompose(codepoint));
    }

    /// Get the semantic coordinates for a codepoint (alias for decompose).
    [[nodiscard]] static constexpr SemanticCoord get_coord(std::int32_t codepoint) noexcept {
        return decompose(codepoint);
    }

    /// Inverse of decompose: convert semantic coordinates back to codepoint.
    /// This is the INVERSE of get_base_and_variant().
    [[nodiscard]] static constexpr std::int32_t to_codepoint(SemanticCoord coord) noexcept {
        return to_codepoint(coord.base, coord.variant);
    }

    /// Inverse of get_base_and_variant: (base, variant) → codepoint.
    /// Deterministic - no lookup tables needed.
    [[nodiscard]] static constexpr std::int32_t to_codepoint(std::int32_t base, std::uint8_t variant) noexcept {
        // variant=0: base IS the codepoint (lowercase letters, digits, punctuation, etc.)
        if (variant == 0) {
            return base;
        }

        // variant=1: uppercase ASCII letter (base is lowercase)
        if (variant == 1 && base >= 0x0061 && base <= 0x007A) {
            return base - 32;  // 'a'->'A', 'b'->'B', etc.
        }

        // variant=1: uppercase Greek (base is lowercase Greek)
        if (variant == 1 && base >= 0x03B1 && base <= 0x03C9) {
            return base - 32;  // Greek lowercase to uppercase
        }

        // variant=1: uppercase Cyrillic (base is lowercase)
        if (variant == 1 && base >= 0x0430 && base <= 0x044F) {
            return base - 32;  // Cyrillic lowercase to uppercase
        }

        // variant=1: Latin Extended-A uppercase forms
        if (variant == 1 && R::is_latin_extended_a_lower(base)) {
            return R::latin_ext_a_to_upper(base);
        }

        // variants 2-7: uppercase diacriticals
        if (variant >= 2 && variant <= 7) {
            if (base == 0x0061) return 0x00C0 + (variant - 2);  // À Á Â Ã Ä Å
            if (base == 0x0063 && variant == 2) return 0x00C7;  // Ç
            if (base == 0x0065) return 0x00C8 + (variant - 2);  // È É Ê Ë
            if (base == 0x0069) return 0x00CC + (variant - 2);  // Ì Í Î Ï
            if (base == 0x006E && variant == 2) return 0x00D1;  // Ñ
            if (base == 0x006F) return 0x00D2 + (variant - 2);  // Ò Ó Ô Õ Ö
            if (base == 0x0075) return 0x00D9 + (variant - 2);  // Ù Ú Û Ü
            if (base == 0x0079 && variant == 2) return 0x00DD;  // Ý
        }

        // variants 8-13: lowercase diacriticals
        if (variant >= 8 && variant <= 13) {
            if (base == 0x0061) return 0x00E0 + (variant - 8);  // à á â ã ä å
            if (base == 0x0063 && variant == 8) return 0x00E7;  // ç
            if (base == 0x0065) return 0x00E8 + (variant - 8);  // è é ê ë
            if (base == 0x0069) return 0x00EC + (variant - 8);  // ì í î ï
            if (base == 0x006E && variant == 8) return 0x00F1;  // ñ
            if (base == 0x006F) return 0x00F2 + (variant - 8);  // ò ó ô õ ö
            if (base == 0x0075) return 0x00F9 + (variant - 8);  // ù ú û ü
            if (base == 0x0079 && variant == 8) return 0x00FD;  // ý
            if (base == 0x0079 && variant == 9) return 0x00FF;  // ÿ
        }

        // variant=14: fullwidth uppercase or fullwidth digit
        if (variant == 14) {
            if (base >= 0x0061 && base <= 0x007A) return 0xFF21 + (base - 0x0061);  // fullwidth A-Z
            if (base >= 0x0030 && base <= 0x0039) return 0xFF10 + (base - 0x0030);  // fullwidth 0-9
        }

        // variant=15: fullwidth lowercase
        if (variant == 15 && base >= 0x0061 && base <= 0x007A) {
            return 0xFF41 + (base - 0x0061);  // fullwidth a-z
        }

        // Fallback: return base (should not happen for valid inputs)
        return base;
    }

    /// Decode an AtomId back to its original codepoint.
    /// Inverse of get_atom_id().
    [[nodiscard]] static constexpr std::int32_t atom_to_codepoint(AtomId id) noexcept {
        SemanticCoord coord = SemanticHilbert::to_semantic(id);
        return to_codepoint(coord);
    }

private:
    //==========================================================================
    // PAGE ASSIGNMENT (3 bits = 8 pages)
    // Determines tesseract face / major Unicode region
    //==========================================================================
    [[nodiscard]] static constexpr std::uint8_t get_page(std::int32_t cp) noexcept {
        // Supplementary planes (U+10000+)
        if (cp >= 0x10000) return static_cast<std::uint8_t>(UnicodePageId::Supplementary);

        // Private Use, Surrogates, Specials (U+E000 - U+FFFF)
        if (cp >= 0xE000) return static_cast<std::uint8_t>(UnicodePageId::System);

        // Hangul Syllables (U+AC00 - U+D7AF)
        if (cp >= 0xAC00 && cp <= 0xD7AF) return static_cast<std::uint8_t>(UnicodePageId::CJK_Extended);

        // CJK Unified Ideographs main block (U+4E00 - U+9FFF)
        if (cp >= 0x4E00 && cp <= 0x9FFF) return static_cast<std::uint8_t>(UnicodePageId::CJK_Common);

        // CJK Extension A + other CJK (U+3400 - U+4DFF)
        if (cp >= 0x3400 && cp <= 0x4DFF) return static_cast<std::uint8_t>(UnicodePageId::CJK_Extended);

        // Hiragana, Katakana, Bopomofo, etc. (U+3040 - U+33FF)
        if (cp >= 0x3040 && cp <= 0x33FF) return static_cast<std::uint8_t>(UnicodePageId::CJK_Extended);

        // General Punctuation through Misc Symbols (U+2000 - U+2BFF)
        if (cp >= 0x2000 && cp <= 0x2BFF) return static_cast<std::uint8_t>(UnicodePageId::Symbols);

        // Georgian and beyond through General Punctuation (U+10A0 - U+1FFF)
        if (cp >= 0x10A0) return static_cast<std::uint8_t>(UnicodePageId::European);

        // Arabic through Tibetan + Indic scripts (U+0600 - U+0FFF)
        if (cp >= 0x0600 && cp <= 0x0FFF) return static_cast<std::uint8_t>(UnicodePageId::RTL_Scripts);

        // Hebrew (U+0590 - U+05FF)
        if (cp >= 0x0590 && cp <= 0x05FF) return static_cast<std::uint8_t>(UnicodePageId::RTL_Scripts);

        // Greek, Cyrillic, Armenian (U+0370 - U+058F)
        if (cp >= 0x0370 && cp <= 0x058F) return static_cast<std::uint8_t>(UnicodePageId::European);

        // Basic Latin through Latin Extended (U+0000 - U+036F)
        return static_cast<std::uint8_t>(UnicodePageId::Latin);
    }

    //==========================================================================
    // TYPE ASSIGNMENT (3 bits = 8 types)
    // Determines functional category
    //==========================================================================
    [[nodiscard]] static constexpr std::uint8_t get_type(std::int32_t cp) noexcept {
        // Control characters (C0, DEL, C1)
        if (R::is_control(cp)) return static_cast<std::uint8_t>(UnicodeTypeId::Control);
        
        // Format characters
        if (R::is_format(cp)) return static_cast<std::uint8_t>(UnicodeTypeId::Control);

        // Numbers
        if (R::is_ascii_digit(cp) || R::is_super_subscript(cp) ||
            R::is_fullwidth_digit(cp) || R::is_number_forms(cp))
            return static_cast<std::uint8_t>(UnicodeTypeId::Number);

        // Punctuation
        if (R::is_ascii_punctuation(cp) || R::is_general_punctuation(cp))
            return static_cast<std::uint8_t>(UnicodeTypeId::Punctuation);

        // Symbols
        if (R::is_math_operators(cp) || R::is_arrows(cp) || R::is_currency(cp) ||
            R::is_misc_technical(cp) || R::is_geometric_shapes(cp) ||
            R::is_misc_symbols(cp) || R::is_dingbats(cp) ||
            R::is_emoticons(cp) || R::is_misc_pictographs(cp))
            return static_cast<std::uint8_t>(UnicodeTypeId::Symbol);

        // Combining marks and modifiers
        if (R::is_combining_diacritical(cp) || R::is_spacing_modifier(cp))
            return static_cast<std::uint8_t>(UnicodeTypeId::Modifier);

        // CJK Ideographs
        if (R::is_cjk_unified(cp) || R::is_cjk_ext_a(cp) || R::is_cjk_ext_b(cp))
            return static_cast<std::uint8_t>(UnicodeTypeId::Ideograph);

        // Private Use and Surrogates
        if (R::is_private_use(cp) || R::is_surrogate(cp))
            return static_cast<std::uint8_t>(UnicodeTypeId::Other);

        // Letters
        if (R::is_ascii_upper(cp) || R::is_ascii_lower(cp) ||
            R::is_latin_extended(cp) || R::is_greek(cp) || R::is_cyrillic(cp) ||
            R::is_hebrew_letter(cp) || R::is_arabic(cp) ||
            R::is_hiragana(cp) || R::is_katakana(cp) || R::is_hangul(cp))
            return static_cast<std::uint8_t>(UnicodeTypeId::Letter);

        // Default: treat as Other
        return static_cast<std::uint8_t>(UnicodeTypeId::Other);
    }

    //==========================================================================
    // BASE CHARACTER + VARIANT (21 + 5 = 26 bits)
    // Base: canonical form for clustering
    // Variant: case/diacritical/style index
    //
    // VARIANT SCHEME (5 bits = 0-31):
    //   0 = base lowercase form (a, b, c...)
    //   1 = uppercase (A, B, C...)
    //   2-7 = uppercase diacriticals (À, Á, Â, Ã, Ä, Å)
    //   8-13 = lowercase diacriticals (à, á, â, ã, ä, å)
    //   14-31 = extended variants (fullwidth, etc.)
    //==========================================================================
    [[nodiscard]] static constexpr std::pair<std::int32_t, std::uint8_t>
    get_base_and_variant(std::int32_t cp) noexcept {
        // ===== LATIN UPPERCASE → lowercase as base, variant=1 =====
        if (R::is_ascii_upper(cp)) {
            return {R::ascii_to_lower(cp), 1};
        }

        // ===== Latin-1 Supplement UPPERCASE diacriticals (variants 2-7) =====
        // À Á Â Ã Ä Å → a with variants 2-7
        if (cp >= 0x00C0 && cp <= 0x00C5) return {0x0061, static_cast<std::uint8_t>(2 + (cp - 0x00C0))};
        // Ç → c
        if (cp == 0x00C7) return {0x0063, 2};
        // È É Ê Ë → e
        if (cp >= 0x00C8 && cp <= 0x00CB) return {0x0065, static_cast<std::uint8_t>(2 + (cp - 0x00C8))};
        // Ì Í Î Ï → i
        if (cp >= 0x00CC && cp <= 0x00CF) return {0x0069, static_cast<std::uint8_t>(2 + (cp - 0x00CC))};
        // Ñ → n
        if (cp == 0x00D1) return {0x006E, 2};
        // Ò Ó Ô Õ Ö → o
        if (cp >= 0x00D2 && cp <= 0x00D6) return {0x006F, static_cast<std::uint8_t>(2 + (cp - 0x00D2))};
        // Ù Ú Û Ü → u
        if (cp >= 0x00D9 && cp <= 0x00DC) return {0x0075, static_cast<std::uint8_t>(2 + (cp - 0x00D9))};
        // Ý → y
        if (cp == 0x00DD) return {0x0079, 2};

        // ===== Latin-1 Supplement LOWERCASE diacriticals (variants 8-13) =====
        // à á â ã ä å → a
        if (cp >= 0x00E0 && cp <= 0x00E5) return {0x0061, static_cast<std::uint8_t>(8 + (cp - 0x00E0))};
        // ç → c
        if (cp == 0x00E7) return {0x0063, 8};
        // è é ê ë → e
        if (cp >= 0x00E8 && cp <= 0x00EB) return {0x0065, static_cast<std::uint8_t>(8 + (cp - 0x00E8))};
        // ì í î ï → i
        if (cp >= 0x00EC && cp <= 0x00EF) return {0x0069, static_cast<std::uint8_t>(8 + (cp - 0x00EC))};
        // ñ → n
        if (cp == 0x00F1) return {0x006E, 8};
        // ò ó ô õ ö → o
        if (cp >= 0x00F2 && cp <= 0x00F6) return {0x006F, static_cast<std::uint8_t>(8 + (cp - 0x00F2))};
        // ù ú û ü → u
        if (cp >= 0x00F9 && cp <= 0x00FC) return {0x0075, static_cast<std::uint8_t>(8 + (cp - 0x00F9))};
        // ý ÿ → y
        if (cp == 0x00FD) return {0x0079, 8};
        if (cp == 0x00FF) return {0x0079, 9};

        // ===== Latin Extended-A (alternating upper/lower) =====
        if (R::is_latin_extended_a(cp)) {
            std::int32_t base = R::latin_ext_a_to_lower(cp);
            std::uint8_t variant = R::is_latin_ext_a_upper(cp) ? 1 : 0;
            return {base, variant};
        }

        // ===== Greek case folding =====
        if (R::is_greek_upper(cp)) {
            return {R::greek_to_lower(cp), 1};
        }

        // ===== Cyrillic case folding =====
        if (R::is_cyrillic_upper(cp)) {
            return {R::cyrillic_to_lower(cp), 1};
        }

        // ===== Fullwidth forms =====
        if (R::is_fullwidth_upper(cp)) {
            return {cp - 0xFF21 + 0x0061, 14};  // variant=14 for fullwidth upper
        }
        if (R::is_fullwidth_lower(cp)) {
            return {cp - 0xFF41 + 0x0061, 15};  // variant=15 for fullwidth lower
        }
        if (R::is_fullwidth_digit(cp)) {
            return {cp - 0xFF10 + 0x0030, 14};
        }

        // ===== Default: codepoint is its own base, variant=0 =====
        return {cp, 0};
    }
};

// Implement the forward-declared function in SemanticHilbert
constexpr AtomId SemanticHilbert::from_codepoint(std::int32_t codepoint) noexcept {
    return SemanticDecompose::get_atom_id(codepoint);
}

} // namespace hartonomous
