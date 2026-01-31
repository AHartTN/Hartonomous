// UcdFileReader.hpp
#ifndef UCD_FILE_READER_HPP
#define UCD_FILE_READER_HPP

#include "IFileReader.hpp"
#include "UcdModels.hpp"
#include <string_view> // C++17 for efficient substring operations
#include <sstream>
#include <algorithm> // For std::remove_if
#include <cctype>    // For std::isspace

// Generic UCD file line preprocessor and splitter
class UcdLineParserBase {
protected:
    // Utility to split a string by delimiter
    std::vector<std::string> split(const std::string& s, char delimiter) const {
        std::vector<std::string> tokens;
        std::string token;
        std::istringstream tokenStream(s);
        while (std::getline(tokenStream, token, delimiter)) {
            tokens.push_back(token);
        }
        return tokens;
    }

    // Utility to trim whitespace from both ends of a string
    std::string trim(const std::string& str) const {
        size_t first = str.find_first_not_of(" 	\n\r\f\v");
        if (std::string::npos == first) {
            return ""; // Entire string is whitespace or empty
        }
        size_t last = str.find_last_not_of(" 	\n\r\f\v");
        return str.substr(first, (last - first + 1));
    }

public:
    // Processes a raw line, removing comments and trimming whitespace
    std::string preprocess_line(const std::string& line) const {
        size_t comment_pos = line.find('#');
        std::string processed_line = (comment_pos == std::string::npos) ? line : line.substr(0, comment_pos);
        return trim(processed_line);
    }
};

// Specific parser for UnicodeData.txt
class UnicodeDataParser : public UcdLineParserBase, public IDataParser<CodePoint> {
public:
    std::unique_ptr<CodePoint> parse_line(const std::string& preprocessed_line) const override;
};

// Specific parser for Blocks.txt
class BlocksParser : public UcdLineParserBase, public IDataParser<Block> {
public:
    std::unique_ptr<Block> parse_line(const std::string& preprocessed_line) const override;
};

// Specific parser for DerivedAge.txt
class DerivedAgeParser : public UcdLineParserBase, public IDataParser<Age> {
public:
    std::unique_ptr<Age> parse_line(const std::string& preprocessed_line) const override;
};


// Templated Concrete FileReader for UCD files
template <typename T>
class UcdFileReader : public IFileReader<T> {
private:
    std::ifstream m_file;
    std::unique_ptr<IDataParser<T>> m_parser;
    std::string m_next_raw_line_buffer; // Buffer for the raw line read from file
    bool m_has_more_data = false;

    // Helper to advance the buffer to the next non-empty, non-comment line
    void advance_buffer() {
        m_has_more_data = false;
        std::string current_raw_line;
        while (std::getline(m_file, current_raw_line)) {
            if (!m_parser->preprocess_line(current_raw_line).empty()) {
                m_next_raw_line_buffer = current_raw_line;
                m_has_more_data = true;
                return;
            }
        }
        // If loop finishes, no more valid lines
        m_next_raw_line_buffer.clear();
    }

public:
    UcdFileReader(std::unique_ptr<IDataParser<T>> parser) : m_parser(std::move(parser)) {}
    ~UcdFileReader() override { this->close(); } // Ensure close is called

    void open(const std::string& filepath) override {
        m_file.open(filepath);
        if (!m_file.is_open()) {
            throw std::runtime_error("Could not open file: " + filepath);
        }
        advance_buffer(); // Populate buffer with the first valid line
    }

    void close() override {
        if (m_file.is_open()) {
            m_file.close();
            m_has_more_data = false;
            m_next_raw_line_buffer.clear();
        }
    }

    bool has_next() const override {
        return m_has_more_data;
    }

    std::unique_ptr<T> read_next() override {
        if (!m_has_more_data) {
            return nullptr;
        }

        std::string line_to_parse = m_parser->preprocess_line(m_next_raw_line_buffer);
        std::unique_ptr<T> data_model = m_parser->parse_line(line_to_parse);

        advance_buffer(); // Load the next valid line into buffer for subsequent call
        return data_model;
    }

    std::vector<std::unique_ptr<T>> read_all() override {
        std::vector<std::unique_ptr<T>> all_models;
        while (has_next()) {
            all_models.push_back(read_next());
        }
        return all_models;
    }
};

#endif // UCD_FILE_READER_HPP