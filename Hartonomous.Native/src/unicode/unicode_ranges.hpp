#pragma once

#include <cstdint>

namespace hartonomous {

/// Unicode codepoint range detection utilities.
/// These are the canonical range checks used throughout the codebase.
/// Centralizing them ensures consistency and makes updates easier.
struct UnicodeRanges {
    //==========================================================================
    // Control and Format Characters
    //==========================================================================
    
    /// C0 control characters (U+0000-001F)
    [[nodiscard]] static constexpr bool is_c0_control(std::int32_t cp) noexcept {
        return cp >= 0x0000 && cp <= 0x001F;
    }
    
    /// C1 control characters (U+0080-009F)
    [[nodiscard]] static constexpr bool is_c1_control(std::int32_t cp) noexcept {
        return cp >= 0x0080 && cp <= 0x009F;
    }
    
    /// DEL character (U+007F)
    [[nodiscard]] static constexpr bool is_del(std::int32_t cp) noexcept {
        return cp == 0x007F;
    }
    
    /// Any control character (C0, DEL, C1)
    [[nodiscard]] static constexpr bool is_control(std::int32_t cp) noexcept {
        return is_c0_control(cp) || is_del(cp) || is_c1_control(cp);
    }
    
    /// Format characters (zero-width, line/para separators, specials)
    [[nodiscard]] static constexpr bool is_format(std::int32_t cp) noexcept {
        return (cp >= 0x200B && cp <= 0x200F) ||  // ZWSP, ZWNJ, ZWJ, LRM, RLM
               (cp >= 0x2028 && cp <= 0x202F) ||  // Line/para separators, embed controls
               (cp >= 0xFFF0 && cp <= 0xFFFF);    // Specials
    }

    //==========================================================================
    // Basic Latin (ASCII)
    //==========================================================================
    
    /// ASCII uppercase A-Z (U+0041-005A)
    [[nodiscard]] static constexpr bool is_ascii_upper(std::int32_t cp) noexcept {
        return cp >= 0x0041 && cp <= 0x005A;
    }
    
    /// ASCII lowercase a-z (U+0061-007A)
    [[nodiscard]] static constexpr bool is_ascii_lower(std::int32_t cp) noexcept {
        return cp >= 0x0061 && cp <= 0x007A;
    }
    
    /// ASCII digits 0-9 (U+0030-0039)
    [[nodiscard]] static constexpr bool is_ascii_digit(std::int32_t cp) noexcept {
        return cp >= 0x0030 && cp <= 0x0039;
    }
    
    /// ASCII punctuation (!"#$%..., :;<=>?@, [\]^_`, {|}~)
    [[nodiscard]] static constexpr bool is_ascii_punctuation(std::int32_t cp) noexcept {
        return (cp >= 0x0021 && cp <= 0x002F) ||
               (cp >= 0x003A && cp <= 0x0040) ||
               (cp >= 0x005B && cp <= 0x0060) ||
               (cp >= 0x007B && cp <= 0x007E);
    }

    //==========================================================================
    // Latin-1 Supplement (U+0080-00FF)
    //==========================================================================
    
    /// Latin-1 uppercase with diacritics À-Ö (U+00C0-00D6)
    [[nodiscard]] static constexpr bool is_latin1_upper_diacritic_1(std::int32_t cp) noexcept {
        return cp >= 0x00C0 && cp <= 0x00D6;
    }
    
    /// Latin-1 uppercase with diacritics Ø-Þ (U+00D8-00DE)
    [[nodiscard]] static constexpr bool is_latin1_upper_diacritic_2(std::int32_t cp) noexcept {
        return cp >= 0x00D8 && cp <= 0x00DE;
    }
    
    /// Latin-1 lowercase with diacritics ß-ö (U+00DF-00F6)
    [[nodiscard]] static constexpr bool is_latin1_lower_diacritic_1(std::int32_t cp) noexcept {
        return cp >= 0x00DF && cp <= 0x00F6;
    }
    
    /// Latin-1 lowercase with diacritics ø-ÿ (U+00F8-00FF)
    [[nodiscard]] static constexpr bool is_latin1_lower_diacritic_2(std::int32_t cp) noexcept {
        return cp >= 0x00F8 && cp <= 0x00FF;
    }

    //==========================================================================
    // Latin Extended
    //==========================================================================
    
    /// Latin Extended-A (U+0100-017F) - alternating upper/lower pairs
    [[nodiscard]] static constexpr bool is_latin_extended_a(std::int32_t cp) noexcept {
        return cp >= 0x0100 && cp <= 0x017F;
    }
    
    /// Latin Extended-B (U+0180-024F)
    [[nodiscard]] static constexpr bool is_latin_extended_b(std::int32_t cp) noexcept {
        return cp >= 0x0180 && cp <= 0x024F;
    }
    
    /// Any Latin Extended (U+00C0-024F)
    [[nodiscard]] static constexpr bool is_latin_extended(std::int32_t cp) noexcept {
        return cp >= 0x00C0 && cp <= 0x024F;
    }

    //==========================================================================
    // Modifiers and Combining Marks
    //==========================================================================
    
    /// Spacing modifier letters (U+02B0-02FF)
    [[nodiscard]] static constexpr bool is_spacing_modifier(std::int32_t cp) noexcept {
        return cp >= 0x02B0 && cp <= 0x02FF;
    }
    
    /// Combining diacritical marks (U+0300-036F)
    [[nodiscard]] static constexpr bool is_combining_diacritical(std::int32_t cp) noexcept {
        return cp >= 0x0300 && cp <= 0x036F;
    }

    //==========================================================================
    // Greek and Cyrillic
    //==========================================================================
    
    /// Greek uppercase Α-Ω (U+0391-03A9)
    [[nodiscard]] static constexpr bool is_greek_upper(std::int32_t cp) noexcept {
        return cp >= 0x0391 && cp <= 0x03A9;
    }
    
    /// Greek lowercase α-ω (U+03B1-03C9)
    [[nodiscard]] static constexpr bool is_greek_lower(std::int32_t cp) noexcept {
        return cp >= 0x03B1 && cp <= 0x03C9;
    }
    
    /// Greek and Coptic block (U+0370-03FF)
    [[nodiscard]] static constexpr bool is_greek(std::int32_t cp) noexcept {
        return cp >= 0x0370 && cp <= 0x03FF;
    }
    
    /// Cyrillic uppercase А-Я (U+0410-042F)
    [[nodiscard]] static constexpr bool is_cyrillic_upper(std::int32_t cp) noexcept {
        return cp >= 0x0410 && cp <= 0x042F;
    }
    
    /// Cyrillic lowercase а-я (U+0430-044F)
    [[nodiscard]] static constexpr bool is_cyrillic_lower(std::int32_t cp) noexcept {
        return cp >= 0x0430 && cp <= 0x044F;
    }
    
    /// Cyrillic block (U+0400-04FF)
    [[nodiscard]] static constexpr bool is_cyrillic(std::int32_t cp) noexcept {
        return cp >= 0x0400 && cp <= 0x04FF;
    }

    //==========================================================================
    // RTL Scripts (Hebrew, Arabic)
    //==========================================================================
    
    /// Hebrew block (U+0590-05FF)
    [[nodiscard]] static constexpr bool is_hebrew(std::int32_t cp) noexcept {
        return cp >= 0x0590 && cp <= 0x05FF;
    }
    
    /// Hebrew consonants (U+05D0-05EA)
    [[nodiscard]] static constexpr bool is_hebrew_letter(std::int32_t cp) noexcept {
        return cp >= 0x05D0 && cp <= 0x05EA;
    }
    
    /// Arabic block (U+0600-06FF)
    [[nodiscard]] static constexpr bool is_arabic(std::int32_t cp) noexcept {
        return cp >= 0x0600 && cp <= 0x06FF;
    }

    //==========================================================================
    // CJK Scripts
    //==========================================================================
    
    /// Hiragana (U+3040-309F)
    [[nodiscard]] static constexpr bool is_hiragana(std::int32_t cp) noexcept {
        return cp >= 0x3040 && cp <= 0x309F;
    }
    
    /// Katakana (U+30A0-30FF)
    [[nodiscard]] static constexpr bool is_katakana(std::int32_t cp) noexcept {
        return cp >= 0x30A0 && cp <= 0x30FF;
    }
    
    /// CJK Unified Ideographs (U+4E00-9FFF)
    [[nodiscard]] static constexpr bool is_cjk_unified(std::int32_t cp) noexcept {
        return cp >= 0x4E00 && cp <= 0x9FFF;
    }
    
    /// CJK Extension A (U+3400-4DBF)
    [[nodiscard]] static constexpr bool is_cjk_ext_a(std::int32_t cp) noexcept {
        return cp >= 0x3400 && cp <= 0x4DBF;
    }
    
    /// CJK Extension B (U+20000-2A6DF)
    [[nodiscard]] static constexpr bool is_cjk_ext_b(std::int32_t cp) noexcept {
        return cp >= 0x20000 && cp <= 0x2A6DF;
    }
    
    /// Hangul Syllables (U+AC00-D7AF)
    [[nodiscard]] static constexpr bool is_hangul(std::int32_t cp) noexcept {
        return cp >= 0xAC00 && cp <= 0xD7AF;
    }

    //==========================================================================
    // Symbols and Punctuation
    //==========================================================================
    
    /// General Punctuation (U+2000-206F)
    [[nodiscard]] static constexpr bool is_general_punctuation(std::int32_t cp) noexcept {
        return cp >= 0x2000 && cp <= 0x206F;
    }
    
    /// Superscripts and Subscripts (U+2070-209F)
    [[nodiscard]] static constexpr bool is_super_subscript(std::int32_t cp) noexcept {
        return cp >= 0x2070 && cp <= 0x209F;
    }
    
    /// Currency Symbols (U+20A0-20CF)
    [[nodiscard]] static constexpr bool is_currency(std::int32_t cp) noexcept {
        return cp >= 0x20A0 && cp <= 0x20CF;
    }
    
    /// Letterlike Symbols (U+2100-214F)
    [[nodiscard]] static constexpr bool is_letterlike(std::int32_t cp) noexcept {
        return cp >= 0x2100 && cp <= 0x214F;
    }
    
    /// Number Forms (U+2150-218F)
    [[nodiscard]] static constexpr bool is_number_forms(std::int32_t cp) noexcept {
        return cp >= 0x2150 && cp <= 0x218F;
    }
    
    /// Arrows (U+2190-21FF)
    [[nodiscard]] static constexpr bool is_arrows(std::int32_t cp) noexcept {
        return cp >= 0x2190 && cp <= 0x21FF;
    }
    
    /// Mathematical Operators (U+2200-22FF)
    [[nodiscard]] static constexpr bool is_math_operators(std::int32_t cp) noexcept {
        return cp >= 0x2200 && cp <= 0x22FF;
    }
    
    /// Miscellaneous Technical (U+2300-23FF)
    [[nodiscard]] static constexpr bool is_misc_technical(std::int32_t cp) noexcept {
        return cp >= 0x2300 && cp <= 0x23FF;
    }
    
    /// Geometric Shapes (U+25A0-25FF)
    [[nodiscard]] static constexpr bool is_geometric_shapes(std::int32_t cp) noexcept {
        return cp >= 0x25A0 && cp <= 0x25FF;
    }
    
    /// Miscellaneous Symbols (U+2600-26FF)
    [[nodiscard]] static constexpr bool is_misc_symbols(std::int32_t cp) noexcept {
        return cp >= 0x2600 && cp <= 0x26FF;
    }
    
    /// Dingbats (U+2700-27BF)
    [[nodiscard]] static constexpr bool is_dingbats(std::int32_t cp) noexcept {
        return cp >= 0x2700 && cp <= 0x27BF;
    }

    //==========================================================================
    // Fullwidth Forms
    //==========================================================================
    
    /// Fullwidth digits 0-9 (U+FF10-FF19)
    [[nodiscard]] static constexpr bool is_fullwidth_digit(std::int32_t cp) noexcept {
        return cp >= 0xFF10 && cp <= 0xFF19;
    }
    
    /// Fullwidth uppercase A-Z (U+FF21-FF3A)
    [[nodiscard]] static constexpr bool is_fullwidth_upper(std::int32_t cp) noexcept {
        return cp >= 0xFF21 && cp <= 0xFF3A;
    }
    
    /// Fullwidth lowercase a-z (U+FF41-FF5A)
    [[nodiscard]] static constexpr bool is_fullwidth_lower(std::int32_t cp) noexcept {
        return cp >= 0xFF41 && cp <= 0xFF5A;
    }

    //==========================================================================
    // Special Ranges
    //==========================================================================
    
    /// Private Use Area (U+E000-F8FF)
    [[nodiscard]] static constexpr bool is_private_use(std::int32_t cp) noexcept {
        return cp >= 0xE000 && cp <= 0xF8FF;
    }
    
    /// Surrogate pairs (U+D800-DFFF) - invalid in isolation
    [[nodiscard]] static constexpr bool is_surrogate(std::int32_t cp) noexcept {
        return cp >= 0xD800 && cp <= 0xDFFF;
    }
    
    /// Supplementary planes (U+10000+)
    [[nodiscard]] static constexpr bool is_supplementary(std::int32_t cp) noexcept {
        return cp >= 0x10000;
    }

    //==========================================================================
    // Emoji
    //==========================================================================
    
    /// Emoticons (U+1F600-1F64F)
    [[nodiscard]] static constexpr bool is_emoticons(std::int32_t cp) noexcept {
        return cp >= 0x1F600 && cp <= 0x1F64F;
    }
    
    /// Miscellaneous Symbols and Pictographs (U+1F300-1F5FF)
    [[nodiscard]] static constexpr bool is_misc_pictographs(std::int32_t cp) noexcept {
        return cp >= 0x1F300 && cp <= 0x1F5FF;
    }

    //==========================================================================
    // Case Folding Helpers
    //==========================================================================
    
    /// Get lowercase version of ASCII uppercase (returns original if not upper)
    [[nodiscard]] static constexpr std::int32_t ascii_to_lower(std::int32_t cp) noexcept {
        return is_ascii_upper(cp) ? (cp + 0x20) : cp;
    }
    
    /// Get uppercase version of ASCII lowercase (returns original if not lower)
    [[nodiscard]] static constexpr std::int32_t ascii_to_upper(std::int32_t cp) noexcept {
        return is_ascii_lower(cp) ? (cp - 0x20) : cp;
    }
    
    /// Get lowercase version of Greek uppercase (returns original if not upper)
    [[nodiscard]] static constexpr std::int32_t greek_to_lower(std::int32_t cp) noexcept {
        return is_greek_upper(cp) ? (cp + 0x20) : cp;
    }
    
    /// Get lowercase version of Cyrillic uppercase (returns original if not upper)
    [[nodiscard]] static constexpr std::int32_t cyrillic_to_lower(std::int32_t cp) noexcept {
        return is_cyrillic_upper(cp) ? (cp + 0x20) : cp;
    }
    
    /// For Latin Extended-A, get the lowercase form (odd codepoint)
    [[nodiscard]] static constexpr std::int32_t latin_ext_a_to_lower(std::int32_t cp) noexcept {
        return is_latin_extended_a(cp) ? (cp | 1) : cp;
    }
    
    /// For Latin Extended-A, get the uppercase form (even codepoint)
    [[nodiscard]] static constexpr std::int32_t latin_ext_a_to_upper(std::int32_t cp) noexcept {
        return is_latin_extended_a(cp) ? (cp & ~1) : cp;
    }
    
    /// Check if Latin Extended-A codepoint is uppercase (even codepoint)
    [[nodiscard]] static constexpr bool is_latin_ext_a_upper(std::int32_t cp) noexcept {
        return is_latin_extended_a(cp) && ((cp & 1) == 0);
    }
    
    /// Check if Latin Extended-A codepoint is lowercase (odd codepoint)
    [[nodiscard]] static constexpr bool is_latin_extended_a_lower(std::int32_t cp) noexcept {
        return is_latin_extended_a(cp) && ((cp & 1) == 1);
    }
};

} // namespace hartonomous
