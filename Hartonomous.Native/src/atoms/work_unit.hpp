#pragma once

#include "node_ref.hpp"
#include <cstdint>
#include <utility>
#include <vector>

namespace hartonomous {

/// Work unit for parallel processing of byte streams.
struct WorkUnit {
    const std::uint8_t* data;   // Pointer to byte data
    std::size_t length;         // Length of data chunk
    std::size_t offset;         // Original offset in stream (for ordering)
};

/// Result from processing a work unit.
struct WorkResult {
    std::size_t offset;                                    // Original offset for ordering
    std::vector<std::pair<NodeRef, NodeRef>> pairs;       // Pairs for frequency counting
    // RLE sequence stored separately as it has complex type
};

} // namespace hartonomous
