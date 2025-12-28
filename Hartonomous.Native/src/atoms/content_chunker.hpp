#pragma once

/// UNIVERSAL TOKENIZATION
///
/// The problem: Balanced binary tree splits "Captain Ahab" differently
/// when it appears in Moby Dick vs standalone. Substring queries fail.
///
/// The solution: Tokenize based on character class, not position.
/// - ASCII letters/digits → grouped as words ("Captain" is one token)
/// - Whitespace → grouped as runs (" " or "  \n" is one token)
/// - CJK/symbols/punctuation → one token per codepoint ("船" is one token)
///
/// This works for ALL languages:
/// - English: "Captain Ahab" → ["Captain", " ", "Ahab"]
/// - Chinese: "船长白鲸" → ["船", "长", "白", "鲸"]
/// - Mixed: "Hello世界" → ["Hello", "世", "界"]
///
/// Same text = same tokens = same composition = queryable substrings.

#include <cstdint>
#include <vector>
#include <string>

namespace hartonomous {

/// Token from universal tokenization
struct Token {
    const std::uint8_t* data;
    std::size_t length;
};

/// Universal tokenizer that works for all languages.
/// Guarantees: Same content = same tokens = same encoding.
class UniversalTokenizer {
public:
    /// Tokenize into universal tokens.
    /// ASCII words grouped, CJK/symbols split per-codepoint.
    [[nodiscard]] std::vector<Token> tokenize(const std::uint8_t* data, std::size_t len) const {
        std::vector<Token> tokens;
        if (len == 0) return tokens;

        tokens.reserve(len / 2);  // Conservative estimate

        std::size_t pos = 0;
        while (pos < len) {
            std::size_t start = pos;
            std::uint8_t first = data[pos];

            if (first < 0x80) {
                // ASCII byte
                if (is_ascii_word_char(first)) {
                    // ASCII word: group all consecutive ASCII word chars
                    while (pos < len && data[pos] < 0x80 && is_ascii_word_char(data[pos])) {
                        ++pos;
                    }
                } else if (is_ascii_whitespace(first)) {
                    // Whitespace run
                    while (pos < len && data[pos] < 0x80 && is_ascii_whitespace(data[pos])) {
                        ++pos;
                    }
                } else {
                    // ASCII punctuation/symbol: single character
                    ++pos;
                }
            } else {
                // Non-ASCII: decode one UTF-8 codepoint
                // Each non-ASCII codepoint is its own token
                pos += utf8_codepoint_length(first);
                if (pos > len) pos = len;  // Bounds check
            }

            tokens.push_back({data + start, pos - start});
        }

        return tokens;
    }

    [[nodiscard]] std::vector<Token> tokenize(const char* data, std::size_t len) const {
        return tokenize(reinterpret_cast<const std::uint8_t*>(data), len);
    }

    [[nodiscard]] std::vector<Token> tokenize(const std::string& s) const {
        return tokenize(s.data(), s.size());
    }

private:
    [[nodiscard]] static bool is_ascii_word_char(std::uint8_t c) {
        return (c >= 'a' && c <= 'z') ||
               (c >= 'A' && c <= 'Z') ||
               (c >= '0' && c <= '9') ||
               c == '_';
    }

    [[nodiscard]] static bool is_ascii_whitespace(std::uint8_t c) {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r';
    }

    /// Get UTF-8 codepoint length from first byte
    [[nodiscard]] static std::size_t utf8_codepoint_length(std::uint8_t first) {
        if ((first & 0x80) == 0x00) return 1;      // 0xxxxxxx - ASCII
        if ((first & 0xE0) == 0xC0) return 2;      // 110xxxxx
        if ((first & 0xF0) == 0xE0) return 3;      // 1110xxxx (CJK lives here)
        if ((first & 0xF8) == 0xF0) return 4;      // 11110xxx (emoji lives here)
        return 1;  // Invalid UTF-8, treat as single byte
    }
};

// Compatibility wrapper
struct Chunk {
    const std::uint8_t* data;
    std::size_t length;
    std::uint32_t repeat_count;
};

class HierarchicalChunker {
    UniversalTokenizer tokenizer_;
public:
    [[nodiscard]] std::vector<Chunk> chunk(const std::uint8_t* data, std::size_t len) const {
        auto tokens = tokenizer_.tokenize(data, len);
        std::vector<Chunk> chunks;
        chunks.reserve(tokens.size());
        for (const auto& t : tokens) {
            chunks.push_back({t.data, t.length, 1});
        }
        return chunks;
    }
};

} // namespace hartonomous
