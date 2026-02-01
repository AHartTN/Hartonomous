#pragma once

#include "ucd_models.hpp"
#include <string>
#include <vector>
#include <map>
#include <unordered_map>
#include <functional>

namespace Hartonomous::unicode {

class UCDParser {
public:
    UCDParser(const std::string& data_dir);

    /**
     * @brief Parse all required UCD files
     *
     * Primary: ucd.all.flat.xml (complete UCD data)
     * Supplementary: allkeys.txt (DUCET), confusables.txt, etc.
     */
    void parse_all();

    /**
     * @brief Parse only the main XML file (fastest path for full data)
     */
    void parse_xml();

    /**
     * @brief Get the parsed codepoints
     */
    const std::map<uint32_t, CodepointMetadata>& get_codepoints() const { return codepoints_; }

    /**
     * @brief Mutable access to parsed codepoints
     */
    std::map<uint32_t, CodepointMetadata>& get_codepoints_mutable() { return codepoints_; }

    /**
     * @brief Check if a codepoint is assigned (has UCD metadata)
     */
    bool is_assigned(uint32_t cp) const { return codepoints_.count(cp) > 0; }

    /**
     * @brief Get count of parsed codepoints
     */
    size_t codepoint_count() const { return codepoints_.size(); }

    /**
     * @brief Post-processing logic (can be called after loading from DB)
     */
    void find_base_characters();
    void build_semantic_edges();

private:
    // XML parsing (SAX-style for memory efficiency on 228MB file)
    void parse_ucd_xml(const std::string& path);
    void parse_char_element(const std::string& line);
    void set_attribute(CodepointMetadata& meta, const std::string& name, const std::string& value);

    // Supplementary file parsing
    void parse_all_keys();           // DUCET collation weights
    void parse_confusables();        // Confusable characters
    void parse_unihan();             // Han readings/radicals
    void parse_emoji_sequences();    // Emoji ZWJ sequences

    uint32_t trace_decomposition(uint32_t cp);

    std::string data_dir_;
    std::map<uint32_t, CodepointMetadata> codepoints_;
};

} // namespace Hartonomous::unicode
