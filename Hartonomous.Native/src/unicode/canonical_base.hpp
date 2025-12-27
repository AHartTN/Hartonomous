#pragma once

#include "char_type.hpp"
#include "unicode_ranges.hpp"
#include <cstdint>
#include <utility>

namespace hartonomous {

/// Extracts the canonical base character and category for semantic clustering.
/// This ensures that character variants (Ä, ä, À, á, etc.) cluster near their
/// base form (A, a), and case variants (A, a) are adjacent.
class CanonicalBase {
public:
    /// Result of canonical decomposition for clustering purposes
    struct Decomposition {
        std::int32_t base_codepoint;  // The base character (A for Ä)
        CharCategory category;         // Semantic category
        std::uint8_t variant_index;    // 0=base, 1-255 for variants

        /// Compute semantic distance between two decompositions
        /// Lower = more similar
        constexpr std::uint32_t distance_to(const Decomposition& other) const noexcept {
            // Category difference is most significant
            std::uint32_t cat_dist = (category != other.category) ? 0x10000 : 0;
            // Base character difference
            std::uint32_t base_dist = (base_codepoint != other.base_codepoint)
                ? static_cast<std::uint32_t>(base_codepoint ^ other.base_codepoint) : 0;
            // Variant difference is least significant
            std::uint32_t var_dist = (variant_index != other.variant_index)
                ? (variant_index ^ other.variant_index) : 0;
            return cat_dist + base_dist + var_dist;
        }
    };

    /// Decompose a codepoint to find its canonical base and category
    [[nodiscard]] static constexpr Decomposition decompose(std::int32_t codepoint) noexcept {
        // Get category first
        CharCategory cat = get_category(codepoint);

        // Get base character and variant index
        auto [base, variant] = get_base_and_variant(codepoint);

        return Decomposition{base, cat, variant};
    }

    /// Get semantic sort key for optimal clustering
    /// Characters with adjacent keys should be visually/semantically similar
    [[nodiscard]] static constexpr std::uint64_t get_semantic_key(std::int32_t codepoint) noexcept {
        auto decomp = decompose(codepoint);

        // Pack into 64 bits: [category:8][base:32][variant:8][original:16 LSB]
        std::uint64_t key = 0;
        key |= static_cast<std::uint64_t>(decomp.category) << 56;
        key |= static_cast<std::uint64_t>(decomp.base_codepoint & 0xFFFFFF) << 32;
        key |= static_cast<std::uint64_t>(decomp.variant_index) << 24;
        key |= static_cast<std::uint64_t>(codepoint & 0xFFFF);

        return key;
    }

private:
    using R = UnicodeRanges;
    
    /// Determine character category
    [[nodiscard]] static constexpr CharCategory get_category(std::int32_t cp) noexcept {
        // Control characters
        if (R::is_control(cp)) return CharCategory::Control;

        // ASCII digits and their variants
        if (R::is_ascii_digit(cp) ||
            (cp >= 0x00B2 && cp <= 0x00B3) ||  // superscript 2,3
            cp == 0x00B9 ||                      // superscript 1
            (cp >= 0x00BC && cp <= 0x00BE) ||  // fractions
            R::is_super_subscript(cp) ||
            R::is_number_forms(cp))
            return CharCategory::Number;

        // Uppercase letters (Latin, Greek, Cyrillic, etc.)
        if (R::is_ascii_upper(cp) ||
            R::is_latin1_upper_diacritic_1(cp) ||
            R::is_latin1_upper_diacritic_2(cp) ||
            R::is_latin_ext_a_upper(cp) ||
            R::is_greek_upper(cp) ||
            R::is_cyrillic_upper(cp))
            return CharCategory::Letter;

        // Lowercase letters
        if (R::is_ascii_lower(cp) ||
            R::is_latin1_lower_diacritic_1(cp) ||
            R::is_latin1_lower_diacritic_2(cp) ||
            (R::is_latin_extended_a(cp) && !R::is_latin_ext_a_upper(cp)) ||
            R::is_greek_lower(cp) ||
            R::is_cyrillic_lower(cp))
            return CharCategory::Letter;

        // Punctuation
        if (R::is_ascii_punctuation(cp) || R::is_general_punctuation(cp))
            return CharCategory::Punctuation;

        // Symbols
        if (R::is_arrows(cp) || R::is_math_operators(cp) || R::is_misc_technical(cp) ||
            R::is_dingbats(cp) || R::is_currency(cp) || R::is_letterlike(cp))
            return CharCategory::Symbol;

        // Combining marks and modifiers
        if (R::is_combining_diacritical(cp) || R::is_spacing_modifier(cp))
            return CharCategory::Modifier;

        // CJK
        if (R::is_cjk_unified(cp) || R::is_cjk_ext_a(cp) ||
            R::is_hiragana(cp) || R::is_katakana(cp))
            return CharCategory::Ideograph;

        return CharCategory::Other;
    }

    /// Get base character and variant index for diacritical clustering.
    /// This handles Latin, Greek, Cyrillic decomposition for common cases.
    [[nodiscard]] static constexpr std::pair<std::int32_t, std::uint8_t>
    get_base_and_variant(std::int32_t cp) noexcept {
        // Latin-1 Supplement decomposition (0x00C0 - 0x00FF)
        // À Á Â Ã Ä Å → A (0x0041)
        if (cp >= 0x00C0 && cp <= 0x00C5) return {0x0041, static_cast<std::uint8_t>(cp - 0x00BF)};
        // Ç → C
        if (cp == 0x00C7) return {0x0043, 1};
        // È É Ê Ë → E
        if (cp >= 0x00C8 && cp <= 0x00CB) return {0x0045, static_cast<std::uint8_t>(cp - 0x00C7)};
        // Ì Í Î Ï → I
        if (cp >= 0x00CC && cp <= 0x00CF) return {0x0049, static_cast<std::uint8_t>(cp - 0x00CB)};
        // Ð → D
        if (cp == 0x00D0) return {0x0044, 1};
        // Ñ → N
        if (cp == 0x00D1) return {0x004E, 1};
        // Ò Ó Ô Õ Ö → O
        if (cp >= 0x00D2 && cp <= 0x00D6) return {0x004F, static_cast<std::uint8_t>(cp - 0x00D1)};
        // Ù Ú Û Ü → U
        if (cp >= 0x00D9 && cp <= 0x00DC) return {0x0055, static_cast<std::uint8_t>(cp - 0x00D8)};
        // Ý → Y
        if (cp == 0x00DD) return {0x0059, 1};

        // Lowercase equivalents (offset by 0x20)
        // à á â ã ä å → a
        if (cp >= 0x00E0 && cp <= 0x00E5) return {0x0061, static_cast<std::uint8_t>(cp - 0x00DF)};
        // ç → c
        if (cp == 0x00E7) return {0x0063, 1};
        // è é ê ë → e
        if (cp >= 0x00E8 && cp <= 0x00EB) return {0x0065, static_cast<std::uint8_t>(cp - 0x00E7)};
        // ì í î ï → i
        if (cp >= 0x00EC && cp <= 0x00EF) return {0x0069, static_cast<std::uint8_t>(cp - 0x00EB)};
        // ñ → n
        if (cp == 0x00F1) return {0x006E, 1};
        // ò ó ô õ ö → o
        if (cp >= 0x00F2 && cp <= 0x00F6) return {0x006F, static_cast<std::uint8_t>(cp - 0x00F1)};
        // ù ú û ü → u
        if (cp >= 0x00F9 && cp <= 0x00FC) return {0x0075, static_cast<std::uint8_t>(cp - 0x00F8)};
        // ý ÿ → y
        if (cp == 0x00FD || cp == 0x00FF) return {0x0079, static_cast<std::uint8_t>(cp == 0x00FD ? 1 : 2)};

        // Case folding: uppercase to lowercase base for pairing
        if (R::is_ascii_upper(cp)) return {R::ascii_to_lower(cp), 0};

        // Latin Extended-A pairs (alternating upper/lower)
        if (R::is_latin_extended_a(cp)) {
            std::int32_t base = R::latin_ext_a_to_lower(cp);
            std::uint8_t variant = R::is_latin_ext_a_upper(cp) ? 1 : 0;
            return {base, variant};
        }

        // Greek case folding
        if (R::is_greek_upper(cp)) return {R::greek_to_lower(cp), 0};

        // Cyrillic case folding
        if (R::is_cyrillic_upper(cp)) return {R::cyrillic_to_lower(cp), 0};

        // Default: codepoint is its own base
        return {cp, 0};
    }
};

} // namespace hartonomous
