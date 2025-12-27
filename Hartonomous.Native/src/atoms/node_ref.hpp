#pragma once

#include "atom_id_type.hpp"
#include <cstdint>

namespace hartonomous {

/// Reference to either an Atom or a Composition.
/// Uses a discriminated union approach with a type flag.
struct NodeRef {
    std::int64_t id_high;
    std::int64_t id_low;
    bool is_atom;  // true = Atom, false = Composition

    constexpr NodeRef() noexcept : id_high(0), id_low(0), is_atom(true) {}

    /// Create reference to an Atom
    static constexpr NodeRef atom(AtomId id) noexcept {
        return NodeRef{id.high, id.low, true};
    }

    /// Create reference to a Composition (using its Merkle hash as ID)
    static constexpr NodeRef comp(std::int64_t hash_high, std::int64_t hash_low) noexcept {
        return NodeRef{hash_high, hash_low, false};
    }

    constexpr bool operator==(const NodeRef& other) const noexcept {
        return id_high == other.id_high && id_low == other.id_low && is_atom == other.is_atom;
    }

private:
    constexpr NodeRef(std::int64_t h, std::int64_t l, bool atom) noexcept
        : id_high(h), id_low(l), is_atom(atom) {}
};

} // namespace hartonomous
