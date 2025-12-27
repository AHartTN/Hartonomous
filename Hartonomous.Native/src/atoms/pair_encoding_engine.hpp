#pragma once

#include "node_ref.hpp"
#include "codepoint_atom_table.hpp"
#include "composition_store.hpp"
#include "pair_frequency_counter.hpp"
#include "rle_sequence.hpp"
#include "work_unit.hpp"
#include "pair_encoding_config.hpp"
#include "../threading/threading.hpp"
#include <vector>
#include <string>
#include <atomic>
#include <cstdint>

namespace hartonomous {

/// BPE-style Pair Encoding Engine with vocabulary learning.
///
/// This engine implements a compression algorithm that iteratively merges
/// the most frequent adjacent pairs of atoms/compositions into new composition
/// nodes, building a Merkle DAG structure with learned vocabulary.
///
/// Algorithm:
/// 1. Stream bytes -> atoms (via CodepointAtomTable with UTF-8 decoding)
/// 2. RLE collapse consecutive identical atoms
/// 3. Count all adjacent pair frequencies (parallel)
/// 4. Merge most frequent pairs -> new vocabulary entries
/// 5. Replace all occurrences of those pairs (parallel)
/// 6. Repeat 3-5 until no pair exceeds threshold
/// 7. Build final balanced Merkle tree from remaining sequence
///
/// Use PairEncodingCascade for simple tree building without vocabulary learning.
class PairEncodingEngine {
public:
    using Config = PairEncodingConfig;

private:
    Config config_;
    CompositionStore store_;
    PairFrequencyCounter freq_counter_;
    std::atomic<std::size_t> total_bytes_processed_{0};
    std::atomic<std::size_t> total_compositions_{0};



    std::size_t effective_thread_count() const {
        if (config_.thread_count > 0) return config_.thread_count;
        return Threading::default_thread_count();
    }

    /// Work result with RLE sequence included.
    struct ChunkResult {
        std::size_t offset;
        RLESequence sequence;
    };

public:
    explicit PairEncodingEngine(Config config = {})
        : config_(std::move(config))
    {
    }

    // Non-copyable
    PairEncodingEngine(const PairEncodingEngine&) = delete;
    PairEncodingEngine& operator=(const PairEncodingEngine&) = delete;

    /// Process a byte stream, learning vocabulary and returning root composition.
    NodeRef ingest(const std::uint8_t* data, std::size_t length) {
        if (length == 0) return NodeRef{};

        // Decode UTF-8 to codepoints
        auto all_codepoints = UTF8Decoder::decode(data, length);
        const auto& atoms = CodepointAtomTable::instance();

        // Create work units based on codepoints
        std::vector<std::pair<std::size_t, std::size_t>> ranges; // start, end indices into codepoints
        std::size_t cp_count = all_codepoints.size();
        for (std::size_t start = 0; start < cp_count; start += config_.chunk_size) {
            std::size_t end = std::min(start + config_.chunk_size, cp_count);
            ranges.push_back({start, end});
        }

        std::vector<ChunkResult> results(ranges.size());

        // Phase 1: Convert to atoms + RLE (parallel)
        Threading::parallel_for(ranges.size(), [&](std::size_t idx) {
            auto [start, end] = ranges[idx];
            ChunkResult& result = results[idx];
            result.offset = start;
            result.sequence.reserve((end - start) / 4);  // Estimate RLE compression

            for (std::size_t i = start; i < end; ++i) {
                result.sequence.push(atoms.ref(all_codepoints[i]));
            }
        });

        // Count initial pairs
        count_all_pairs(results);

        // Phase 2: Iterative pair merging
        while (store_.size() < config_.max_vocabulary_size) {
            auto top = freq_counter_.top_pairs(config_.batch_merge_count);
            if (top.empty() || top[0].second < config_.min_pair_frequency) {
                break;
            }

            bool any_new = false;
            for (const auto& [pair, count] : top) {
                if (count < config_.min_pair_frequency) break;

                // Add to store if new
                auto existing = store_.find(pair.first, pair.second);
                if (!existing) {
                    store_.get_or_create(pair.first, pair.second);
                    any_new = true;
                    total_compositions_++;
                }
            }

            if (!any_new) break;

            // Phase 3: Replace pairs in all sequences (parallel)
            Threading::parallel_for(results.size(), [&](std::size_t unit_idx) {
                replace_pairs_in_chunk(results[unit_idx].sequence);
            });

            // Re-count pairs
            freq_counter_.clear();
            count_all_pairs(results);
        }

        // Phase 4: Build final tree from all chunk sequences
        // Count non-empty chunks first to avoid wasted capacity
        std::size_t non_empty_count = 0;
        for (const auto& result : results) {
            if (!result.sequence.empty()) ++non_empty_count;
        }

        std::vector<NodeRef> chunk_roots;
        chunk_roots.reserve(non_empty_count);

        for (const auto& result : results) {
            if (!result.sequence.empty()) {
                chunk_roots.push_back(build_tree_from_rle(result.sequence));
            }
        }

        total_bytes_processed_ += length;

        // Combine chunk roots into final tree
        return build_balanced_tree(chunk_roots.data(), chunk_roots.size());
    }

    /// Convenience overload for string.
    NodeRef ingest(const char* text, std::size_t length) {
        return ingest(reinterpret_cast<const std::uint8_t*>(text), length);
    }

    NodeRef ingest(const std::string& text) {
        return ingest(text.data(), text.size());
    }

    /// Get composition store size.
    [[nodiscard]] std::size_t vocabulary_size() const { return store_.size(); }

    /// Get total bytes processed.
    [[nodiscard]] std::size_t bytes_processed() const { return total_bytes_processed_; }

    /// Get total compositions created.
    [[nodiscard]] std::size_t compositions_created() const { return total_compositions_; }

    /// DECODE: Reconstruct original bytes from root NodeRef.
    /// This is the INVERSE of ingest - required for lossless verification.
    [[nodiscard]] std::vector<std::uint8_t> decode(NodeRef root) const {
        std::vector<std::uint8_t> result;
        result.reserve(total_bytes_processed_.load());  // Pre-allocate for known size
        decode_recursive(root, result);
        return result;
    }

    /// Access composition store for database operations.
    [[nodiscard]] const CompositionStore& store() const { return store_; }
    [[nodiscard]] CompositionStore& store() { return store_; }

private:
    /// Count pairs in all chunks using RLE-aware iteration - PARALLEL.
    void count_all_pairs(const std::vector<ChunkResult>& results) {
        Threading::parallel_for(results.size(), [&](std::size_t i) {
            const auto& result = results[i];
            result.sequence.for_each_pair([this](NodeRef left, NodeRef right) {
                freq_counter_.increment(left, right);
            });
        });
    }

    /// Replace known pairs in a chunk's sequence.
    void replace_pairs_in_chunk(RLESequence& sequence) {
        RLESequence new_seq;
        new_seq.reserve(sequence.size());

        auto it = sequence.expanded_begin();
        auto end = sequence.expanded_end();

        while (it != end) {
            NodeRef current = *it;
            ++it;

            if (it != end) {
                NodeRef next = *it;
                auto found = store_.find(current, next);
                if (found) {
                    new_seq.push(*found);
                    ++it;  // Skip the second element of the pair
                    continue;
                }
            }

            new_seq.push(current);
        }

        sequence = std::move(new_seq);
    }

    /// Build balanced tree from RLE sequence using iterators (no full expansion).
    NodeRef build_tree_from_rle(const RLESequence& seq) {
        if (seq.empty()) return NodeRef{};

        std::size_t total = seq.total_count();
        if (total == 1) {
            return seq.items[0].ref;
        }
        if (total == 2) {
            auto it = seq.expanded_begin();
            NodeRef first = *it; ++it;
            NodeRef second = *it;
            return store_.get_or_create(first, second);
        }

        // Use divide-and-conquer on RLE items to minimize expansion
        return build_tree_from_rle_range(seq, 0, 0, total);
    }

    /// Build tree from a range within RLE sequence without full expansion.
    /// Uses item-level divide-and-conquer.
    NodeRef build_tree_from_rle_range(const RLESequence& seq,
                                       std::size_t start_item, [[maybe_unused]] std::size_t start_rep,
                                       std::size_t count) {
        if (count == 0) return NodeRef{};
        if (count == 1) {
            // Find the single element - just return the ref at start_item
            return seq.items[start_item].ref;
        }
        if (count == 2) {
            auto it = IteratorAt(seq, start_item, start_rep);
            NodeRef first = *it; ++it;
            NodeRef second = *it;
            return store_.get_or_create(first, second);
        }

        // For larger ranges, split and recurse
        std::size_t mid = count / 2;

        // Find the split point in RLE items
        std::size_t left_count = mid;
        std::size_t right_count = count - mid;

        // Build left and right subtrees
        auto left_it = IteratorAt(seq, start_item, start_rep);
        std::vector<NodeRef> left_nodes;
        left_nodes.reserve(left_count);
        for (std::size_t i = 0; i < left_count; ++i) {
            left_nodes.push_back(*left_it);
            ++left_it;
        }

        std::vector<NodeRef> right_nodes;
        right_nodes.reserve(right_count);
        for (std::size_t i = 0; i < right_count; ++i) {
            right_nodes.push_back(*left_it);
            ++left_it;
        }

        NodeRef left = build_balanced_tree(left_nodes.data(), left_nodes.size());
        NodeRef right = build_balanced_tree(right_nodes.data(), right_nodes.size());
        return store_.get_or_create(left, right);
    }

    /// Create iterator at specific position
    static RLESequence::ExpandedIterator IteratorAt(const RLESequence& seq,
                                                     std::size_t item_idx, std::size_t rep_idx) {
        return RLESequence::ExpandedIterator(&seq.items, item_idx, static_cast<std::uint32_t>(rep_idx));
    }

    /// ITERATIVE decode using explicit stack - no recursion limit, much faster.
    void decode_recursive(NodeRef node, std::vector<std::uint8_t>& out) const {
        std::vector<NodeRef> stack;
        stack.reserve(131072);  // Pre-allocate for deep trees
        stack.push_back(node);

        while (!stack.empty()) {
            NodeRef current = stack.back();
            stack.pop_back();

            if (current.id_high == 0 && current.id_low == 0 && !current.is_atom) {
                continue;  // Null node
            }

            if (current.is_atom) {
                // Convert atom back to codepoint, then UTF-8 encode
                std::int32_t cp = SemanticDecompose::atom_to_codepoint(
                    AtomId{current.id_high, current.id_low});
                std::uint8_t buf[4];
                std::size_t len = UTF8Decoder::encode_one(cp, buf);
                out.insert(out.end(), buf, buf + len);
                continue;
            }

            auto children = store_.decompose(current);
            if (!children) {
                throw std::runtime_error("Cannot decode: unknown composition");
            }

            // Push right first so left processes first (stack is LIFO)
            stack.push_back(children->second);
            stack.push_back(children->first);
        }
    }

    /// Build balanced binary tree from sequence.
    NodeRef build_balanced_tree(const NodeRef* items, std::size_t count) {
        if (count == 0) return NodeRef{};
        if (count == 1) return items[0];
        if (count == 2) return store_.get_or_create(items[0], items[1]);

        std::size_t mid = count / 2;
        NodeRef left = build_balanced_tree(items, mid);
        NodeRef right = build_balanced_tree(items + mid, count - mid);
        return store_.get_or_create(left, right);
    }
};

} // namespace hartonomous
