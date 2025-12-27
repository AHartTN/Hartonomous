#pragma once

#include <cstdint>

namespace hartonomous {

/// A composition is a DAG node that references other nodes (atoms or compositions).
///
/// Properties:
/// - Merkle hash computed from children → content-addressable identity
/// - Same children (in order) → same hash → automatic deduplication
/// - The children array IS the relationship data
///
/// Emergent semantics from graph structure:
/// - inbound_refs(this) = how many compositions reference this = importance
/// - children = what this contains
/// - co_occurrence(A,B) = compositions that contain both A and B
///
/// Storage: The children are stored separately (variable length).
/// This struct is the header/metadata.
struct CompositionHeader {
    std::int64_t merkle_high;    // Upper 64 bits of Merkle hash (identity)
    std::int64_t merkle_low;     // Lower 64 bits of Merkle hash
    std::uint32_t child_count;   // Number of children
    std::uint32_t flags;         // Reserved for metadata

    /// Flags for composition types
    static constexpr std::uint32_t FLAG_RLE = 0x01;       // Contains run-length encoded segments
    static constexpr std::uint32_t FLAG_ORDERED = 0x02;   // Order of children matters (default)
    static constexpr std::uint32_t FLAG_SET = 0x04;       // Unordered set (for some structures)

    constexpr bool has_rle() const noexcept { return flags & FLAG_RLE; }
    constexpr bool is_ordered() const noexcept { return flags & FLAG_ORDERED; }
    constexpr bool is_set() const noexcept { return flags & FLAG_SET; }
};

} // namespace hartonomous
