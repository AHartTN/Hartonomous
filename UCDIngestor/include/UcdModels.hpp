#ifndef UCD_MODELS_HPP
#define UCD_MODELS_HPP

#include "IDataModel.hpp"
#include <string>
#include <map>
#include <vector>
#include <optional>
#include <sstream>
#include <iomanip>

// Helper to escape JSON strings for SQL
inline std::string escape_json_string(const std::string& input) {
    std::ostringstream ss;
    for (char c : input) {
        if (c == '"') ss << "\\\"";
        else if (c == '\\') ss << "\\\\";
        else if (c == '\b') ss << "\\b";
        else if (c == '\f') ss << "\\f";
        else if (c == '\n') ss << "\\n";
        else if (c == '\r') ss << "\\r";
        else if (c == '\t') ss << "\\t";
        else if ((unsigned char)c < 0x20) ss << "\\u" << std::hex << std::setw(4) << std::setfill('0') << (int)c;
        else ss << c;
    }
    return ss.str();
}

// -----------------------------------------------------------------------------
// The UcdRawCodepoint Model
// Represents a raw entry from the UCD XML.
// Distinct from Hartonomous::Atom to strictly separate Ingestion from Seeding.
// -----------------------------------------------------------------------------
struct UcdRawCodepoint {
    long long id;          // The Integer Codepoint (0-1114111)
    std::string hex;       // "0041"
    std::string name;      // "LATIN CAPITAL LETTER A"
    std::string gc;        // "Lu"
    std::string block;     // "Basic Latin"
    std::string age;       // "1.1"
    
    // Dynamic property bag (na1, sc, dm, etc.)
    std::map<std::string, std::string> properties;

    UcdRawCodepoint() : id(0) {}
};

/**
 * @brief Represents a raw Emoji Sequence from emoji-sequences.txt
 */
struct UcdEmojiSequence {
    std::vector<int> codepoints;
    std::string type_field;
    std::string description;
};

#endif // UCD_MODELS_HPP
