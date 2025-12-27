#pragma once

/// SUBSTRING-AWARE ENCODER
///
/// Uses content-defined chunking so that:
/// - "Captain Ahab" produces the same composition root whether
///   encoded standalone or as part of Moby Dick
/// - Enables substring containment queries
/// - Maintains bit-perfect lossless encoding
///
/// The composition tree structure:
///   Root
///   ├── Chunk1 (content-defined)
///   │   └── [byte atoms or sub-chunks]
///   └── Chunk2 (content-defined)
///       └── [byte atoms or sub-chunks]

#include "content_chunker.hpp"
#include "node_ref.hpp"
#include "codepoint_atom_table.hpp"
#include "merkle_hash.hpp"
#include <vector>
#include <unordered_map>

namespace hartonomous {

/// Composition entry for database storage
struct CompositionEntry {
    NodeRef parent;
    NodeRef left;
    NodeRef right;
};

/// Substring-aware encoder using content-defined chunking
class SubstringEncoder {
    HierarchicalChunker chunker_;
    const CodepointAtomTable& atoms_;

    // Composition cache: prevents duplicate computation
    std::unordered_map<std::uint64_t, NodeRef> cache_;

    // Pending compositions for batch DB insert
    std::vector<CompositionEntry> pending_;

    static std::uint64_t cache_key(std::int64_t h, std::int64_t l) {
        return static_cast<std::uint64_t>(h) ^
               (static_cast<std::uint64_t>(l) * 0x9e3779b97f4a7c15ULL);
    }

public:
    SubstringEncoder() : atoms_(CodepointAtomTable::instance()) {
        cache_.reserve(100000);
        pending_.reserve(10000);
    }

    /// Encode content with content-defined chunking
    /// Returns root NodeRef
    [[nodiscard]] NodeRef encode(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};

        // Decode UTF-8 to codepoints
        auto codepoints = UTF8Decoder::decode(data, len);
        if (codepoints.size() == 1) return atoms_.ref(codepoints[0]);

        // Small content: encode directly as balanced tree (fast path)
        if (codepoints.size() <= 8) {
            return encode_balanced_codepoints(codepoints, 0, codepoints.size());
        }

        // Use content-defined chunks on the raw bytes
        auto chunks = chunker_.chunk(data, len);

        if (chunks.size() == 1 && chunks[0].repeat_count == 1) {
            // Single chunk, no RLE - encode recursively
            return encode_chunk(chunks[0]);
        }

        // Multiple chunks or RLE - build tree from chunks
        std::vector<NodeRef> chunk_refs;
        chunk_refs.reserve(chunks.size());

        for (const auto& chunk : chunks) {
            NodeRef ref = encode_chunk(chunk);

            // Handle RLE: same chunk repeated
            for (std::uint32_t i = 1; i < chunk.repeat_count; ++i) {
                // Create composition of (prev, ref) for each repeat
                // This maintains order while recording repetition
                NodeRef children[2] = {ref, ref};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef repeat_ref = NodeRef::comp(h, l);

                add_composition(repeat_ref, ref, ref);
                ref = repeat_ref;
            }

            chunk_refs.push_back(ref);
        }

        // Build tree from chunk references
        return build_tree_from_refs(chunk_refs);
    }

    [[nodiscard]] NodeRef encode(const std::string& s) {
        return encode(reinterpret_cast<const std::uint8_t*>(s.data()), s.size());
    }

    [[nodiscard]] NodeRef encode(const char* s) {
        return encode(reinterpret_cast<const std::uint8_t*>(s), std::strlen(s));
    }

    /// Get pending compositions for database batch insert
    [[nodiscard]] const std::vector<CompositionEntry>& pending() const {
        return pending_;
    }

    /// Clear pending compositions (call after DB insert)
    void clear_pending() {
        pending_.clear();
    }

    /// Clear all caches
    void clear() {
        cache_.clear();
        pending_.clear();
    }

private:
    /// Encode a single chunk
    [[nodiscard]] NodeRef encode_chunk(const Chunk& chunk) {
        // Decode UTF-8 to codepoints
        auto codepoints = UTF8Decoder::decode(chunk.data, chunk.length);
        if (codepoints.size() == 1) {
            return atoms_.ref(codepoints[0]);
        }

        // Recursively chunk or use balanced tree for small chunks
        if (codepoints.size() <= 16) {
            return encode_balanced_codepoints(codepoints, 0, codepoints.size());
        }

        // Recursive content-defined chunking
        auto sub_chunks = chunker_.chunk(chunk.data, chunk.length);

        if (sub_chunks.size() == 1) {
            // No further subdivision - use balanced tree
            return encode_balanced_codepoints(codepoints, 0, codepoints.size());
        }

        std::vector<NodeRef> refs;
        refs.reserve(sub_chunks.size());
        for (const auto& sc : sub_chunks) {
            refs.push_back(encode_chunk(sc));
        }

        return build_tree_from_refs(refs);
    }

    /// Encode codepoints as balanced binary tree
    [[nodiscard]] NodeRef encode_balanced_codepoints(const std::vector<std::int32_t>& codepoints,
                                                      std::size_t start, std::size_t end) {
        std::size_t len = end - start;
        if (len == 1) return atoms_.ref(codepoints[start]);
        if (len == 2) {
            NodeRef left = atoms_.ref(codepoints[start]);
            NodeRef right = atoms_.ref(codepoints[start + 1]);
            return make_composition(left, right);
        }

        std::size_t mid = start + len / 2;
        NodeRef left = encode_balanced_codepoints(codepoints, start, mid);
        NodeRef right = encode_balanced_codepoints(codepoints, mid, end);
        return make_composition(left, right);
    }

    /// Build tree from array of NodeRefs
    [[nodiscard]] NodeRef build_tree_from_refs(std::vector<NodeRef>& refs) {
        if (refs.empty()) return NodeRef{};
        if (refs.size() == 1) return refs[0];

        // Pairwise reduction until single root
        while (refs.size() > 1) {
            std::vector<NodeRef> next;
            next.reserve((refs.size() + 1) / 2);

            for (std::size_t i = 0; i < refs.size(); i += 2) {
                if (i + 1 < refs.size()) {
                    next.push_back(make_composition(refs[i], refs[i + 1]));
                } else {
                    next.push_back(refs[i]);  // Odd one out
                }
            }

            refs = std::move(next);
        }

        return refs[0];
    }

    /// Create composition, cache it, and add to pending
    [[nodiscard]] NodeRef make_composition(NodeRef left, NodeRef right) {
        NodeRef children[2] = {left, right};
        auto [h, l] = MerkleHash::compute(children, children + 2);

        auto key = cache_key(h, l);
        auto it = cache_.find(key);
        if (it != cache_.end()) {
            return it->second;
        }

        NodeRef comp = NodeRef::comp(h, l);
        cache_[key] = comp;
        add_composition(comp, left, right);
        return comp;
    }

    void add_composition(NodeRef parent, NodeRef left, NodeRef right) {
        pending_.push_back({parent, left, right});
    }
};

} // namespace hartonomous
