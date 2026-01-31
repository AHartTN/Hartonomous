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
    
    // Raw fields for FK lookup
    cp->general_category_code_raw = trim(fields[2]);
    cp->combining_class_value_raw = std::stoi(trim(fields[3]));
    cp->bidi_class_code_raw = trim(fields[4]);
    // Numeric Type (Field 6)
    std::string numeric_type_str = trim(fields[6]);
    if (!numeric_type_str.empty()) {
        cp->numeric_type_raw = numeric_type_str;
    }


    // Directly insertable fields (with optional handling)
    if (!trim(fields[5]).empty()) cp->decomposition_mapping = trim(fields[5]);
    if (!trim(fields[7]).empty()) cp->numeric_value_decimal = std::stoll(trim(fields[7]));
    if (!trim(fields[8]).empty()) cp->numeric_value_digit = std::stoll(trim(fields[8]));
    if (!trim(fields[9]).empty()) cp->numeric_value_numeric = trim(fields[9]);
    
    cp->bidi_mirrored = (trim(fields[10]) == "Y");
    
    if (!trim(fields[11]).empty()) cp->unicode_1_name = trim(fields[11]);
    // fields[12] is ISO_Comment
    if (!trim(fields[12]).empty()) cp->iso_comment = trim(fields[12]);
    if (!trim(fields[13]).empty()) cp->simple_uppercase_mapping = trim(fields[13]);
    if (!trim(fields[14]).empty()) cp->simple_lowercase_mapping = trim(fields[14]);
    if (fields.size() > 15 && !trim(fields[15]).empty()) cp->simple_titlecase_mapping = trim(fields[15]);
    
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
    if (range_parts.size() != 2) {
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

    std::string range_and_version_str = trim(parts[0]);
    std::string version = trim(parts[1]); // This field contains version like "1.1", "2.0"
    std::optional<std::string> comment = std::nullopt;

    // The line format from DerivedAge.txt is "0000..001F    ; 1.1 #  [32] <control-0000>..<control-001F>"
    // So the second field after splitting by ';' is the version. The comment is after '#' in the original line.
    // The preprocess_line already removed the '# Comment' part.
    // The version string itself might contain comments within (e.g., "1.1 # comment part") if not processed carefully.
    // But `preprocess_line` should have already stripped everything after '#'.
    // So `version` should be clean.

    std::vector<std::string> range_parts = split(range_and_version_str, '.'); // Split "0000..001F"
    if (range_parts.size() != 2) { // Should be exactly two parts: start and end
        // If there's only one part, it means the range is not "X..Y" format, maybe just a single code point like "20AC"
        // This parser expects X..Y format.
        // For actual UCD's DerivedAge.txt, ranges are always X..Y
         throw std::runtime_error("Invalid range format in DerivedAge.txt: " + range_and_version_str);
    }
    
    // In DerivedAge.txt, the format is "Start..End; Age [ # Comment ]"
    // So `parts[0]` is "Start..End", `parts[1]` is "Age".
    // Let's re-parse `preprocessed_line` more carefully for DerivedAge.txt.
    
    // Re-split `preprocessed_line` which would be something like "0000..001F; 1.1"
    std::vector<std::string> line_parts = split(preprocessed_line, ';');
    if (line_parts.size() < 2) {
         throw std::runtime_error("Invalid line format for DerivedAge.txt (after preprocess): " + preprocessed_line);
    }

    range_str = trim(line_parts[0]);
    version = trim(line_parts[1]); // This is the cleaned version string

    range_parts = split(range_str, '.');
    if (range_parts.size() != 2) {
        throw std::runtime_error("Invalid range format in DerivedAge.txt: " + range_str);
    }

    // In DerivedAge.txt, the comment is after the version like `1.1 # [32] ...`
    // So, if we need the original comment, it needs to be extracted before `preprocess_line` or separately.
    // For now, `comment` is from `std::optional<std::string> c = std::nullopt` in constructor.
    // The current `preprocessed_line` will NOT contain the comment after '#'.
    // The `Age` model constructor supports an optional comment, but the parser here
    // doesn't have access to the original raw line's comment. If needed, the parser
    // signature needs to change or the `UcdFileReader` needs to pass the raw comment.
    // For now, `comment` field in `Age` object will remain empty from this parser.
    
    return std::make_unique<Age>(trim(range_parts[0]), trim(range_parts[1]), version);
}
