#pragma once

#include "node_ref.hpp"
#include "rle_child.hpp"
#include "hash_utils.hpp"
#include <cstdint>
#include <utility>

namespace hartonomous {

/// Merkle hash computation for compositions.
/// Uses a simple but deterministic algorithm.
///
/// The hash is computed as:
///   hash = H(child_count || pos_0 || child[0] || pos_1 || child[1] || ...)
///
/// Where H is a collision-resistant hash function.
/// Position is mixed in to ensure order-sensitivity: hash(A,B) ≠ hash(B,A)
class MerkleHash {
public:
    /// Compute Merkle hash for a sequence of children.
    /// Returns (high, low) 128-bit hash.
    /// ORDER-SENSITIVE: hash(A,B) ≠ hash(B,A)
    template<typename Iter>
    [[nodiscard]] static constexpr std::pair<std::int64_t, std::int64_t>
    compute(Iter begin, Iter end) noexcept {
        // Use centralized FNV constants
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

    /// Compute hash for RLE-encoded children
    template<typename Iter>
    [[nodiscard]] static constexpr std::pair<std::int64_t, std::int64_t>
    compute_rle(Iter begin, Iter end) noexcept {
        constexpr std::uint64_t FNV_PRIME = FnvHashConstants::PRIME;
        constexpr std::uint64_t FNV_OFFSET = FnvHashConstants::OFFSET;

        std::uint64_t hash_low = FNV_OFFSET;
        std::uint64_t hash_high = FNV_OFFSET;

        std::uint32_t total_count = 0;
        for (auto it = begin; it != end; ++it) {
            const RLEChild& rle = *it;
            total_count += rle.count;

            // Mix in the reference
            hash_low ^= static_cast<std::uint64_t>(rle.ref.id_low);
            hash_low *= FNV_PRIME;
            hash_high ^= static_cast<std::uint64_t>(rle.ref.id_high);
            hash_high *= FNV_PRIME;

            // Mix in the count
            hash_low ^= static_cast<std::uint64_t>(rle.count);
            hash_low *= FNV_PRIME;
        }

        hash_high ^= total_count;
        hash_high *= FNV_PRIME;

        return {static_cast<std::int64_t>(hash_high),
                static_cast<std::int64_t>(hash_low)};
    }
};

} // namespace hartonomous
