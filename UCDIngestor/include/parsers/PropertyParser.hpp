#ifndef PROPERTY_PARSER_HPP
#define PROPERTY_PARSER_HPP

#include "IUcdParser.hpp"
#include <fstream>
#include <sstream>
#include <iostream>
#include <vector>

// Handles standard UCD "Code ; Property ; Value" and Unihan "Code 	 Property 	 Value" files.
class PropertyParser : public IUcdParser {
public:
    void parse(const std::string& filepath, std::function<void(std::map<std::string, std::string>)> callback) override {
        std::ifstream file(filepath);
        if (!file.is_open()) return;

        std::string line;
        while (std::getline(file, line)) {
            size_t hash = line.find('#');
            if (hash != std::string::npos) line = line.substr(0, hash);
            if (line.empty()) continue;

            // Detect separator (Tab for Unihan, Semicolon for others)
            char sep = (line.find('\t') != std::string::npos && line.find(';') == std::string::npos) ? '\t' : ';';

            std::vector<std::string> parts;
            std::stringstream ss(line);
            std::string segment;
            
            size_t start = 0, end = 0;
            while ((end = line.find(sep, start)) != std::string::npos) {
                parts.push_back(line.substr(start, end - start));
                start = end + 1;
            }
            parts.push_back(line.substr(start));

            if (parts.empty()) continue;

            // Trim
            for (auto& p : parts) {
                p.erase(0, p.find_first_not_of(" 	"));
                p.erase(p.find_last_not_of(" 	") + 1);
            }

            if (parts.size() < 2) continue;

            // Standardize output
            std::map<std::string, std::string> row;
            row["range"] = parts[0]; // Parser leaves range expansion to the Ingestor or handles it? 
                                     // "Separation of concerns": Parser parses, Ingestor logic processes.
                                     // But returning "range" string implies the Ingestor must parse it again.
                                     // I'll return "start_cp" and "end_cp" here for convenience.
            
            parse_range(parts[0], row);

            // Heuristic for Property/Value
            // Unihan: Field 1 is Property, Field 2 is Value
            // PropList: Field 1 is Property (Value=True implicitly usually)
            // But sometimes Field 1 is Value if the File is the Property.
            // This ambiguity requires context or configuration. 
            // For now, I'll return raw parts and let the specific Strategy handle mapping.
            
            row["raw_p1"] = parts.size() > 1 ? parts[1] : "";
            row["raw_p2"] = parts.size() > 2 ? parts[2] : "";
            
            callback(row);
        }
    }

private:
    void parse_range(const std::string& range_str, std::map<std::string, std::string>& row) {
        size_t dots = range_str.find("..");
        if (dots != std::string::npos) {
            row["start_cp"] = range_str.substr(0, dots);
            row["end_cp"] = range_str.substr(dots + 2);
        } else {
            row["start_cp"] = range_str;
            row["end_cp"] = range_str;
        }
    }
};

#endif // PROPERTY_PARSER_HPP
