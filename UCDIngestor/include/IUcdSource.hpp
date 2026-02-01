#ifndef IUCD_SOURCE_HPP
#define IUCD_SOURCE_HPP

#include "UcdModels.hpp"
#include <optional>

// Interface for any UCD Data Source (XML, Text, Network, etc.)
// Uses a generator/iterator pattern to yield Atoms one by one.
class IUcdSource {
public:
    virtual ~IUcdSource() = default;

    // Prepare the source (open file, connect, etc.)
    virtual void open() = 0;

    // Get the next UcdRawCodepoint. Returns std::nullopt when exhausted.
    virtual std::optional<UcdRawCodepoint> next_atom() = 0;

    // Close and cleanup
    virtual void close() = 0;
};

#endif // IUCD_SOURCE_HPP
