#ifndef UCD_XML_READER_HPP
#define UCD_XML_READER_HPP

#include "IUcdSource.hpp"
#include <string>
#include <fstream>
#include <vector>
#include <optional>
#include <map>

// A high-performance, streaming XML reader for UCD flat XML.
// Handles both single char tags (<char cp="...") and range tags (<char first-cp="..." last-cp="...").
// Expands ranges into individual Atom objects to ensure granular node representation.
class UcdXmlReader : public IUcdSource {
private:
    std::string m_filepath;
    std::ifstream m_file;
    
    // Range expansion state
    bool m_expanding_range = false;
    long long m_current_range_start = 0;
    long long m_current_range_end = 0;
    long long m_current_range_cursor = 0;
    Atom m_pending_range_template; // Properties shared by all atoms in the current range

    // Internal helper to parse a line and determine if it's a single atom or start of a range
    std::optional<Atom> process_line(const std::string& line);
    
    // Helper to extract attribute value
    std::string get_attribute(const std::string& content, const std::string& attr_name);

    // Parse all attributes into the atom's property map
    void parse_attributes(const std::string& line, Atom& atom);

public:
    UcdXmlReader(const std::string& filepath);
    ~UcdXmlReader();

    void open() override;
    std::optional<Atom> next_atom() override;
    void close() override;
};

#endif // UCD_XML_READER_HPP