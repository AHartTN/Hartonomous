// UcdFileReader.cpp
#include "UcdFileReader.hpp"
#include <sstream>
#include <iomanip> // For std::stoll with base 16
#include <stdexcept>
#include <algorithm> // For std::remove_if
#include <cctype>    // For std::isspace


// -----------------------------------------------------------------------------
// UnicodeDataParser Implementation
// -----------------------------------------------------------------------------
std::unique_ptr<CodePoint> UnicodeDataParser::parse_line(const std::string& preprocessed_line) const {
    std::vector<std::string> fields = split(preprocessed_line, ';');
    if (fields.size() < 15) { // UnicodeData.txt has 15 fields, plus potential optional ones
        throw std::runtime_error("Invalid line format for UnicodeData.txt: " + preprocessed_line);
    }

    auto cp = std::make_unique<CodePoint>();
    cp->code_point_id = trim(fields[0]);
    cp->name = trim(fields[1]);
    
    // General_Category (will be linked by FK after initial insertion)
    // Combining_Class (will be linked by FK)
    // Bidi_Class (will be linked by FK)
    // Decomposition_Mapping
    // Numeric_Type (will be linked by FK)
    // Numeric_Value_Decimal
    // Numeric_Value_Digit
    // Numeric_Value_Numeric
    // Bidi_Mirrored
    // Unicode_1_Name
    // ISO_Comment
    // Simple_Uppercase_Mapping
    // Simple_Lowercase_Mapping
    // Simple_Titlecase_Mapping

    // Populate fields. Use optional for potentially empty fields.
    // Fields[2] General_Category - needs lookup
    // Fields[3] Combining_Class - needs lookup
    // Fields[4] Bidi_Class - needs lookup
    if (!trim(fields[5]).empty()) cp->decomposition_mapping = trim(fields[5]);
    // Fields[6] Numeric_Type - needs lookup
    if (!trim(fields[7]).empty()) cp->numeric_value_decimal = std::stoll(trim(fields[7]));
    if (!trim(fields[8]).empty()) cp->numeric_value_digit = std::stoll(trim(fields[8]));
    if (!trim(fields[9]).empty()) cp->numeric_value_numeric = trim(fields[9]);
    
    cp->bidi_mirrored = (trim(fields[10]) == "Y");
    
    if (!trim(fields[11]).empty()) cp->unicode_1_name = trim(fields[11]);
    // fields[12] is deprecated and often empty, handled by PropertyAliases.txt for full history
    if (!trim(fields[13]).empty()) cp->simple_uppercase_mapping = trim(fields[13]);
    if (!trim(fields[14]).empty()) cp->simple_lowercase_mapping = trim(fields[14]);
    if (fields.size() > 15 && !trim(fields[15]).empty()) cp->simple_titlecase_mapping = trim(fields[15]);
    
    // Block and Age IDs will be resolved later in the ingestion process
    return cp;
}

// -----------------------------------------------------------------------------
// BlocksParser Implementation
// -----------------------------------------------------------------------------
std::unique_ptr<Block> BlocksParser::parse_line(const std::string& preprocessed_line) const {
    std::vector<std::string> parts = split(preprocessed_line, ';');
    if (parts.size() != 2) {
        throw std::runtime_error("Invalid line format for Blocks.txt: " + preprocessed_line);
    }

    std::string range_str = trim(parts[0]);
    std::string name = trim(parts[1]);

    std::vector<std::string> range_parts = split(range_str, '.');
    if (range_parts.size() != 2 || range_parts[0].length() != range_parts[1].length()) {
        throw std::runtime_error("Invalid range format in Blocks.txt: " + range_str);
    }
    
    return std::make_unique<Block>(trim(range_parts[0]), trim(range_parts[1]), name);
}

// -----------------------------------------------------------------------------
// DerivedAgeParser Implementation
// -----------------------------------------------------------------------------
std::unique_ptr<Age> DerivedAgeParser::parse_line(const std::string& preprocessed_line) const {
    // Expected format: Start Code..End Code; Version # Comment (optional)
    std::vector<std::string> parts = split(preprocessed_line, ';');
    if (parts.size() < 2) {
        throw std::runtime_error("Invalid line format for DerivedAge.txt: " + preprocessed_line);
    }

    std::string range_str = trim(parts[0]);
    std::string version = trim(parts[1]);
    std::optional<std::string> comment = std::nullopt;

    // Check for an additional comment part if present
    if (parts.size() > 2) {
        comment = trim(parts[2]); // For lines like `02C6;	1.1 #  [31] MODIFIER LETTER CIRCUMFLEX ACCENT`
    }

    std::vector<std::string> range_parts = split(range_str, '.');
    if (range_parts.size() != 2 || range_parts[0].length() != range_parts[1].length()) {
        throw std::runtime_error("Invalid range format in DerivedAge.txt: " + range_str);
    }
    
    return std::make_unique<Age>(trim(range_parts[0]), trim(range_parts[1]), version, comment);
}

