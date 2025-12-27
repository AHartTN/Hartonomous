#pragma once

#include <cstdint>
#include <string_view>

namespace hartonomous {

/// Unicode block information for semantic grouping
struct UnicodeBlock {
    std::int32_t start;
    std::int32_t end;
    std::string_view name;
};

/// Complete list of Unicode 15.0 blocks (partial - key blocks for clustering)
/// Full list would be generated from Unicode data files
namespace unicode_blocks {

constexpr UnicodeBlock BASIC_LATIN                  = {0x0000, 0x007F, "Basic Latin"};
constexpr UnicodeBlock LATIN_1_SUPPLEMENT           = {0x0080, 0x00FF, "Latin-1 Supplement"};
constexpr UnicodeBlock LATIN_EXTENDED_A             = {0x0100, 0x017F, "Latin Extended-A"};
constexpr UnicodeBlock LATIN_EXTENDED_B             = {0x0180, 0x024F, "Latin Extended-B"};
constexpr UnicodeBlock IPA_EXTENSIONS               = {0x0250, 0x02AF, "IPA Extensions"};
constexpr UnicodeBlock SPACING_MODIFIER_LETTERS     = {0x02B0, 0x02FF, "Spacing Modifier Letters"};
constexpr UnicodeBlock COMBINING_DIACRITICAL_MARKS  = {0x0300, 0x036F, "Combining Diacritical Marks"};
constexpr UnicodeBlock GREEK_AND_COPTIC             = {0x0370, 0x03FF, "Greek and Coptic"};
constexpr UnicodeBlock CYRILLIC                     = {0x0400, 0x04FF, "Cyrillic"};
constexpr UnicodeBlock CYRILLIC_SUPPLEMENT          = {0x0500, 0x052F, "Cyrillic Supplement"};
constexpr UnicodeBlock ARMENIAN                     = {0x0530, 0x058F, "Armenian"};
constexpr UnicodeBlock HEBREW                       = {0x0590, 0x05FF, "Hebrew"};
constexpr UnicodeBlock ARABIC                       = {0x0600, 0x06FF, "Arabic"};
constexpr UnicodeBlock SYRIAC                       = {0x0700, 0x074F, "Syriac"};
constexpr UnicodeBlock THAANA                       = {0x0780, 0x07BF, "Thaana"};
constexpr UnicodeBlock DEVANAGARI                   = {0x0900, 0x097F, "Devanagari"};
constexpr UnicodeBlock BENGALI                      = {0x0980, 0x09FF, "Bengali"};
constexpr UnicodeBlock GURMUKHI                     = {0x0A00, 0x0A7F, "Gurmukhi"};
constexpr UnicodeBlock GUJARATI                     = {0x0A80, 0x0AFF, "Gujarati"};
constexpr UnicodeBlock ORIYA                        = {0x0B00, 0x0B7F, "Oriya"};
constexpr UnicodeBlock TAMIL                        = {0x0B80, 0x0BFF, "Tamil"};
constexpr UnicodeBlock TELUGU                       = {0x0C00, 0x0C7F, "Telugu"};
constexpr UnicodeBlock KANNADA                      = {0x0C80, 0x0CFF, "Kannada"};
constexpr UnicodeBlock MALAYALAM                    = {0x0D00, 0x0D7F, "Malayalam"};
constexpr UnicodeBlock THAI                         = {0x0E00, 0x0E7F, "Thai"};
constexpr UnicodeBlock LAO                          = {0x0E80, 0x0EFF, "Lao"};
constexpr UnicodeBlock TIBETAN                      = {0x0F00, 0x0FFF, "Tibetan"};
constexpr UnicodeBlock GEORGIAN                     = {0x10A0, 0x10FF, "Georgian"};
constexpr UnicodeBlock HANGUL_JAMO                  = {0x1100, 0x11FF, "Hangul Jamo"};
constexpr UnicodeBlock GENERAL_PUNCTUATION          = {0x2000, 0x206F, "General Punctuation"};
constexpr UnicodeBlock SUPERSCRIPTS_AND_SUBSCRIPTS  = {0x2070, 0x209F, "Superscripts and Subscripts"};
constexpr UnicodeBlock CURRENCY_SYMBOLS             = {0x20A0, 0x20CF, "Currency Symbols"};
constexpr UnicodeBlock LETTERLIKE_SYMBOLS           = {0x2100, 0x214F, "Letterlike Symbols"};
constexpr UnicodeBlock NUMBER_FORMS                 = {0x2150, 0x218F, "Number Forms"};
constexpr UnicodeBlock ARROWS                       = {0x2190, 0x21FF, "Arrows"};
constexpr UnicodeBlock MATHEMATICAL_OPERATORS       = {0x2200, 0x22FF, "Mathematical Operators"};
constexpr UnicodeBlock MISCELLANEOUS_TECHNICAL      = {0x2300, 0x23FF, "Miscellaneous Technical"};
constexpr UnicodeBlock BOX_DRAWING                  = {0x2500, 0x257F, "Box Drawing"};
constexpr UnicodeBlock BLOCK_ELEMENTS               = {0x2580, 0x259F, "Block Elements"};
constexpr UnicodeBlock GEOMETRIC_SHAPES             = {0x25A0, 0x25FF, "Geometric Shapes"};
constexpr UnicodeBlock MISCELLANEOUS_SYMBOLS        = {0x2600, 0x26FF, "Miscellaneous Symbols"};
constexpr UnicodeBlock DINGBATS                     = {0x2700, 0x27BF, "Dingbats"};
constexpr UnicodeBlock CJK_RADICALS_SUPPLEMENT      = {0x2E80, 0x2EFF, "CJK Radicals Supplement"};
constexpr UnicodeBlock KANGXI_RADICALS              = {0x2F00, 0x2FDF, "Kangxi Radicals"};
constexpr UnicodeBlock CJK_SYMBOLS_AND_PUNCTUATION  = {0x3000, 0x303F, "CJK Symbols and Punctuation"};
constexpr UnicodeBlock HIRAGANA                     = {0x3040, 0x309F, "Hiragana"};
constexpr UnicodeBlock KATAKANA                     = {0x30A0, 0x30FF, "Katakana"};
constexpr UnicodeBlock BOPOMOFO                     = {0x3100, 0x312F, "Bopomofo"};
constexpr UnicodeBlock HANGUL_COMPATIBILITY_JAMO    = {0x3130, 0x318F, "Hangul Compatibility Jamo"};
constexpr UnicodeBlock CJK_EXTENSION_A              = {0x3400, 0x4DBF, "CJK Unified Ideographs Extension A"};
constexpr UnicodeBlock CJK_UNIFIED_IDEOGRAPHS       = {0x4E00, 0x9FFF, "CJK Unified Ideographs"};
constexpr UnicodeBlock HANGUL_SYLLABLES             = {0xAC00, 0xD7AF, "Hangul Syllables"};
constexpr UnicodeBlock HIGH_SURROGATES              = {0xD800, 0xDB7F, "High Surrogates"};
constexpr UnicodeBlock HIGH_PRIVATE_USE_SURROGATES  = {0xDB80, 0xDBFF, "High Private Use Surrogates"};
constexpr UnicodeBlock LOW_SURROGATES               = {0xDC00, 0xDFFF, "Low Surrogates"};
constexpr UnicodeBlock PRIVATE_USE_AREA             = {0xE000, 0xF8FF, "Private Use Area"};
constexpr UnicodeBlock CJK_COMPATIBILITY_IDEOGRAPHS = {0xF900, 0xFAFF, "CJK Compatibility Ideographs"};
constexpr UnicodeBlock ALPHABETIC_PRESENTATION_FORMS= {0xFB00, 0xFB4F, "Alphabetic Presentation Forms"};
constexpr UnicodeBlock ARABIC_PRESENTATION_FORMS_A  = {0xFB50, 0xFDFF, "Arabic Presentation Forms-A"};
constexpr UnicodeBlock HALFWIDTH_AND_FULLWIDTH      = {0xFF00, 0xFFEF, "Halfwidth and Fullwidth Forms"};
constexpr UnicodeBlock SPECIALS                     = {0xFFF0, 0xFFFF, "Specials"};

// Supplementary planes
constexpr UnicodeBlock LINEAR_B_SYLLABARY           = {0x10000, 0x1007F, "Linear B Syllabary"};
constexpr UnicodeBlock MATHEMATICAL_ALPHANUMERIC    = {0x1D400, 0x1D7FF, "Mathematical Alphanumeric Symbols"};
constexpr UnicodeBlock EMOTICONS                    = {0x1F600, 0x1F64F, "Emoticons"};
constexpr UnicodeBlock MISCELLANEOUS_SYMBOLS_PICTOGRAPHS = {0x1F300, 0x1F5FF, "Miscellaneous Symbols and Pictographs"};
constexpr UnicodeBlock SYMBOLS_AND_PICTOGRAPHS_EXT_A= {0x1FA70, 0x1FAFF, "Symbols and Pictographs Extended-A"};

} // namespace unicode_blocks

/// Get the block name for a given codepoint
[[nodiscard]] constexpr std::string_view get_block_name(std::int32_t codepoint) noexcept {
    using namespace unicode_blocks;
    
    // Binary search would be better for production, but this is clear
    if (codepoint >= BASIC_LATIN.start && codepoint <= BASIC_LATIN.end) return BASIC_LATIN.name;
    if (codepoint >= LATIN_1_SUPPLEMENT.start && codepoint <= LATIN_1_SUPPLEMENT.end) return LATIN_1_SUPPLEMENT.name;
    if (codepoint >= LATIN_EXTENDED_A.start && codepoint <= LATIN_EXTENDED_A.end) return LATIN_EXTENDED_A.name;
    if (codepoint >= LATIN_EXTENDED_B.start && codepoint <= LATIN_EXTENDED_B.end) return LATIN_EXTENDED_B.name;
    if (codepoint >= GREEK_AND_COPTIC.start && codepoint <= GREEK_AND_COPTIC.end) return GREEK_AND_COPTIC.name;
    if (codepoint >= CYRILLIC.start && codepoint <= CYRILLIC.end) return CYRILLIC.name;
    if (codepoint >= ARABIC.start && codepoint <= ARABIC.end) return ARABIC.name;
    if (codepoint >= HEBREW.start && codepoint <= HEBREW.end) return HEBREW.name;
    if (codepoint >= DEVANAGARI.start && codepoint <= DEVANAGARI.end) return DEVANAGARI.name;
    if (codepoint >= THAI.start && codepoint <= THAI.end) return THAI.name;
    if (codepoint >= GENERAL_PUNCTUATION.start && codepoint <= GENERAL_PUNCTUATION.end) return GENERAL_PUNCTUATION.name;
    if (codepoint >= MATHEMATICAL_OPERATORS.start && codepoint <= MATHEMATICAL_OPERATORS.end) return MATHEMATICAL_OPERATORS.name;
    if (codepoint >= BOX_DRAWING.start && codepoint <= BOX_DRAWING.end) return BOX_DRAWING.name;
    if (codepoint >= HIRAGANA.start && codepoint <= HIRAGANA.end) return HIRAGANA.name;
    if (codepoint >= KATAKANA.start && codepoint <= KATAKANA.end) return KATAKANA.name;
    if (codepoint >= CJK_UNIFIED_IDEOGRAPHS.start && codepoint <= CJK_UNIFIED_IDEOGRAPHS.end) return CJK_UNIFIED_IDEOGRAPHS.name;
    if (codepoint >= HANGUL_SYLLABLES.start && codepoint <= HANGUL_SYLLABLES.end) return HANGUL_SYLLABLES.name;
    if (codepoint >= PRIVATE_USE_AREA.start && codepoint <= PRIVATE_USE_AREA.end) return PRIVATE_USE_AREA.name;
    if (codepoint >= EMOTICONS.start && codepoint <= EMOTICONS.end) return EMOTICONS.name;
    
    // Default for unmapped blocks
    return "Unknown";
}

} // namespace hartonomous
