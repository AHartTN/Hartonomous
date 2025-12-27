#pragma once

#include "node_ref.hpp"
#include "byte_atom_table.hpp"
#include "composition_store.hpp"
#include "../threading/threading.hpp"
#include <vector>
#include <cstdint>
#include <cstring>

namespace hartonomous {

/// Cascading pair encoding engine with parallel chunk processing.
/// Achieves O(n) hierarchical composition using work-stealing parallelism.
///
/// This builds a balanced binary tree from input bytes, creating compositions
/// for each pair of nodes. Uses thread-local caching for parallel efficiency
/// with collision-safe merging into the global store.
class PairEncodingCascade {
public:
    static constexpr std::size_t CHUNK_SIZE = 262144;  // 256KB chunks
    static constexpr std::size_t BLOCK_SIZE = 64;      // Cache-line aligned blocks

    /// Encode bytes to composition DAG. Returns root node.
    static NodeRef encode(const std::uint8_t* data, std::size_t len, CompositionStore& store) {
        if (len == 0) return NodeRef{};

        std::size_t num_chunks = (len + CHUNK_SIZE - 1) / CHUNK_SIZE;
        std::vector<NodeRef> chunk_roots(num_chunks);
        std::vector<CompositionCache> local_caches(num_chunks);

        // Parallel chunk processing with work-stealing
        Threading::parallel_for(num_chunks, [&](std::size_t i) {
            std::size_t start = i * CHUNK_SIZE;
            std::size_t end = std::min(start + CHUNK_SIZE, len);
            chunk_roots[i] = encode_chunk(data + start, end - start, local_caches[i]);
        });

        // Parallel merge - each cache merges independently (store handles locking internally)
        Threading::parallel_for(num_chunks, [&](std::size_t i) {
            store.merge(local_caches[i]);
        });

        // Compose chunk roots into balanced tree
        return build_tree(chunk_roots.data(), chunk_roots.size(), store);
    }

    /// Convenience overload for null-terminated strings.
    static NodeRef encode(const char* text, CompositionStore& store) {
        return encode(reinterpret_cast<const std::uint8_t*>(text), std::strlen(text), store);
    }

    /// Decode a composition tree back to bytes.
    static std::vector<std::uint8_t> decode(NodeRef root, const CompositionStore& store) {
        std::vector<std::uint8_t> result;
        decode_recursive(root, store, result);
        return result;
    }

private:
    static NodeRef encode_chunk(const std::uint8_t* data, std::size_t len, CompositionCache& cache) {
        if (len == 0) return NodeRef{};

        const auto& atoms = ByteAtomTable::instance();

        // Build block roots first, then reduce
        std::vector<NodeRef> level;
        level.reserve((len + BLOCK_SIZE - 1) / BLOCK_SIZE);

        // First level: create blocks of atoms
        for (std::size_t i = 0; i < len; i += BLOCK_SIZE) {
            std::size_t block_end = std::min(i + BLOCK_SIZE, len);
            NodeRef block_root = build_atom_block(data + i, block_end - i, cache, atoms);
            level.push_back(block_root);
        }

        // Reduce to single root via balanced tree - use swap to avoid move overhead
        std::vector<NodeRef> next_level;
        while (level.size() > 1) {
            next_level.clear();
            next_level.reserve((level.size() + 1) / 2);

            for (std::size_t i = 0; i < level.size(); i += 2) {
                if (i + 1 < level.size()) {
                    next_level.push_back(cache.get_or_create(level[i], level[i + 1]));
                } else {
                    next_level.push_back(level[i]);
                }
            }
            level.swap(next_level);
        }

        return level.empty() ? NodeRef{} : level[0];
    }

    static NodeRef build_atom_block(const std::uint8_t* data, std::size_t len,
                                     CompositionCache& cache, const ByteAtomTable& atoms) {
        if (len == 0) return NodeRef{};
        if (len == 1) return atoms[data[0]];
        if (len == 2) return cache.get_or_create(atoms[data[0]], atoms[data[1]]);

        // Recursive balanced split
        std::size_t mid = len / 2;
        NodeRef left = build_atom_block(data, mid, cache, atoms);
        NodeRef right = build_atom_block(data + mid, len - mid, cache, atoms);
        return cache.get_or_create(left, right);
    }

    static NodeRef build_tree(const NodeRef* nodes, std::size_t count, CompositionStore& store) {
        if (count == 0) return NodeRef{};
        if (count == 1) return nodes[0];
        if (count == 2) return store.get_or_create(nodes[0], nodes[1]);

        std::size_t mid = count / 2;
        NodeRef left = build_tree(nodes, mid, store);
        NodeRef right = build_tree(nodes + mid, count - mid, store);
        return store.get_or_create(left, right);
    }

    static void decode_recursive(NodeRef node, const CompositionStore& store,
                                  std::vector<std::uint8_t>& out) {
        // Null/empty node
        if (node.id_high == 0 && node.id_low == 0 && !node.is_atom) {
            return;
        }

        if (node.is_atom) {
            // Terminal: convert atom back to byte
            out.push_back(ByteAtomTable::instance().to_byte(node));
            return;
        }

        // Composition: decompose and recurse
        auto children = store.decompose(node);
        if (!children) {
            throw std::runtime_error("Cannot decode: unknown composition");
        }

        decode_recursive(children->first, store, out);
        decode_recursive(children->second, store, out);
    }
};

} // namespace hartonomous
