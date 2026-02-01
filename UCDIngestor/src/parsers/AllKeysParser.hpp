#pragma once
#include <string>
#include <vector>
#include <optional>
#include <fstream>

namespace ucd {

struct CollationWeight {
    std::vector<int> source_codepoints;
    int primary;
    int secondary;
    int tertiary;
    bool is_variable;
};

class AllKeysParser {
public:
    explicit AllKeysParser(const std::string& filepath);
    ~AllKeysParser();

    bool has_next();
    std::optional<CollationWeight> next();

private:
    std::ifstream m_file;
    std::string m_filepath;
};

}
