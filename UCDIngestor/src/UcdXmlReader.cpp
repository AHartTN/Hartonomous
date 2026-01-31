#include "UcdXmlReader.hpp"
#include <iostream>
#include <stdexcept>
#include <sstream>
#include <algorithm>
#include <iomanip>

UcdXmlReader::UcdXmlReader(const std::string& filepath) 
    : m_filepath(filepath) {}

UcdXmlReader::~UcdXmlReader() {
    close();
}

void UcdXmlReader::open() {
    m_file.open(m_filepath);
    if (!m_file.is_open()) {
        throw std::runtime_error("Failed to open XML file: " + m_filepath);
    }
}

void UcdXmlReader::close() {
    if (m_file.is_open()) {
        m_file.close();
    }
}

// Simple Helper: Convert Hex String to Long Long
static long long hex_to_int_local(const std::string& hex_str) {
    if (hex_str.empty()) return 0;
    try {
        return std::stoll(hex_str, nullptr, 16);
    } catch (...) {
        return 0;
    }
}

static std::string int_to_hex_string(long long val) {
    std::stringstream ss;
    ss << std::uppercase << std::hex << std::setw(4) << std::setfill('0') << val;
    return ss.str();
}

std::string UcdXmlReader::get_attribute(const std::string& content, const std::string& attr_name) {
    std::string key = attr_name + "=\"";
    size_t start = content.find(key);
    if (start == std::string::npos) return "";

    start += key.length();
    size_t end = content.find("\"", start);
    if (end == std::string::npos) return "";

    return content.substr(start, end - start);
}

void UcdXmlReader::parse_attributes(const std::string& line, Atom& atom) {
    // We iterate the string looking for `key="value"` patterns.
    size_t pos = 0;
    while (true) {
        size_t eq_pos = line.find("=\"", pos);
        if (eq_pos == std::string::npos) break;
        
        // Find start of key (search backwards from equals sign)
        // XML attributes are separated by space.
        // E.g. <char cp="0041" age="1.1" ...
        size_t key_end = eq_pos;
        size_t key_start = line.rfind(" ", key_end);
        
        // Handle case where it's the first attribute after tag name
        // e.g. <char cp="..."
        if (key_start == std::string::npos) {
            // Should not happen if we search from pos, but safety check.
            // Maybe find start of tag?
            size_t tag_start = line.rfind("<", key_end);
            if (tag_start != std::string::npos) {
                // Find space after tag name
                size_t space = line.find(" ", tag_start);
                if (space != std::string::npos && space < key_end) {
                    key_start = space;
                }
            }
        }
        
        if (key_start != std::string::npos) {
            std::string key = line.substr(key_start + 1, key_end - (key_start + 1));
            
            // Get Value
            size_t val_start = eq_pos + 2;
            size_t val_end = line.find("\"", val_start);
            if (val_end != std::string::npos) {
                std::string val = line.substr(val_start, val_end - val_start);
                
                // Store EVERY attribute.
                // We do NOT filter anything here. The Atom model will handle DB mapping.
                // We exclude 'cp', 'first-cp', 'last-cp' from properties map 
                // ONLY because they define the Atom's identity, which we set explicitly.
                if (key != "cp" && key != "first-cp" && key != "last-cp") {
                    atom.properties[key] = val;
                }
                
                pos = val_end + 1;
            } else {
                pos = eq_pos + 1; 
            }
        } else {
            pos = eq_pos + 1;
        }
    }
}

std::optional<Atom> UcdXmlReader::next_atom() {
    // 1. If we are in the middle of expanding a range, yield the next item.
    if (m_expanding_range) {
        if (m_current_range_cursor <= m_current_range_end) {
            Atom atom = m_pending_range_template; // Copy template
            atom.id = m_current_range_cursor;
            atom.hex = int_to_hex_string(atom.id);
            // Name often needs to be unique or derived.
            // In UCD XML ranges, the 'na' attribute is usually distinct or marked with '#'
            // but for CJK ranges it might be "CJK UNIFIED IDEOGRAPH-#".
            // We keep the template name. The 'name' field in Atom struct is just a string.
            // If it contains #, we could replace it with hex, but let's keep it raw as requested.
            
            m_current_range_cursor++;
            return atom;
        } else {
            m_expanding_range = false; // Range finished
        }
    }

    // 2. Read next line from file
    if (!m_file.is_open()) return std::nullopt;

    std::string line;
    while (std::getline(m_file, line)) {
        // Trim whitespace
        size_t first = line.find_first_not_of(" 	");
        if (first == std::string::npos) continue;
        
        // Detect relevant tags
        bool is_char = (line.compare(first, 6, "<char ") == 0);
        bool is_reserved = (line.compare(first, 10, "<reserved ") == 0);
        bool is_nonchar = (line.compare(first, 14, "<noncharacter ") == 0);
        bool is_surrogate = (line.compare(first, 11, "<surrogate ") == 0);

        if (is_char || is_reserved || is_nonchar || is_surrogate) {
            return process_line(line);
        }
        
        if (line.find("</repertoire>") != std::string::npos) {
            return std::nullopt; // End of data
        }
    }
    return std::nullopt;
}

std::optional<Atom> UcdXmlReader::process_line(const std::string& line) {
    Atom atom;
    
    // Check for Range attributes first
    std::string first_cp = get_attribute(line, "first-cp");
    std::string last_cp = get_attribute(line, "last-cp");
    
    if (!first_cp.empty() && !last_cp.empty()) {
        // It is a range.
        m_expanding_range = true;
        m_current_range_start = hex_to_int_local(first_cp);
        m_current_range_end = hex_to_int_local(last_cp);
        m_current_range_cursor = m_current_range_start;
        
        // Parse attributes once into the template
        // Explicit hot columns
        atom.name = get_attribute(line, "na");
        atom.block = get_attribute(line, "blk");
        atom.gc = get_attribute(line, "gc");
        atom.age = get_attribute(line, "age");
        
        parse_attributes(line, atom);
        
        m_pending_range_template = atom; // Save template
        
        // Yield first item immediately
        atom.id = m_current_range_cursor;
        atom.hex = int_to_hex_string(atom.id);
        m_current_range_cursor++;
        return atom;
    } 
    
    // Not a range, single code point
    std::string cp_str = get_attribute(line, "cp");
    if (cp_str.empty()) {
        // Should not happen for valid tags unless malformed
        return std::nullopt; 
    }

    atom.hex = cp_str;
    atom.id = hex_to_int_local(cp_str);
    atom.name = get_attribute(line, "na");
    atom.block = get_attribute(line, "blk");
    atom.gc = get_attribute(line, "gc");
    atom.age = get_attribute(line, "age");
    
    parse_attributes(line, atom);

    return atom;
}