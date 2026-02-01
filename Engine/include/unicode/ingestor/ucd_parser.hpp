#pragma once

#include "ucd_models.hpp"
#include <string>
#include <vector>
#include <map>
#include <unordered_map>

namespace Hartonomous::unicode {

class UCDParser {
public:
    UCDParser(const std::string& data_dir);

    /**
     * @brief Parse all required UCD files
     * 
     * Parses UnicodeData.txt, Scripts.txt, allkeys.txt, etc.
     */
    void parse_all();

    /**
     * @brief Get the parsed codepoints
     */
    const std::vector<CodepointMetadata>& get_codepoints() const { return codepoints_; }

private:
    void generate_full_codespace();  // All 1,114,112 codepoints
    void parse_unicode_data();
    void parse_scripts();
    void parse_blocks();
    void parse_ages();
    void parse_unihan_rs();
    void parse_all_keys(); // DUCET

    void find_base_characters();
    uint32_t trace_decomposition(uint32_t cp);

    std::string data_dir_;
    std::vector<CodepointMetadata> codepoints_;
};

} // namespace Hartonomous::unicode
