#pragma once
#include <string>
#include <vector>
#include <optional>
#include <fstream>

namespace ucd {

struct Confusable {
    int source_codepoint;
    std::vector<int> target_codepoints;
    std::string type; // MA, L, etc.
};

class ConfusablesParser {
public:
    explicit ConfusablesParser(const std::string& filepath);
    ~ConfusablesParser();

    bool has_next();
    std::optional<Confusable> next();

private:
    std::ifstream m_file;
    std::string m_filepath;
};

}
