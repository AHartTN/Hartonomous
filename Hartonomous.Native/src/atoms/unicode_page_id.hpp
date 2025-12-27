#pragma once

#include <cstdint>

namespace hartonomous {

/// Page assignment for major Unicode regions.
/// Each page maps to one tesseract face.
enum class UnicodePageId : std::uint8_t {
    Latin = 0,        // Basic Latin through Extended Latin
    European = 1,     // Greek, Cyrillic, Armenian, Georgian
    CJK_Common = 2,   // CJK Unified Ideographs (main block)
    CJK_Extended = 3, // CJK Extensions, Japanese kana, Korean
    RTL_Scripts = 4,  // Arabic, Hebrew, Syriac, Indic
    Symbols = 5,      // Punctuation, Math, Arrows, Emoji
    System = 6,       // Control, Private Use, Surrogates
    Supplementary = 7 // All supplementary planes (U+10000+)
};

} // namespace hartonomous
