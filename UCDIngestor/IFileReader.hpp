// IFileReader.hpp
#ifndef IFILE_READER_HPP
#define IFILE_READER_HPP

#include <string>
#include <vector>
#include <memory>
#include <fstream>
#include <stdexcept>
#include <string_view> // C++17 for efficient substring operations

// Abstract base class for any UCD data entity parser
// T represents the specific DataModel type (e.g., CodePoint, Block)
template <typename T>
class IDataParser {
public:
    virtual ~IDataParser() = default;
    // Preprocesses a raw line (e.g., removes comments, trims whitespace)
    virtual std::string preprocess_line(const std::string& line) const = 0;
    // Parses a preprocessed line into a DataModel object
    virtual std::unique_ptr<T> parse_line(const std::string& preprocessed_line) const = 0;
};

// Abstract base class for file readers
// T represents the specific DataModel type that the reader produces
template <typename T>
class IFileReader {
public:
    virtual ~IFileReader() = default;
    virtual void open(const std::string& filepath) = 0;
    virtual void close() = 0;
    virtual bool has_next() const = 0;
    virtual std::unique_ptr<T> read_next() = 0; // Read and parse next line
    virtual std::vector<std::unique_ptr<T>> read_all() = 0; // Convenience method
};

#endif // IFILE_READER_HPP
