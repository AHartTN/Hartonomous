#pragma once

#include <cstdint>

namespace hartonomous {

/// Type classification for functional categories (dimension 2 of 4 in semantic space).
/// This defines the broad functional category of a Unicode character.
enum class UnicodeTypeId : std::uint8_t {
    Control = 0,      // Control characters, format chars, BOM, etc.
    Letter = 1,       // Alphabetic (Latin, Greek, Cyrillic, etc.)
    Number = 2,       // Digits, fractions, numeric symbols
    Punctuation = 3,  // Sentence punctuation, quotes, brackets
    Symbol = 4,       // Math, currency, arrows, emoji, technical
    Modifier = 5,     // Combining marks, spacing modifiers, diacritics
    Ideograph = 6,    // CJK ideographs, Yi syllables
    Other = 7         // Private use, unassigned, surrogates
};

// Backward compatibility aliases
using CharType = UnicodeTypeId;
using CharCategory = UnicodeTypeId;

// Alias for Control that matches "System" terminology used elsewhere
constexpr UnicodeTypeId UnicodeTypeId_System = UnicodeTypeId::Control;

} // namespace hartonomous
