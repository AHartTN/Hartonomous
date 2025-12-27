#pragma once

#include "node_ref.hpp"
#include "semantic_decompose.hpp"
#include <cstdint>
#include <stdexcept>

namespace hartonomous {

/// Pure functional byte ↔ NodeRef conversion.
/// NO CACHING. NO LOOKUP TABLES. NO ARBITRARY LIMITS.
/// 
/// Uses the deterministic SemanticDecompose functions directly.
/// Works for ALL Unicode codepoints, not just 0-255.
class ByteAtomTable {
public:
    /// Get the singleton instance (for API compatibility).
    /// This is now just a namespace - no state.
    static const ByteAtomTable& instance() {
        static ByteAtomTable table;
        return table;
    }
    
    /// Convert a codepoint to its corresponding NodeRef.
    /// Works for ANY valid Unicode codepoint.
    [[nodiscard]] static NodeRef to_ref(std::int32_t codepoint) noexcept { 
        AtomId id = SemanticDecompose::get_atom_id(codepoint);
        return NodeRef::atom(id);
    }
    
    /// Array subscript operator for byte values (compatibility).
    [[nodiscard]] NodeRef operator[](std::uint8_t byte) const noexcept { 
        return to_ref(static_cast<std::int32_t>(byte)); 
    }
    
    /// Reverse lookup: NodeRef → codepoint.
    /// Pure computation - no hash table.
    [[nodiscard]] static std::int32_t to_codepoint(NodeRef ref) {
        if (!ref.is_atom) {
            throw std::runtime_error("NodeRef is not an atom");
        }
        AtomId id{ref.id_high, ref.id_low};
        return SemanticDecompose::atom_to_codepoint(id);
    }
    
    /// Reverse lookup: NodeRef → byte value.
    /// Throws if the codepoint is not in 0-255 range.
    [[nodiscard]] std::uint8_t to_byte(NodeRef ref) const {
        std::int32_t cp = to_codepoint(ref);
        if (cp < 0 || cp > 255) {
            throw std::runtime_error("NodeRef is not a valid byte atom (codepoint out of range)");
        }
        return static_cast<std::uint8_t>(cp);
    }
    
    /// Check if NodeRef represents a byte (0-255).
    [[nodiscard]] static bool is_byte_atom(NodeRef ref) noexcept {
        if (!ref.is_atom) return false;
        AtomId id{ref.id_high, ref.id_low};
        std::int32_t cp = SemanticDecompose::atom_to_codepoint(id);
        return cp >= 0 && cp <= 255;
    }
};

} // namespace hartonomous
