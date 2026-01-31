#ifndef IUCD_PARSER_HPP
#define IUCD_PARSER_HPP

#include <string>
#include <map>
#include <vector>
#include <functional>

// Interface for a generic UCD file parser.
// Implementations are responsible for reading specific file formats
// and invoking a callback for each parsed record.
class IUcdParser {
public:
    virtual ~IUcdParser() = default;

    // Parses the file at the given path.
    // The callback receives a map representing the parsed row (Key-Value).
    virtual void parse(const std::string& filepath, std::function<void(std::map<std::string, std::string>)> callback) = 0;
};

#endif // IUCD_PARSER_HPP
