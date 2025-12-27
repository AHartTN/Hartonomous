#pragma once

/// DATABASE-BACKED ENCODER
///
/// Single unified encoder that writes directly to PostgreSQL.
/// No in-memory toy stores. Real database operations.
/// Uses PairEncodingCascade for fast O(n) tree building with parallel chunks.

#include "node_ref.hpp"
#include "byte_atom_table.hpp"
#include "composition_store.hpp"
#include "../threading/threading.hpp"
#include "../db/database_store.hpp"
#include <vector>
#include <cstdint>
#include <chrono>

namespace hartonomous {

/// Database-backed encoder with parallel chunk processing.
/// Writes all compositions directly to PostgreSQL via bulk COPY.
class DatabaseEncoder {
private:
    db::DatabaseStore& db_;
    CompositionStore store_;  // Thread-safe store for parallel ops
    std::size_t total_bytes_ = 0;
    std::size_t chunk_size_;
    std::size_t block_size_;

public:
    explicit DatabaseEncoder(db::DatabaseStore& db, 
                            std::size_t chunk_size = 0, 
                            std::size_t block_size = 0) 
        : db_(db) 
    {
        // Auto-tune based on hardware
        std::size_t threads = Threading::default_thread_count();
        
        // Chunk size: aim for ~threads * 4 chunks minimum for good load balancing
        // Default: 64KB per chunk, or smaller if we have many cores
        chunk_size_ = chunk_size > 0 ? chunk_size : std::max<std::size_t>(16384, 65536 / (threads / 4 + 1));
        
        // Block size: cache-line aligned, smaller = more parallelism in tree building
        block_size_ = block_size > 0 ? block_size : 32;
    }

    /// Encode bytes and write all compositions to database.
    /// Returns root NodeRef.
    NodeRef ingest(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};

        total_bytes_ = len;

        std::size_t num_chunks = (len + chunk_size_ - 1) / chunk_size_;
        std::vector<NodeRef> chunk_roots(num_chunks);
        std::vector<CompositionCache> local_caches(num_chunks);

        // Phase 1: Parallel chunk encoding (CPU-bound)
        Threading::parallel_for(num_chunks, [&](std::size_t i) {
            std::size_t start = i * chunk_size_;
            std::size_t end = std::min(start + chunk_size_, len);
            chunk_roots[i] = encode_chunk(data + start, end - start, local_caches[i]);
        });

        // Phase 2: Merge local caches into global store (parallel, store handles locking)
        Threading::parallel_for(num_chunks, [&](std::size_t i) {
            store_.merge(local_caches[i]);
        });

        // Phase 3: Build final tree from chunk roots
        NodeRef root = build_tree(chunk_roots.data(), chunk_roots.size());

        // Phase 4: Write ALL compositions to database via bulk COPY
        flush_to_database();

        return root;
    }

    /// Timing info from last ingest (for profiling)
    mutable std::size_t last_encode_ms_ = 0;
    mutable std::size_t last_merge_ms_ = 0;
    mutable std::size_t last_tree_ms_ = 0;
    mutable std::size_t last_db_ms_ = 0;
    mutable std::size_t last_string_build_ms_ = 0;
    mutable std::size_t last_copy_ms_ = 0;

    /// Encode with detailed timing breakdown
    NodeRef ingest_timed(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};

        total_bytes_ = len;
        auto t0 = std::chrono::high_resolution_clock::now();

        std::size_t num_chunks = (len + chunk_size_ - 1) / chunk_size_;
        std::vector<NodeRef> chunk_roots(num_chunks);
        std::vector<CompositionCache> local_caches(num_chunks);

        Threading::parallel_for(num_chunks, [&](std::size_t i) {
            std::size_t start = i * chunk_size_;
            std::size_t end = std::min(start + chunk_size_, len);
            chunk_roots[i] = encode_chunk(data + start, end - start, local_caches[i]);
        });

        auto t1 = std::chrono::high_resolution_clock::now();
        last_encode_ms_ = std::chrono::duration_cast<std::chrono::milliseconds>(t1 - t0).count();

        Threading::parallel_for(num_chunks, [&](std::size_t i) {
            store_.merge(local_caches[i]);
        });

        auto t2 = std::chrono::high_resolution_clock::now();
        last_merge_ms_ = std::chrono::duration_cast<std::chrono::milliseconds>(t2 - t1).count();

        NodeRef root = build_tree(chunk_roots.data(), chunk_roots.size());

        auto t3 = std::chrono::high_resolution_clock::now();
        last_tree_ms_ = std::chrono::duration_cast<std::chrono::milliseconds>(t3 - t2).count();

        flush_to_database_timed();

        auto t4 = std::chrono::high_resolution_clock::now();
        last_db_ms_ = std::chrono::duration_cast<std::chrono::milliseconds>(t4 - t3).count();

        return root;
    }

    /// Convenience overloads
    NodeRef ingest(const char* text, std::size_t len) {
        return ingest(reinterpret_cast<const std::uint8_t*>(text), len);
    }

    NodeRef ingest(const std::string& text) {
        return ingest(text.data(), text.size());
    }

    /// Get composition count
    [[nodiscard]] std::size_t composition_count() const { return store_.size(); }

    /// Get bytes processed
    [[nodiscard]] std::size_t bytes_processed() const { return total_bytes_; }

    /// Access store for debugging
    [[nodiscard]] const CompositionStore& store() const { return store_; }

private:
    NodeRef encode_chunk(const std::uint8_t* data, std::size_t len, CompositionCache& cache) {
        if (len == 0) return NodeRef{};

        const auto& atoms = ByteAtomTable::instance();

        // Build balanced tree from bytes
        std::vector<NodeRef> level;
        level.reserve((len + block_size_ - 1) / block_size_);

        // First level: create blocks of atoms
        for (std::size_t i = 0; i < len; i += block_size_) {
            std::size_t block_end = std::min(i + block_size_, len);
            level.push_back(build_atom_block(data + i, block_end - i, cache, atoms));
        }

        // Reduce to single root
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

        std::size_t mid = len / 2;
        NodeRef left = build_atom_block(data, mid, cache, atoms);
        NodeRef right = build_atom_block(data + mid, len - mid, cache, atoms);
        return cache.get_or_create(left, right);
    }

    NodeRef build_tree(const NodeRef* nodes, std::size_t count) {
        if (count == 0) return NodeRef{};
        if (count == 1) return nodes[0];
        if (count == 2) return store_.get_or_create(nodes[0], nodes[1]);

        std::size_t mid = count / 2;
        NodeRef left = build_tree(nodes, mid);
        NodeRef right = build_tree(nodes + mid, count - mid);
        return store_.get_or_create(left, right);
    }

    void flush_to_database() {
        const auto& compositions = store_.compositions();
        if (compositions.empty()) return;
        
        // Build COPY data - ONE row per composition
        std::string data;
        data.reserve(compositions.size() * 60);
        
        char buf[128];
        for (const auto& comp : compositions) {
            char* p = buf;
            p = db::DatabaseStore::write_int64(p, comp.parent.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.parent.id_low); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.left.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.left.id_low); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.right.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.right.id_low); *p++ = '\n';
            data.append(buf, static_cast<std::size_t>(p - buf));
        }
        
        db_.bulk_copy_relations(data);
    }

    void flush_to_database_timed() {
        const auto& compositions = store_.compositions();
        if (compositions.empty()) return;
        
        auto t0 = std::chrono::high_resolution_clock::now();
        
        // Build COPY data - ONE row per composition
        std::string data;
        data.reserve(compositions.size() * 60);
        
        char buf[128];
        for (const auto& comp : compositions) {
            char* p = buf;
            p = db::DatabaseStore::write_int64(p, comp.parent.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.parent.id_low); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.left.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.left.id_low); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.right.id_high); *p++ = '\t';
            p = db::DatabaseStore::write_int64(p, comp.right.id_low); *p++ = '\n';
            data.append(buf, static_cast<std::size_t>(p - buf));
        }
        
        auto t1 = std::chrono::high_resolution_clock::now();
        last_string_build_ms_ = std::chrono::duration_cast<std::chrono::milliseconds>(t1 - t0).count();
        
        db_.bulk_copy_relations(data);
        
        auto t2 = std::chrono::high_resolution_clock::now();
        last_copy_ms_ = std::chrono::duration_cast<std::chrono::milliseconds>(t2 - t1).count();
    }
};

} // namespace hartonomous
