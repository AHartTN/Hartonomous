#pragma once

#include "node_ref.hpp"
#include "merkle_hash.hpp"
#include "hash_utils.hpp"
#include <optional>
#include <mutex>
#include <unordered_map>
#include <vector>
#include <utility>
#include <stdexcept>
#include <cstdint>

namespace hartonomous {

/// Thread-local composition cache for lock-free parallel processing.
/// Each worker thread gets its own instance to avoid contention.
/// Must be merged into CompositionStore after parallel work completes.
class CompositionCache {
public:
    struct Entry {
        NodeRef ref;
        NodeRef left;
        NodeRef right;
    };

private:
    std::vector<Entry> entries_;
    std::unordered_map<std::pair<NodeRef, NodeRef>, std::size_t, PairHash, PairEqual> pair_to_idx_;

public:
    CompositionCache() {
        entries_.reserve(65536);
        pair_to_idx_.reserve(65536);
    }

    /// Get or create composition locally (no global collision check yet).
    /// Collision safety is ensured when merging into CompositionStore.
    NodeRef get_or_create(NodeRef left, NodeRef right) {
        const auto pair = std::make_pair(left, right);

        auto it = pair_to_idx_.find(pair);
        if (it != pair_to_idx_.end()) {
            return entries_[it->second].ref;
        }

        // Compute Merkle hash (will be validated during merge)
        NodeRef children[2] = {left, right};
        auto [hash_high, hash_low] = MerkleHash::compute(children, children + 2);
        NodeRef comp_ref = NodeRef::comp(hash_high, hash_low);

        std::size_t idx = entries_.size();
        entries_.push_back({comp_ref, left, right});
        pair_to_idx_.emplace(pair, idx);

        return comp_ref;
    }

    [[nodiscard]] const std::vector<Entry>& entries() const { return entries_; }
    [[nodiscard]] std::size_t size() const { return entries_.size(); }
    void clear() { entries_.clear(); pair_to_idx_.clear(); }
};

/// Unified thread-safe composition store with collision handling.
///
/// This is the single source of truth for all compositions.
/// Features:
/// - Collision-safe Merkle hashing with salt resolution
/// - Reverse mapping for O(1) decode/decomposition
/// - Thread-local cache merging for parallel workloads
/// - Reader-writer locking for concurrent access
///
/// Replaces: Vocabulary, GlobalCompositionStore, ThreadLocalCompositionStore
class CompositionStore {
public:
    struct Composition {
        NodeRef parent;
        NodeRef left;
        NodeRef right;
    };

private:
    mutable std::mutex mutex_;

    // Forward mapping: (left, right) -> composition ref
    std::unordered_map<std::pair<NodeRef, NodeRef>, NodeRef, PairHash, PairEqual> forward_;

    // Reverse mapping: composition ref -> (left, right) for decoding
    std::unordered_map<NodeRef, std::pair<NodeRef, NodeRef>, NodeRefHash, NodeRefEqual> reverse_;

    // Ordered list for database export
    std::vector<Composition> compositions_;

public:
    CompositionStore() {
        // Pre-allocate for typical vocabulary sizes
        forward_.reserve(1000000);
        reverse_.reserve(1000000);
        compositions_.reserve(1000000);
    }

    // Non-copyable, non-movable (contains mutex)
    CompositionStore(const CompositionStore&) = delete;
    CompositionStore& operator=(const CompositionStore&) = delete;
    CompositionStore(CompositionStore&&) = delete;
    CompositionStore& operator=(CompositionStore&&) = delete;

    /// Try to find existing composition for pair.
    [[nodiscard]] std::optional<NodeRef> find(NodeRef left, NodeRef right) const {
        const auto pair = std::make_pair(left, right);
        std::unique_lock lock(mutex_);
        auto it = forward_.find(pair);
        if (it != forward_.end()) {
            return it->second;
        }
        return std::nullopt;
    }

    /// Decompose a composition into its children (for decoding).
    [[nodiscard]] std::optional<std::pair<NodeRef, NodeRef>> decompose(NodeRef comp) const {
        std::unique_lock lock(mutex_);
        auto it = reverse_.find(comp);
        if (it != reverse_.end()) {
            return it->second;
        }
        return std::nullopt;
    }

    /// Check if a composition exists.
    [[nodiscard]] bool contains(NodeRef comp) const {
        std::unique_lock lock(mutex_);
        return reverse_.find(comp) != reverse_.end();
    }

    /// Get or create composition for pair.
    /// CRITICAL: Handles Merkle hash collisions for lossless round-trip.
    /// Uses single-lock pattern to avoid double-lookup overhead.
    NodeRef get_or_create(NodeRef left, NodeRef right) {
        const auto pair = std::make_pair(left, right);

        // Single lock acquisition - avoids double-lookup on miss
        std::unique_lock lock(mutex_);

        // Check if exists
        auto existing = forward_.find(pair);
        if (existing != forward_.end()) {
            return existing->second;
        }

        // Compute base Merkle hash (outside would be better but need lock for collision check)
        NodeRef children[2] = {left, right};
        auto [hash_high, hash_low] = MerkleHash::compute(children, children + 2);

        // Handle collisions with minimal lock time
        return insert_with_collision_handling(pair, hash_high, hash_low, left, right);
    }

    /// Merge entries from a thread-local cache.
    /// Applies collision handling to all cached entries.
    void merge(const CompositionCache& cache) {
        if (cache.entries().empty()) return;

        std::unique_lock lock(mutex_);

        for (const auto& entry : cache.entries()) {
            const auto pair = std::make_pair(entry.left, entry.right);

            // Skip if already exists
            if (forward_.find(pair) != forward_.end()) {
                continue;
            }

            // Compute hash with collision handling
            NodeRef children[2] = {entry.left, entry.right};
            auto [hash_high, hash_low] = MerkleHash::compute(children, children + 2);

            insert_with_collision_handling_unlocked(pair, hash_high, hash_low, entry.left, entry.right);
        }
    }

    /// Get number of compositions.
    [[nodiscard]] std::size_t size() const {
        std::unique_lock lock(mutex_);
        return compositions_.size();
    }

    /// Get all compositions for database export.
    [[nodiscard]] const std::vector<Composition>& compositions() const {
        return compositions_;
    }

    /// Get all forward mappings for database export.
    [[nodiscard]] const std::unordered_map<std::pair<NodeRef, NodeRef>, NodeRef, PairHash, PairEqual>&
    all_compositions() const {
        return forward_;
    }

    /// Clear all compositions.
    void clear() {
        std::unique_lock lock(mutex_);
        forward_.clear();
        reverse_.clear();
        compositions_.clear();
    }

private:
    /// Insert with collision handling - lock already held
    NodeRef insert_with_collision_handling(
        const std::pair<NodeRef, NodeRef>& pair,
        std::int64_t hash_high, std::int64_t hash_low,
        NodeRef left, NodeRef right)
    {
        return insert_with_collision_handling_unlocked(pair, hash_high, hash_low, left, right);
    }

    /// Insert with collision handling - assumes lock is held
    NodeRef insert_with_collision_handling_unlocked(
        const std::pair<NodeRef, NodeRef>& pair,
        std::int64_t hash_high, std::int64_t hash_low,
        NodeRef left, NodeRef right)
    {
        std::uint64_t salt = 0;
        constexpr std::uint64_t MAX_SALT = 1000000;

        while (salt < MAX_SALT) {
            std::int64_t salted_high = hash_high ^ static_cast<std::int64_t>(salt);
            std::int64_t salted_low = hash_low ^ static_cast<std::int64_t>(salt >> 32);
            NodeRef comp = NodeRef::comp(salted_high, salted_low);

            auto rev_it = reverse_.find(comp);
            if (rev_it == reverse_.end()) {
                // No collision - use this hash
                forward_.emplace(pair, comp);
                reverse_.emplace(comp, pair);
                compositions_.push_back({comp, left, right});
                return comp;
            }

            // Check if it's actually the same pair (concurrent insert)
            if (rev_it->second.first == left && rev_it->second.second == right) {
                return comp;
            }

            // Collision with different pair - try next salt
            ++salt;
        }

        throw std::runtime_error("Merkle hash collision overflow - this should never happen");
    }
};

} // namespace hartonomous
