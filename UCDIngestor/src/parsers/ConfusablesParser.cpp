#include "ConfusablesParser.hpp"
#include <sstream>
#include <iostream>
#include <iomanip>
#include <algorithm>

namespace ucd {

ConfusablesParser::ConfusablesParser(const std::string& filepath) : m_filepath(filepath) {
    m_file.open(filepath);
}

ConfusablesParser::~ConfusablesParser() {
    if (m_file.is_open()) m_file.close();
}

bool ConfusablesParser::has_next() {
    return m_file.peek() != EOF;
}

// Helper: parse hex string to int
static int parse_hex(const std::string& s) {
    try {
        return std::stoi(s, nullptr, 16);
    } catch (...) {
        return 0;
    }
}

static std::string trim(const std::string& str) {
    size_t first = str.find_first_not_of(" \t");
    if (std::string::npos == first) return str;
    size_t last = str.find_last_not_of(" \t");
    return str.substr(first, (last - first + 1));
}

std::optional<Confusable> ConfusablesParser::next() {
    std::string line;
    while (std::getline(m_file, line)) {
        // Skip comments and empty lines
        size_t comment_pos = line.find('#');
        if (comment_pos != std::string::npos) {
            line = line.substr(0, comment_pos);
        }
        if (line.empty() || line.find_first_not_of(" \t") == std::string::npos) {
            continue;
        }

        // Format: 0041 ; 0061 ; MA 
        std::stringstream ss(line);
        std::string segment;
        std::vector<std::string> parts;
        while (std::getline(ss, segment, ';')) {
            parts.push_back(trim(segment));
        }

        if (parts.size() < 3) continue;

        Confusable item;
        item.source_codepoint = parse_hex(parts[0]);
        
        std::stringstream target_ss(parts[1]);
        std::string cp_hex;
        while (target_ss >> cp_hex) {
            item.target_codepoints.push_back(parse_hex(cp_hex));
        }
        
        item.type = parts[2];
        return item;
    }
    return std::nullopt;
}

} // namespace ucd
