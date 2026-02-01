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
     * Only parses ASSIGNED codepoints to keep memory usage reasonable.
     */
    void parse_all();

    /**
     * @brief Get the parsed codepoints (assigned only)
     */
    const std::map<uint32_t, CodepointMetadata>& get_codepoints() const { return codepoints_; }

    /**
     * @brief Mutable access to parsed codepoints.
     *
     * This accessor is provided to allow downstream processors to
     * annotate and mutate metadata (sequence indices, positions, etc.)
     * without copying the entire map. Callers must ensure they do not
     * modify the map structure (insert/erase) while iterating concurrently.
     */
    std::map<uint32_t, CodepointMetadata>& get_codepoints_mutable() { return codepoints_; }

    /**
     * @brief Check if a codepoint is assigned (has UCD metadata)
     */
    bool is_assigned(uint32_t cp) const { return codepoints_.count(cp) > 0; }

private:
    void parse_unicode_data();
    void parse_scripts();
    void parse_blocks();
    void parse_ages();
    void parse_unihan_rs();
    void parse_all_keys(); // DUCET

    void find_base_characters();
    uint32_t trace_decomposition(uint32_t cp);

    std::string data_dir_;
    std::map<uint32_t, CodepointMetadata> codepoints_;
};

} // namespace Hartonomous::unicode
