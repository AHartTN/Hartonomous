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
// The Atom Model
// Represents a single Unicode Codepoint as a deterministic node.
// -----------------------------------------------------------------------------
struct Atom : public IDataModel {
    long long id;          // Integer codepoint value (PK)
    std::string hex;       // Hex string (e.g., "0041")
    std::string name;      // Name attribute
    std::string scalar;    // Printable char or NULL
    std::string block;     // Block attribute
    std::string gc;        // General Category
    std::string age;       // Version age
    
    // Key-Value pairs for all other properties to be stored in JSONB
    std::map<std::string, std::string> properties;

    Atom() : id(0) {}

    std::string get_table_name() const override { return "atoms"; }
    
    std::string get_primary_key_column() const override { return "id"; }
    
    std::string get_primary_key_value() const override { return std::to_string(id); }

    std::map<std::string, std::string> to_db_map() const override {
        std::map<std::string, std::string> map;
        map["id"] = std::to_string(id);
        
        // Handle Scalar (NULL if empty/control)
        // We return the raw string or empty. The DB layer handles quoting/NULLs.
        // If it's effectively empty/null, we pass empty string? No, we need to signal NULL.
        // But map<string,string> can't easily signal NULL vs "NULL" string vs empty string.
        // Let's use a magic value or just empty string = NULL?
        // Scalar is char(1) or text.
        // Let's pass the raw string. If it is empty, we might want to skip it or pass empty.
        // But wait, PgConnection logic I just wrote calls `quote()` on everything.
        // `quote("")` -> `''`.
        // If we want NULL, we need a way to pass it.
        // Let's keep it simple: Raw value. Empty string means empty string in DB (not NULL).
        // If we want NULL, we need to pass a sentinel or handle it in PgConnection.
        // For 'scalar', standard UCD often leaves it empty for non-printable.
        map["scalar"] = scalar; 
        
        map["name"] = name;
        map["block"] = block;
        map["general_category"] = gc;
        map["age"] = age;

        // Build JSONB string
        std::ostringstream json;
        json << "{";
        bool first = true;
        for (const auto& pair : properties) {
            if (!first) json << ", ";
            json << "\"" << escape_json_string(pair.first) << "\": \"" << escape_json_string(pair.second) << "\"";
            first = false;
        }
        json << "}";
        map["metadata"] = json.str();

        return map;
    }

    std::vector<std::string> get_update_columns() const override {
        // In case we re-ingest, update everything except ID
        return {"name", "scalar", "block", "general_category", "age", "metadata"};
    }
};

#endif // UCD_MODELS_HPP
