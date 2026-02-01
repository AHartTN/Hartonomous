#include <unicode/ingestor/ucd_parser.hpp>
#include <fstream>
#include <sstream>
#include <iostream>
#include <algorithm>

namespace Hartonomous::unicode {

UCDParser::UCDParser(const std::string& data_dir) : data_dir_(data_dir) {}

void UCDParser::parse_all() {
    // Parse assigned codepoints from UCD files
    // This keeps memory usage reasonable (~150k entries vs 1.1M)
    parse_unicode_data();
    parse_scripts();
    parse_blocks();
    parse_ages();
    parse_unihan_rs();
    parse_all_keys();
    find_base_characters();
}

void UCDParser::parse_unicode_data() {
    std::string path = data_dir_ + "/UnicodeData.txt";
    std::ifstream file(path);
    if (!file) {
        std::cerr << "Warning: Could not open " << path << "\n";
        return;
    }

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;

        std::stringstream ss(line);
        std::string field;
        std::vector<std::string> fields;
        while (std::getline(ss, field, ';')) {
            fields.push_back(field);
        }

        // UnicodeData.txt has 15 fields, but std::getline doesn't capture
        // trailing empty fields after the last ';'. Accept 14+ fields.
        if (fields.size() < 14) {
            std::cerr << "Field size mismatch in " << fields.size() << " fields: " << line << "\n";
            continue;
        } 

        uint32_t cp = std::stoul(fields[0], nullptr, 16);
        CodepointMetadata meta;
        meta.codepoint = cp;
        meta.name = fields[1];
        meta.general_category = fields[2];
        meta.combining_class = static_cast<uint8_t>(fields[3].empty() ? 0 : std::stoul(fields[3]));
        meta.decomposition = fields[5];

        codepoints_[cp] = std::move(meta);
    }
    std::cout << "Parsed " << codepoints_.size() << " codepoints from UnicodeData.txt\n";
}

void UCDParser::parse_scripts() {
    std::string path = data_dir_ + "/Scripts.txt";
    std::ifstream file(path);
    if (!file) return;

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;

        size_t semi = line.find(';');
        if (semi == std::string::npos) continue;

        std::string range_str = line.substr(0, semi);
        std::string script = line.substr(semi + 1);

        script.erase(0, script.find_first_not_of(" \t"));
        size_t hash = script.find('#');
        if (hash != std::string::npos) script.erase(hash);
        script.erase(script.find_last_not_of(" \t") + 1);

        size_t dots = range_str.find("..");
        if (dots != std::string::npos) {
            uint32_t start = std::stoul(range_str.substr(0, dots), nullptr, 16);
            uint32_t end = std::stoul(range_str.substr(dots + 2), nullptr, 16);
            for (uint32_t cp = start; cp <= end; ++cp) {
                if (codepoints_.count(cp)) codepoints_[cp].script = script;
            }
        } else {
            uint32_t cp = std::stoul(range_str, nullptr, 16);
            if (codepoints_.count(cp)) codepoints_[cp].script = script;
        }
    }
}

void UCDParser::parse_blocks() {
    std::string path = data_dir_ + "/Blocks.txt";
    std::ifstream file(path);
    if (!file) return;

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;

        size_t semi = line.find(';');
        if (semi == std::string::npos) continue;

        std::string range_str = line.substr(0, semi);
        std::string block = line.substr(semi + 1);
        block.erase(0, block.find_first_not_of(" \t"));
        block.erase(block.find_last_not_of(" \t") + 1);

        size_t dots = range_str.find("..");
        if (dots != std::string::npos) {
            uint32_t start = std::stoul(range_str.substr(0, dots), nullptr, 16);
            uint32_t end = std::stoul(range_str.substr(dots + 2), nullptr, 16);
            for (uint32_t cp = start; cp <= end; ++cp) {
                if (codepoints_.count(cp)) codepoints_[cp].block = block;
            }
        }
    }
}

void UCDParser::parse_ages() {
    std::string path = data_dir_ + "/DerivedAge.txt";
    std::ifstream file(path);
    if (!file) return;

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;

        size_t semi = line.find(';');
        if (semi == std::string::npos) continue;

        std::string range_str = line.substr(0, semi);
        std::string age = line.substr(semi + 1);
        age.erase(0, age.find_first_not_of(" \t"));
        size_t hash = age.find('#');
        if (hash != std::string::npos) age.erase(hash);
        age.erase(age.find_last_not_of(" \t") + 1);

        size_t dots = range_str.find("..");
        if (dots != std::string::npos) {
            uint32_t start = std::stoul(range_str.substr(0, dots), nullptr, 16);
            uint32_t end = std::stoul(range_str.substr(dots + 2), nullptr, 16);
            for (uint32_t cp = start; cp <= end; ++cp) {
                if (codepoints_.count(cp)) codepoints_[cp].age = age;
            }
        } else {
            uint32_t cp = std::stoul(range_str, nullptr, 16);
            if (codepoints_.count(cp)) codepoints_[cp].age = age;
        }
    }
}

void UCDParser::find_base_characters() {
    for (auto& pair : codepoints_) {
        pair.second.base_codepoint = trace_decomposition(pair.first);
    }
}

uint32_t UCDParser::trace_decomposition(uint32_t cp) {
    if (!codepoints_.count(cp)) return cp;
    const std::string& decomp = codepoints_[cp].decomposition;
    if (decomp.empty()) return cp;

    std::stringstream ss(decomp);
    std::string part;
    uint32_t first_cp = 0;
    while (ss >> part) {
        if (part[0] == '<') continue;
        try {
            first_cp = std::stoul(part, nullptr, 16);
            break;
        } catch (...) { continue; }
    }

    if (first_cp == 0 || first_cp == cp) return cp;
    return trace_decomposition(first_cp);
}

void UCDParser::parse_unihan_rs() {
    std::string path = data_dir_ + "/Unihan_RadicalStrokeCounts.txt";
    std::ifstream file(path);
    if (!file) return;

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '#') continue;

        std::stringstream ss(line);
        std::string cp_str, field, value;
        if (!(ss >> cp_str >> field >> value)) continue;

        if (field == "kRSUnicode") {
            try {
                uint32_t cp = std::stoul(cp_str.substr(2), nullptr, 16);
                if (codepoints_.count(cp)) {
                    size_t dot = value.find('.');
                    if (dot != std::string::npos) {
                        std::string rad_str = value.substr(0, dot);
                        if (!rad_str.empty() && rad_str.back() == '\'') rad_str.pop_back();
                        codepoints_[cp].radical = std::stoul(rad_str);
                        codepoints_[cp].strokes = std::stoi(value.substr(dot + 1));
                    }
                }
            } catch (...) { continue; }
        }
    }
}

void UCDParser::parse_all_keys() {
    std::string path = data_dir_ + "/allkeys.txt";
    std::ifstream file(path);
    if (!file) return;

    std::string line;
    while (std::getline(file, line)) {
        if (line.empty() || line[0] == '@' || line[0] == '#') continue;

        size_t semi = line.find(';');
        if (semi == std::string::npos) continue;

        std::string cp_str = line.substr(0, semi);
        cp_str.erase(0, cp_str.find_first_not_of(" \t"));
        cp_str.erase(cp_str.find_last_not_of(" \t") + 1);

        if (cp_str.find(' ') != std::string::npos) continue;

        try {
            uint32_t cp = std::stoul(cp_str, nullptr, 16);
            if (codepoints_.count(cp)) {
                size_t start = line.find('[', semi);
                while (start != std::string::npos) {
                    size_t dot1 = line.find('.', start);
                    size_t dot2 = line.find('.', dot1 + 1);
                    size_t dot3 = line.find('.', dot2 + 1);
                    size_t end = line.find(']', dot3 + 1);

                    if (dot1 != std::string::npos && end != std::string::npos) {
                        UCAWeights weights;
                        weights.primary = std::stoul(line.substr(start + 2, dot1 - start - 2), nullptr, 16);
                        weights.secondary = std::stoul(line.substr(dot1 + 1, dot2 - dot1 - 1), nullptr, 16);
                        weights.tertiary = std::stoul(line.substr(dot2 + 1, dot3 - dot2 - 1), nullptr, 16);
                        codepoints_[cp].uca_elements.push_back(weights);
                    }
                    start = line.find('[', end);
                }
            }
        } catch (...) { continue; }
    }
}

} // namespace Hartonomous::unicode
