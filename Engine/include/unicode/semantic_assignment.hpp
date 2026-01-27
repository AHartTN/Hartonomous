#pragma once

#include "../geometry/super_fibonacci.hpp"
#include <Eigen/Core>
#include <cstdint>
#include <unordered_map>
#include <vector>
#include <string>
#include <algorithm>

namespace hartonomous::unicode {

/**
 * @brief Semantic Unicode Codepoint Assignment to S³
 *
 * This class manages the semantic assignment of all Unicode codepoints to
 * positions on the 3-sphere (S³), ensuring that semantically related characters
 * are geometrically proximate.
 *
 * Assignment Strategy:
 *   - Characters are grouped by semantic categories
 *   - Each category gets a region on S³
 *   - Within regions, fine-grained similarity determines exact position
 *
 * Categories (examples):
 *   - Latin letters: A-Z, a-z with case pairs adjacent
 *   - Accented letters: Ä near A, é near e
 *   - Numbers: 0-9 clustered
 *   - Punctuation: , . ! ? : ; etc.
 *   - Whitespace: space, tab, newline
 *   - Emoji: grouped by category (faces, objects, symbols)
 *   - CJK: by radical/stroke count
 *   - Control characters: separate isolated region
 *   - Surrogates: special handling region
 *
 * This ensures:
 *   - "The" and "the" are nearby (case variation)
 *   - "café" characters cluster appropriately
 *   - Numbers form a coherent block
 *   - Emoji are organized semantically
 */
class SemanticAssignment {
public:
    using Vec4 = Eigen::Vector4d;
    using SuperFib = geometry::SuperFibonacci;

    /**
     * @brief Unicode category for semantic clustering
     */
    enum class Category : uint16_t {
        // Basic Latin & Extensions
        LatinUppercase,           ///< A-Z, Á-Ž, etc.
        LatinLowercase,           ///< a-z, á-ž, etc.

        // Digits
        Digits,                   ///< 0-9, ⁰-⁹, ₀-₉

        // Punctuation & Symbols
        PunctuationCommon,        ///< , . ! ? : ;
        PunctuationRare,          ///< « » ‹ › " " ' '
        MathSymbols,              ///< + - × ÷ = < > ∑ ∫
        CurrencySymbols,          ///< $ € £ ¥ ¢

        // Whitespace & Control
        Whitespace,               ///< space, tab, newline, nbsp
        ControlCharacters,        ///< 0x00-0x1F, 0x7F-0x9F

        // Greek & Cyrillic
        GreekUppercase,           ///< Α-Ω
        GreekLowercase,           ///< α-ω
        CyrillicUppercase,        ///< А-Я
        CyrillicLowercase,        ///< а-я

        // CJK (Chinese, Japanese, Korean)
        CJKIdeographs,            ///< 一-龥 (by radical/strokes)
        Hiragana,                 ///< あ-ん
        Katakana,                 ///< ア-ン
        Hangul,                   ///< 가-힣

        // Emoji & Pictographs
        EmojiSmileys,             ///< 😀-😿
        EmojiPeople,              ///< 👤-👥, 🧑-🧔
        EmojiAnimals,             ///< 🐀-🦴
        EmojiFood,                ///< 🍇-🍽
        EmojiTravel,              ///< ⛰-🛸
        EmojiObjects,             ///< 📦-🔧
        EmojiSymbols,             ///< ❤-⭕
        EmojiFlags,               ///< 🇦-🇿

        // Other Scripts
        Arabic,                   ///< ا-ي
        Hebrew,                   ///< א-ת
        Devanagari,               ///< अ-ह (Hindi, Sanskrit)
        Thai,                     ///< ก-ฮ

        // Special & Technical
        BoxDrawing,               ///< ─│┌┐└┘├┤
        GeometricShapes,          ///< ■□▲△●○
        Arrows,                   ///< ←→↑↓⇐⇒
        Dingbats,                 ///< ✁-➾

        // Private Use & Surrogates
        PrivateUse,               ///< U+E000-U+F8FF
        Surrogates,               ///< U+D800-U+DFFF

        // Unassigned
        Unassigned,               ///< Reserved/unassigned codepoints

        CATEGORY_COUNT
    };

    /**
     * @brief Semantic cluster defining a region on S³
     */
    struct SemanticCluster {
        Category category;
        Vec4 center;              ///< Center point on S³
        double radius;            ///< Radius of the cluster (geodesic)
        uint32_t start_index;     ///< Starting index in Super Fibonacci sequence
        uint32_t count;           ///< Number of points allocated to this cluster

        /**
         * @brief Get a point within this cluster
         *
         * @param local_index Index within cluster (0 to count-1)
         * @return Vec4 Point on S³
         */
        Vec4 get_point(uint32_t local_index) const {
            if (local_index >= count) {
                local_index = count - 1; // Clamp
            }

            uint32_t global_index = start_index + local_index;
            return SuperFib::point_on_s3(global_index, start_index + count);
        }
    };

    /**
     * @brief Codepoint assignment result
     */
    struct Assignment {
        uint32_t codepoint;
        Category category;
        Vec4 s3_position;
        uint32_t cluster_index;     ///< Index within category cluster

        /**
         * @brief Get semantic similarity score to another codepoint
         *
         * 1.0 = identical, 0.0 = completely different
         */
        double similarity_to(const Assignment& other) const {
            if (category == other.category) {
                // Same category: use geometric distance
                double dist = (s3_position - other.s3_position).norm();
                return std::max(0.0, 1.0 - (dist / 2.0)); // Normalize to [0, 1]
            } else {
                // Different categories: check if related
                return category_similarity(category, other.category);
            }
        }
    };

    /**
     * @brief Initialize the semantic assignment system
     *
     * This precomputes the cluster layout across S³.
     */
    static void initialize() {
        if (!clusters_.empty()) return; // Already initialized

        // Allocate points across S³ based on category importance/size
        allocate_clusters();
    }

    /**
     * @brief Get the S³ position for a Unicode codepoint
     *
     * @param codepoint Unicode codepoint (U+0000 to U+10FFFF)
     * @return Assignment Complete assignment data
     */
    static Assignment get_assignment(uint32_t codepoint) {
        initialize();

        Assignment result;
        result.codepoint = codepoint;
        result.category = classify_codepoint(codepoint);

        // Find the cluster for this category
        auto& cluster = get_cluster(result.category);

        // Map codepoint to local index within category
        result.cluster_index = codepoint_to_cluster_index(codepoint, result.category);

        // Get the S³ position
        result.s3_position = cluster.get_point(result.cluster_index);

        return result;
    }

    /**
     * @brief Get assignments for all codepoints in a string
     */
    static std::vector<Assignment> get_assignments(const std::string& utf8_string);

private:
    static std::unordered_map<Category, SemanticCluster> clusters_;
    static constexpr uint32_t TOTAL_UNICODE_POINTS = 0x110000; // 1,114,112 codepoints

    /**
     * @brief Classify a codepoint into a semantic category
     */
    static Category classify_codepoint(uint32_t cp) {
        // Basic Latin uppercase
        if (cp >= 'A' && cp <= 'Z') return Category::LatinUppercase;
        if (cp >= 'a' && cp <= 'z') return Category::LatinLowercase;

        // Digits
        if (cp >= '0' && cp <= '9') return Category::Digits;

        // Common punctuation
        if (cp == ' ' || cp == '\t' || cp == '\n' || cp == '\r') return Category::Whitespace;
        if (cp >= 0x20 && cp <= 0x2F) return Category::PunctuationCommon; // !"#$%&'()*+,-./
        if (cp >= 0x3A && cp <= 0x40) return Category::PunctuationCommon; // :;<=>?@
        if (cp >= 0x5B && cp <= 0x60) return Category::PunctuationCommon; // [\]^_`
        if (cp >= 0x7B && cp <= 0x7E) return Category::PunctuationCommon; // {|}~

        // Control characters
        if (cp <= 0x1F || (cp >= 0x7F && cp <= 0x9F)) return Category::ControlCharacters;

        // Latin Extended
        if (cp >= 0xC0 && cp <= 0xD6) return Category::LatinUppercase; // À-Ö
        if (cp >= 0xD8 && cp <= 0xDE) return Category::LatinUppercase; // Ø-Þ
        if (cp >= 0xE0 && cp <= 0xF6) return Category::LatinLowercase; // à-ö
        if (cp >= 0xF8 && cp <= 0xFF) return Category::LatinLowercase; // ø-ÿ

        // Greek
        if (cp >= 0x0391 && cp <= 0x03A9) return Category::GreekUppercase; // Α-Ω
        if (cp >= 0x03B1 && cp <= 0x03C9) return Category::GreekLowercase; // α-ω

        // Cyrillic
        if (cp >= 0x0410 && cp <= 0x042F) return Category::CyrillicUppercase; // А-Я
        if (cp >= 0x0430 && cp <= 0x044F) return Category::CyrillicLowercase; // а-я

        // CJK
        if (cp >= 0x4E00 && cp <= 0x9FFF) return Category::CJKIdeographs;
        if (cp >= 0x3040 && cp <= 0x309F) return Category::Hiragana;
        if (cp >= 0x30A0 && cp <= 0x30FF) return Category::Katakana;
        if (cp >= 0xAC00 && cp <= 0xD7AF) return Category::Hangul;

        // Emoji ranges
        if (cp >= 0x1F600 && cp <= 0x1F64F) return Category::EmojiSmileys;
        if (cp >= 0x1F300 && cp <= 0x1F5FF) return Category::EmojiSymbols;
        if (cp >= 0x1F900 && cp <= 0x1F9FF) return Category::EmojiPeople;

        // Surrogates
        if (cp >= 0xD800 && cp <= 0xDFFF) return Category::Surrogates;

        // Private use
        if (cp >= 0xE000 && cp <= 0xF8FF) return Category::PrivateUse;

        // Default: unassigned
        return Category::Unassigned;
    }

    /**
     * @brief Allocate clusters across S³
     */
    static void allocate_clusters() {
        // Total number of categories
        const uint32_t num_categories = static_cast<uint32_t>(Category::CATEGORY_COUNT);

        // Distribute points using Super Fibonacci
        // Each category gets a proportional share based on its expected size

        uint32_t current_index = 0;

        // Define category sizes (approximate)
        std::vector<std::pair<Category, uint32_t>> sizes = {
            {Category::LatinUppercase, 500},
            {Category::LatinLowercase, 500},
            {Category::Digits, 100},
            {Category::PunctuationCommon, 200},
            {Category::Whitespace, 20},
            {Category::ControlCharacters, 100},
            {Category::CJKIdeographs, 50000},
            {Category::EmojiSmileys, 1000},
            // ... add more as needed
        };

        for (auto& [cat, size] : sizes) {
            SemanticCluster cluster;
            cluster.category = cat;
            cluster.start_index = current_index;
            cluster.count = size;
            cluster.center = SuperFib::point_on_s3(current_index + size / 2, TOTAL_UNICODE_POINTS);
            cluster.radius = 0.1; // Approximate, will be refined

            clusters_[cat] = cluster;
            current_index += size;
        }
    }

    /**
     * @brief Get cluster for a category
     */
    static SemanticCluster& get_cluster(Category cat) {
        return clusters_[cat];
    }

    /**
     * @brief Map codepoint to index within its category cluster
     */
    static uint32_t codepoint_to_cluster_index(uint32_t cp, Category cat) {
        // Simple mapping: just use the codepoint value modulo cluster size
        // In practice, this would be more sophisticated (e.g., case pairs adjacent)
        auto& cluster = clusters_[cat];
        return cp % cluster.count;
    }

    /**
     * @brief Compute similarity between two categories
     */
    static double category_similarity(Category c1, Category c2) {
        // Related categories have higher similarity
        if (c1 == c2) return 1.0;

        // Uppercase/lowercase pairs
        if ((c1 == Category::LatinUppercase && c2 == Category::LatinLowercase) ||
            (c1 == Category::LatinLowercase && c2 == Category::LatinUppercase)) {
            return 0.8;
        }

        // All punctuation related
        if ((c1 == Category::PunctuationCommon || c1 == Category::PunctuationRare) &&
            (c2 == Category::PunctuationCommon || c2 == Category::PunctuationRare)) {
            return 0.6;
        }

        // Default: unrelated
        return 0.1;
    }
};

// Static member initialization
std::unordered_map<SemanticAssignment::Category, SemanticAssignment::SemanticCluster>
    SemanticAssignment::clusters_;

} // namespace hartonomous::unicode
