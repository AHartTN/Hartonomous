#pragma once

#include "node_ref.hpp"
#include "hash_utils.hpp"
#include <cstdint>
#include <utility>

namespace hartonomous {

/// Merkle hash computation for compositions.
/// Uses a simple but deterministic algorithm.
class MerkleHash {
public:
    /// Compute Merkle hash for a sequence of children.
    /// Returns (high, low) 128-bit hash.
    /// ORDER-SENSITIVE: hash(A,B) ≠ hash(B,A)
    template<typename Iter>
    [[nodiscard]] static constexpr std::pair<std::int64_t, std::int64_t>
    compute(Iter begin, Iter end) noexcept {
        constexpr std::uint64_t FNV_PRIME = FnvHashConstants::PRIME;
        constexpr std::uint64_t FNV_OFFSET = FnvHashConstants::OFFSET;

        std::uint64_t hash_low = FNV_OFFSET;
        std::uint64_t hash_high = FNV_OFFSET;

        std::uint32_t position = 0;
        for (auto it = begin; it != end; ++it, ++position) {
            const NodeRef& ref = *it;

            // Mix in position FIRST to make order matter
            hash_low ^= static_cast<std::uint64_t>(position);
            hash_low *= FNV_PRIME;
            hash_high ^= static_cast<std::uint64_t>(position * 0x9E3779B97F4A7C15ULL);
            hash_high *= FNV_PRIME;

            // Mix in the reference
            hash_low ^= static_cast<std::uint64_t>(ref.id_low);
            hash_low *= FNV_PRIME;
            hash_high ^= static_cast<std::uint64_t>(ref.id_high);
            hash_high *= FNV_PRIME;

            // Mix in the type flag
            hash_low ^= ref.is_atom ? 1ULL : 0ULL;
            hash_low *= FNV_PRIME;
        }

        // Mix in count for length commitment
        hash_high ^= position;
        hash_high *= FNV_PRIME;

        return {static_cast<std::int64_t>(hash_high),
                static_cast<std::int64_t>(hash_low)};
    }
};

} // namespace hartonomous
