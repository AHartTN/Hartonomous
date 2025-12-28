#pragma once

/// VOCABULARY PARSING - Token extraction from various formats
///
/// Supports:
/// - vocab.txt: Simple line-per-token format (preferred)
/// - tokenizer.json / vocab.json: JSON vocabulary mappings

#include <string>
#include <vector>
#include <cstdint>

namespace hartonomous::model {

/// Parsed token from vocabulary file (before ingestion)
struct ParsedToken {
    std::uint32_t id;
    std::string text;
    // NodeRef stored separately after ingestion
};

/// Parsed vocabulary result
struct ParsedVocab {
    std::vector<ParsedToken> tokens;
    
    [[nodiscard]] std::size_t size() const { return tokens.size(); }
    [[nodiscard]] bool empty() const { return tokens.empty(); }
    
    ParsedToken& operator[](std::size_t i) { return tokens[i]; }
    const ParsedToken& operator[](std::size_t i) const { return tokens[i]; }
};

/// Vocabulary parser - extracts tokens from various file formats
class VocabParser {
public:
    /// Parse vocab.txt format (one token per line)
    [[nodiscard]] static ParsedVocab parse_vocab_txt(const std::string& content) {
        ParsedVocab result;
        std::uint32_t id = 0;
        std::size_t pos = 0;

        // Estimate line count for reserve (average token ~8 chars + newline)
        result.tokens.reserve(content.size() / 9 + 100);

        while (pos < content.size()) {
            std::size_t end = content.find('\n', pos);
            if (end == std::string::npos) end = content.size();

            std::string line = content.substr(pos, end - pos);

            // Remove carriage return if present
            if (!line.empty() && line.back() == '\r') {
                line.pop_back();
            }

            if (!line.empty()) {
                result.tokens.push_back({id++, std::move(line)});
            }

            pos = end + 1;
        }

        return result;
    }

    /// Parse JSON vocabulary (tokenizer.json or vocab.json format)
    [[nodiscard]] static ParsedVocab parse_vocab_json(const std::string& content) {
        ParsedVocab result;

        // Look for "vocab" or "model" -> "vocab" section
        std::size_t vocab_pos = content.find("\"vocab\"");
        if (vocab_pos == std::string::npos) {
            // Try direct key-value pairs at root
            vocab_pos = content.find('{');
        } else {
            vocab_pos = content.find('{', vocab_pos);
        }

        if (vocab_pos == std::string::npos) return result;

        // Simple JSON key-value extraction: "token": id
        std::size_t pos = vocab_pos;
        while (pos < content.size()) {
            // Find next string key
            std::size_t key_start = content.find('"', pos);
            if (key_start == std::string::npos) break;
            key_start++;

            std::size_t key_end = content.find('"', key_start);
            if (key_end == std::string::npos) break;

            std::string key = content.substr(key_start, key_end - key_start);

            // Skip non-vocabulary keys
            if (is_metadata_key(key)) {
                pos = key_end + 1;
                continue;
            }

            // Find colon and value
            std::size_t colon = content.find(':', key_end);
            if (colon == std::string::npos) break;

            // Skip whitespace
            std::size_t val_start = colon + 1;
            while (val_start < content.size() && is_whitespace(content[val_start])) {
                val_start++;
            }

            // Check if value is a number (token ID)
            if (val_start < content.size() && is_digit(content[val_start])) {
                std::size_t val_end = val_start;
                while (val_end < content.size() && is_digit(content[val_end])) {
                    val_end++;
                }

                std::uint32_t id = static_cast<std::uint32_t>(
                    std::stoul(content.substr(val_start, val_end - val_start)));

                // Unescape token text
                std::string token_text = unescape_json_string(key);

                // Ensure result is large enough
                if (id >= result.tokens.size()) {
                    result.tokens.resize(id + 1);
                }
                result.tokens[id] = {id, std::move(token_text)};

                pos = val_end;
            } else {
                pos = val_start + 1;
            }
        }

        // Remove empty slots
        result.tokens.erase(
            std::remove_if(result.tokens.begin(), result.tokens.end(),
                [](const ParsedToken& t) { return t.text.empty(); }),
            result.tokens.end());

        return result;
    }

    /// Unescape JSON string (handles \n, \t, \uXXXX, etc.)
    [[nodiscard]] static std::string unescape_json_string(const std::string& s) {
        std::string result;
        result.reserve(s.size());

        for (std::size_t i = 0; i < s.size(); ++i) {
            if (s[i] == '\\' && i + 1 < s.size()) {
                char next = s[i + 1];
                switch (next) {
                    case 'n':  result += '\n'; ++i; break;
                    case 'r':  result += '\r'; ++i; break;
                    case 't':  result += '\t'; ++i; break;
                    case '"':  result += '"';  ++i; break;
                    case '\\': result += '\\'; ++i; break;
                    case 'u':
                        if (i + 5 < s.size()) {
                            std::string hex = s.substr(i + 2, 4);
                            try {
                                int cp = std::stoi(hex, nullptr, 16);
                                append_utf8(result, cp);
                                i += 5;
                            } catch (...) {
                                result += s[i];
                            }
                        } else {
                            result += s[i];
                        }
                        break;
                    default:
                        result += s[i];
                        break;
                }
            } else {
                result += s[i];
            }
        }

        return result;
    }

private:
    /// Check if key is a metadata key (not a token)
    [[nodiscard]] static bool is_metadata_key(const std::string& key) {
        static const char* metadata_keys[] = {
            "vocab", "model", "version", "truncation", "padding",
            "added_tokens", "normalizer", "pre_tokenizer", "post_processor",
            "decoder", "unk_token", "bos_token", "eos_token", "pad_token"
        };
        for (const char* mk : metadata_keys) {
            if (key == mk) return true;
        }
        return false;
    }

    [[nodiscard]] static bool is_whitespace(char c) {
        return c == ' ' || c == '\t' || c == '\n' || c == '\r';
    }

    [[nodiscard]] static bool is_digit(char c) {
        return c >= '0' && c <= '9';
    }

    /// Append UTF-8 encoded codepoint to string
    static void append_utf8(std::string& result, int cp) {
        if (cp < 0x80) {
            result += static_cast<char>(cp);
        } else if (cp < 0x800) {
            result += static_cast<char>(0xC0 | (cp >> 6));
            result += static_cast<char>(0x80 | (cp & 0x3F));
        } else {
            result += static_cast<char>(0xE0 | (cp >> 12));
            result += static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
            result += static_cast<char>(0x80 | (cp & 0x3F));
        }
    }
};

} // namespace hartonomous::model
