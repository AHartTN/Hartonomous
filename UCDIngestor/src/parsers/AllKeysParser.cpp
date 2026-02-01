#include "AllKeysParser.hpp"
#include <sstream>
#include <iostream>
#include <iomanip>
#include <algorithm>

namespace ucd {

AllKeysParser::AllKeysParser(const std::string& filepath) : m_filepath(filepath) {
    m_file.open(filepath);
}

AllKeysParser::~AllKeysParser() {
    if (m_file.is_open()) m_file.close();
}

bool AllKeysParser::has_next() {
    return m_file.peek() != EOF;
}

// Helper: split string by delimiter
static std::vector<std::string> split(const std::string& s, char delimiter) {
    std::vector<std::string> tokens;
    std::string token;
    std::istringstream tokenStream(s);
    while (std::getline(tokenStream, token, delimiter)) {
        tokens.push_back(token);
    }
    return tokens;
}

// Helper: parse hex string to int
static int parse_hex(const std::string& s) {
    try {
        return std::stoi(s, nullptr, 16);
    } catch (...) {
        return 0;
    }
}

std::optional<CollationWeight> AllKeysParser::next() {
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
        if (line[0] == '@') continue; // @version lines

        // Format: 0041 ; [.1C47.0020.0008] 
        // Part 1: Codepoints
        // Part 2: Weights
        
        auto parts = split(line, ';');
        if (parts.size() < 2) continue;

        CollationWeight weight;
        
        // 1. Parse Source Codepoints (space separated hex)
        std::stringstream ss_source(parts[0]);
        std::string cp_hex;
        while (ss_source >> cp_hex) {
            weight.source_codepoints.push_back(parse_hex(cp_hex));
        }

        // 2. Parse Weights
        // Format: [.PPPP.SSSS.TTTT] or [*PPPP.SSSS.TTTT]
        // Note: A line can have multiple collation elements: `[.A.B.C][.D.E.F]`
        // For simplicity in the "Gene Pool" MVP, we take the *first* collation element 
        // as the primary sorting key, or we need to normalize to a single tuple.
        // UCA defines lexicographical sort. Storing just the first is a simplification 
        // but useful for coarse sorting. 
        // HOWEVER, "Enterprise Grade" means we should handle the full sequence if possible.
        // But `collation_weights` table defined `primary`, `secondary`, `tertiary` as INTs (scalar).
        // If there are multiple CEs, it's an expansion. 
        // The most critical weight is the first one. Let's parse the first one found.
        
        std::string weights_part = parts[1];
        size_t bracket_start = weights_part.find('[');
        size_t bracket_end = weights_part.find(']');
        
        if (bracket_start != std::string::npos && bracket_end != std::string::npos) {
            std::string content = weights_part.substr(bracket_start + 1, bracket_end - bracket_start - 1);
            // content: .PPPP.SSSS.TTTT or *PPPP.SSSS.TTTT
            
            weight.is_variable = (content[0] == '*');
            
            // Remove dots and *
            std::replace(content.begin(), content.end(), '.', ' ');
            if (weight.is_variable) std::replace(content.begin(), content.end(), '*', ' ');
            
            std::stringstream ss_weights(content);
            std::string w_str;
            std::vector<int> w_vals;
            while (ss_weights >> w_str) {
                w_vals.push_back(parse_hex(w_str));
            }
            
            if (w_vals.size() >= 3) {
                weight.primary = w_vals[0];
                weight.secondary = w_vals[1];
                weight.tertiary = w_vals[2];
                return weight;
            }
        }
    }
    return std::nullopt;
}

}
