#pragma once

#include "../geometry/surface_point.hpp"
#include <cstdint>

namespace hartonomous {

/// Semantic face assignment for Unicode codepoints.
/// 
/// Maps codepoints to one of 8 tesseract faces based on script/region.
/// This grouping ensures related scripts are on the same face for better
/// Hilbert curve locality.
///
/// Face assignment strategy:
/// - Face 0 (XNeg): Latin scripts (Western)
/// - Face 1 (XPos): Greek, Cyrillic, Armenian, Georgian (Eastern European)
/// - Face 2 (YNeg): CJK Unified Ideographs (main block)
/// - Face 3 (YPos): CJK Extensions, Japanese, Korean
/// - Face 4 (ZNeg): Arabic, Hebrew, RTL scripts, Indic
/// - Face 5 (ZPos): Symbols, Punctuation, Technical, Math
/// - Face 6 (WNeg): Private Use, Compatibility, Specials
/// - Face 7 (WPos): Supplementary Planes (SMP, SIP, etc.)
class SemanticFaceAssignment {
public:
    /// Get the tesseract face for a codepoint based on Unicode regions
    [[nodiscard]] static constexpr TesseractFace
    get_face(std::int32_t codepoint) noexcept {
        // Face 0 (XNeg): Basic Latin + Latin Extended (Western scripts)
        // 0x0000 - 0x024F, plus scattered Latin blocks
        if (codepoint <= 0x024F) return TesseractFace::XNeg;
        if (codepoint >= 0x1E00 && codepoint <= 0x1EFF) return TesseractFace::XNeg; // Latin Ext Additional
        if (codepoint >= 0x2C60 && codepoint <= 0x2C7F) return TesseractFace::XNeg; // Latin Ext C

        // Face 1 (XPos): Greek, Cyrillic, Armenian, Georgian (Eastern European)
        // 0x0370 - 0x1FFF
        if (codepoint >= 0x0370 && codepoint <= 0x03FF) return TesseractFace::XPos; // Greek
        if (codepoint >= 0x0400 && codepoint <= 0x052F) return TesseractFace::XPos; // Cyrillic
        if (codepoint >= 0x0530 && codepoint <= 0x058F) return TesseractFace::XPos; // Armenian
        if (codepoint >= 0x10A0 && codepoint <= 0x10FF) return TesseractFace::XPos; // Georgian
        if (codepoint >= 0x1F00 && codepoint <= 0x1FFF) return TesseractFace::XPos; // Greek Ext

        // Face 2 (YNeg): CJK Unified Ideographs (main block)
        // ~20,000 characters, most common CJK
        if (codepoint >= 0x4E00 && codepoint <= 0x9FFF) return TesseractFace::YNeg;

        // Face 3 (YPos): CJK Extensions, Japanese, Korean
        if (codepoint >= 0x3400 && codepoint <= 0x4DBF) return TesseractFace::YPos; // CJK Ext A
        if (codepoint >= 0x3040 && codepoint <= 0x30FF) return TesseractFace::YPos; // Hiragana + Katakana
        if (codepoint >= 0x3100 && codepoint <= 0x312F) return TesseractFace::YPos; // Bopomofo
        if (codepoint >= 0xAC00 && codepoint <= 0xD7AF) return TesseractFace::YPos; // Hangul Syllables
        if (codepoint >= 0x1100 && codepoint <= 0x11FF) return TesseractFace::YPos; // Hangul Jamo
        if (codepoint >= 0xF900 && codepoint <= 0xFAFF) return TesseractFace::YPos; // CJK Compat

        // Face 4 (ZNeg): Arabic, Hebrew, RTL scripts + Indic
        if (codepoint >= 0x0590 && codepoint <= 0x05FF) return TesseractFace::ZNeg; // Hebrew
        if (codepoint >= 0x0600 && codepoint <= 0x06FF) return TesseractFace::ZNeg; // Arabic
        if (codepoint >= 0x0700 && codepoint <= 0x074F) return TesseractFace::ZNeg; // Syriac
        if (codepoint >= 0x0900 && codepoint <= 0x0DFF) return TesseractFace::ZNeg; // Indic scripts

        // Face 5 (ZPos): Symbols, Punctuation, Technical, Math
        if (codepoint >= 0x2000 && codepoint <= 0x2BFF) return TesseractFace::ZPos; // Punctuation -> Misc Symbols
        if (codepoint >= 0x2190 && codepoint <= 0x21FF) return TesseractFace::ZPos; // Arrows
        if (codepoint >= 0x2200 && codepoint <= 0x22FF) return TesseractFace::ZPos; // Math Operators
        if (codepoint >= 0x2300 && codepoint <= 0x23FF) return TesseractFace::ZPos; // Misc Technical
        if (codepoint >= 0x25A0 && codepoint <= 0x25FF) return TesseractFace::ZPos; // Geometric Shapes
        if (codepoint >= 0x2700 && codepoint <= 0x27BF) return TesseractFace::ZPos; // Dingbats

        // Face 6 (WNeg): Private Use, Compatibility, Specials
        if (codepoint >= 0xE000 && codepoint <= 0xF8FF) return TesseractFace::WNeg; // Private Use
        if (codepoint >= 0xFB00 && codepoint <= 0xFFFD) return TesseractFace::WNeg; // Presentation Forms + Specials
        if (codepoint >= 0xD800 && codepoint <= 0xDFFF) return TesseractFace::WNeg; // Surrogates (invalid but mapped)

        // Face 7 (WPos): Supplementary Planes (SMP, SIP, etc.)
        if (codepoint >= 0x10000) return TesseractFace::WPos;

        // Default: unmapped regions go to XNeg (will be rare)
        return TesseractFace::XNeg;
    }
};

} // namespace hartonomous
