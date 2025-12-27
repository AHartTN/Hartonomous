#pragma once

#include "node_ref.hpp"
#include "rle_child.hpp"
#include <vector>
#include <cstdint>
#include <iterator>
#include <functional>
#include <algorithm>

namespace hartonomous {

/// RLE-encoded sequence of NodeRefs.
/// Collapses consecutive identical items into (item, count) pairs.
///
/// Provides both compressed (RLEChild) and expanded (NodeRef) iteration
/// without materializing the full expanded sequence.
struct RLESequence {
    std::vector<RLEChild> items;

    // Cached cumulative counts for O(log n) random access
    mutable std::vector<std::size_t> cumulative_counts_;
    mutable bool cumulative_valid_ = false;

    /// Iterator that expands RLE on-the-fly without allocation.
    /// Iterates over individual NodeRefs, transparently handling repetition.
    class ExpandedIterator {
    public:
        using iterator_category = std::forward_iterator_tag;
        using value_type = NodeRef;
        using difference_type = std::ptrdiff_t;
        using pointer = const NodeRef*;
        using reference = const NodeRef&;

    private:
        const std::vector<RLEChild>* items_;
        std::size_t item_idx_;
        std::uint32_t rep_idx_;

    public:
        ExpandedIterator() : items_(nullptr), item_idx_(0), rep_idx_(0) {}
        ExpandedIterator(const std::vector<RLEChild>* items, std::size_t idx, std::uint32_t rep)
            : items_(items), item_idx_(idx), rep_idx_(rep) {}

        reference operator*() const { return (*items_)[item_idx_].ref; }
        pointer operator->() const { return &(*items_)[item_idx_].ref; }

        ExpandedIterator& operator++() {
            ++rep_idx_;
            if (rep_idx_ >= (*items_)[item_idx_].count) {
                ++item_idx_;
                rep_idx_ = 0;
            }
            return *this;
        }

        ExpandedIterator operator++(int) {
            ExpandedIterator tmp = *this;
            ++(*this);
            return tmp;
        }

        bool operator==(const ExpandedIterator& other) const {
            return item_idx_ == other.item_idx_ && rep_idx_ == other.rep_idx_;
        }

        bool operator!=(const ExpandedIterator& other) const {
            return !(*this == other);
        }

        /// Get current item index and repetition index.
        std::size_t item_index() const { return item_idx_; }
        std::uint32_t rep_index() const { return rep_idx_; }
    };

    /// Push a NodeRef onto the sequence, collapsing if same as previous.
    void push(NodeRef ref) {
        cumulative_valid_ = false;  // Invalidate cache
        if (!items.empty() && items.back().ref == ref) {
            items.back().count++;
        } else {
            items.push_back(RLEChild{ref, 1});
        }
    }

    /// Clear the sequence.
    void clear() {
        items.clear();
        cumulative_counts_.clear();
        cumulative_valid_ = false;
    }

    /// Reserve capacity for expected number of RLE items.
    void reserve(std::size_t n) {
        items.reserve(n);
        cumulative_counts_.reserve(n);
    }

    /// Get number of RLE items (compressed count).
    [[nodiscard]] std::size_t size() const { return items.size(); }

    /// Check if sequence is empty.
    [[nodiscard]] bool empty() const { return items.empty(); }

    /// Total count of all items (expanded count).
    [[nodiscard]] std::size_t total_count() const {
        if (items.empty()) return 0;
        ensure_cumulative();
        return cumulative_counts_.back();
    }

    /// Begin iterator for expanded traversal (no allocation).
    [[nodiscard]] ExpandedIterator expanded_begin() const {
        return ExpandedIterator(&items, 0, 0);
    }

    /// End iterator for expanded traversal.
    [[nodiscard]] ExpandedIterator expanded_end() const {
        return ExpandedIterator(&items, items.size(), 0);
    }

    /// Iterate over expanded elements with callback (zero allocation).
    /// More efficient than iterator for simple traversal.
    template<typename Func>
    void for_each_expanded(Func&& func) const {
        for (const auto& item : items) {
            for (std::uint32_t i = 0; i < item.count; ++i) {
                func(item.ref);
            }
        }
    }

    /// Iterate over pairs of adjacent expanded elements (zero allocation).
    /// Useful for pair frequency counting.
    template<typename Func>
    void for_each_pair(Func&& func) const {
        if (items.empty()) return;

        NodeRef prev = items[0].ref;
        bool first = true;

        for (const auto& item : items) {
            for (std::uint32_t i = 0; i < item.count; ++i) {
                if (first) {
                    first = false;
                } else {
                    func(prev, item.ref);
                }
                prev = item.ref;
            }
        }
    }

    /// Expand to full sequence (allocates - use sparingly).
    /// Prefer expanded_begin()/expanded_end() or for_each_expanded() instead.
    [[nodiscard]] std::vector<NodeRef> expand() const {
        std::vector<NodeRef> result;
        result.reserve(total_count());
        for_each_expanded([&result](NodeRef ref) {
            result.push_back(ref);
        });
        return result;
    }

    /// Get element at expanded index - O(log n) with cached cumulative counts.
    [[nodiscard]] NodeRef at_expanded(std::size_t idx) const {
        ensure_cumulative();

        // Binary search for the item containing this index
        auto it = std::upper_bound(cumulative_counts_.begin(), cumulative_counts_.end(), idx);
        if (it == cumulative_counts_.end()) {
            throw std::out_of_range("RLESequence::at_expanded index out of range");
        }

        std::size_t item_idx = static_cast<std::size_t>(it - cumulative_counts_.begin());
        return items[item_idx].ref;
    }

    /// Get iterator at expanded index - O(log n).
    [[nodiscard]] ExpandedIterator iterator_at(std::size_t idx) const {
        ensure_cumulative();

        auto it = std::upper_bound(cumulative_counts_.begin(), cumulative_counts_.end(), idx);
        if (it == cumulative_counts_.end()) {
            return expanded_end();
        }

        std::size_t item_idx = static_cast<std::size_t>(it - cumulative_counts_.begin());
        std::size_t prev_cum = (item_idx > 0) ? cumulative_counts_[item_idx - 1] : 0;
        std::uint32_t rep_idx = static_cast<std::uint32_t>(idx - prev_cum);

        return ExpandedIterator(&items, item_idx, rep_idx);
    }

private:
    /// Build cumulative count cache for O(log n) random access.
    void ensure_cumulative() const {
        if (cumulative_valid_) return;

        cumulative_counts_.clear();
        cumulative_counts_.reserve(items.size());

        std::size_t acc = 0;
        for (const auto& item : items) {
            acc += item.count;
            cumulative_counts_.push_back(acc);
        }

        cumulative_valid_ = true;
    }
};

} // namespace hartonomous
