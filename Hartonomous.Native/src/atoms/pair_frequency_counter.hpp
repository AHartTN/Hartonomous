#pragma once

#include "node_ref.hpp"
#include "hash_utils.hpp"
#include <array>
#include <mutex>
#include <unordered_map>
#include <vector>
#include <algorithm>
#include <cstdint>

namespace hartonomous {

/// Thread-safe pair frequency counter with sharded locks.
/// Counts occurrences of adjacent (left, right) NodeRef pairs.
/// Used during pair encoding vocabulary learning to find common patterns.
class PairFrequencyCounter {
    static constexpr std::size_t SHARD_COUNT = 64;
    
    struct Shard {
        mutable std::mutex mutex;
        std::unordered_map<std::pair<NodeRef, NodeRef>, std::uint64_t, PairHash, PairEqual> counts;
    };
    
    std::array<Shard, SHARD_COUNT> shards_;
    
    std::size_t shard_index(const std::pair<NodeRef, NodeRef>& p) const noexcept {
        return PairHash{}(p) % SHARD_COUNT;
    }

public:
    /// Increment count for a single pair.
    void increment(NodeRef left, NodeRef right, std::uint64_t count = 1) {
        auto pair = std::make_pair(left, right);
        std::size_t idx = shard_index(pair);
        std::lock_guard<std::mutex> lock(shards_[idx].mutex);
        shards_[idx].counts[pair] += count;
    }
    
    /// Batch increment - more efficient for multiple pairs.
    void batch_increment(const std::vector<std::pair<NodeRef, NodeRef>>& pairs) {
        // Sort by shard to minimize lock contention
        std::array<std::vector<std::pair<NodeRef, NodeRef>>, SHARD_COUNT> by_shard;
        for (const auto& p : pairs) {
            by_shard[shard_index(p)].push_back(p);
        }
        
        for (std::size_t i = 0; i < SHARD_COUNT; ++i) {
            if (!by_shard[i].empty()) {
                std::lock_guard<std::mutex> lock(shards_[i].mutex);
                for (const auto& p : by_shard[i]) {
                    shards_[i].counts[p]++;
                }
            }
        }
    }
    
    /// Get top N pairs by frequency.
    std::vector<std::pair<std::pair<NodeRef, NodeRef>, std::uint64_t>> 
    top_pairs(std::size_t n) const {
        // Merge all shards
        std::unordered_map<std::pair<NodeRef, NodeRef>, std::uint64_t, PairHash, PairEqual> merged;
        
        for (const auto& shard : shards_) {
            std::lock_guard<std::mutex> lock(shard.mutex);
            for (const auto& [pair, count] : shard.counts) {
                merged[pair] += count;
            }
        }
        
        // Use partial_sort for efficiency
        std::vector<std::pair<std::pair<NodeRef, NodeRef>, std::uint64_t>> all(merged.begin(), merged.end());
        
        std::size_t k = std::min(n, all.size());
        std::partial_sort(all.begin(), all.begin() + k, all.end(),
            [](const auto& a, const auto& b) { return a.second > b.second; });
        
        all.resize(k);
        return all;
    }
    
    /// Clear all counts.
    void clear() {
        for (auto& shard : shards_) {
            std::lock_guard<std::mutex> lock(shard.mutex);
            shard.counts.clear();
        }
    }
    
    /// Get total number of unique pairs across all shards.
    std::size_t total_unique_pairs() const {
        std::size_t total = 0;
        for (const auto& shard : shards_) {
            std::lock_guard<std::mutex> lock(shard.mutex);
            total += shard.counts.size();
        }
        return total;
    }
};

} // namespace hartonomous
