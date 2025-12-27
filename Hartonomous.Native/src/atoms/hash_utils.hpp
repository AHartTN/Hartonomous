#pragma once

#include "node_ref.hpp"
#include <cstdint>
#include <utility>

namespace hartonomous {

/// FNV-1a hash constants used across the codebase.
/// Centralized here to ensure consistency and eliminate duplication.
struct FnvHashConstants {
    static constexpr std::uint64_t PRIME = 0x00000100000001B3ULL;
    static constexpr std::uint64_t OFFSET = 0xcbf29ce484222325ULL;
};

/// Hash function for single NodeRef.
/// Used as key in hash maps for composition lookups.
struct NodeRefHash {
    std::size_t operator()(const NodeRef& n) const noexcept {
        std::size_t h = FnvHashConstants::OFFSET;
        h ^= static_cast<std::size_t>(n.id_high);
        h *= FnvHashConstants::PRIME;
        h ^= static_cast<std::size_t>(n.id_low);
        h *= FnvHashConstants::PRIME;
        h ^= static_cast<std::size_t>(n.is_atom);
        h *= FnvHashConstants::PRIME;
        return h;
    }
};

/// Equality comparator for NodeRef in hash containers.
struct NodeRefEqual {
    bool operator()(const NodeRef& a, const NodeRef& b) const noexcept {
        return a == b;
    }
};

/// Hash function for NodeRef pairs.
/// Used for composition pair lookups in vocabulary.
struct PairHash {
    std::size_t operator()(const std::pair<NodeRef, NodeRef>& p) const noexcept {
        std::size_t h = FnvHashConstants::OFFSET;
        h ^= static_cast<std::size_t>(p.first.id_high);
        h *= FnvHashConstants::PRIME;
        h ^= static_cast<std::size_t>(p.first.id_low);
        h *= FnvHashConstants::PRIME;
        h ^= static_cast<std::size_t>(p.first.is_atom);
        h *= FnvHashConstants::PRIME;
        h ^= static_cast<std::size_t>(p.second.id_high);
        h *= FnvHashConstants::PRIME;
        h ^= static_cast<std::size_t>(p.second.id_low);
        h *= FnvHashConstants::PRIME;
        h ^= static_cast<std::size_t>(p.second.is_atom);
        h *= FnvHashConstants::PRIME;
        return h;
    }
};

/// Equality comparator for NodeRef pairs in hash containers.
struct PairEqual {
    bool operator()(const std::pair<NodeRef, NodeRef>& a, 
                    const std::pair<NodeRef, NodeRef>& b) const noexcept {
        return a.first == b.first && a.second == b.second;
    }
};

/// Compute hash for a pair of NodeRefs (standalone function).
/// Returns size_t hash suitable for use as map key.
inline std::size_t hash_node_pair(NodeRef left, NodeRef right) noexcept {
    return PairHash{}(std::make_pair(left, right));
}

} // namespace hartonomous
