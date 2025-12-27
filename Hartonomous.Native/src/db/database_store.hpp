#pragma once

/// DATABASE-BACKED COMPOSITION STORE
///
/// This provides actual read/write to PostgreSQL.
/// Unlike the in-memory CompositionStore, this persists compositions
/// and can reconstruct content from the database alone.
///
/// OPTIMIZED: Bulk loads all compositions in one query for fast decode.

#include "connection.hpp"
#include "schema.hpp"
#include "pg_result.hpp"
#include "copy_builder.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/byte_atom_table.hpp"
#include "../atoms/semantic_decompose.hpp"
#include "../atoms/atom_id.hpp"
#include <libpq-fe.h>
#include <string>
#include <optional>
#include <vector>
#include <unordered_map>
#include <cstdint>

namespace hartonomous::db {

// Fast hash for 128-bit IDs
struct IdHash {
    std::size_t operator()(std::uint64_t key) const noexcept {
        return key * 0x9e3779b97f4a7c15ULL;
    }
};

/// Database-backed composition store.
/// Writes compositions to PostgreSQL and reads them back.
/// This is the REAL store - not a toy in-memory test double.
class DatabaseStore {
    std::string connstr_;
    PgConnection conn_;
    
    // Write buffer for batch operations
    std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> pending_compositions_;
    
    // Read cache - BULK LOADED, uses combined 64-bit key for speed
    mutable std::unordered_map<std::uint64_t, std::pair<NodeRef, NodeRef>, IdHash> decompose_cache_;
    mutable std::unordered_map<std::uint64_t, bool, IdHash> atom_cache_;
    mutable bool cache_loaded_ = false;
    
    static std::uint64_t make_key(std::int64_t high, std::int64_t low) noexcept {
        // Combine into single key - use XOR with rotation for distribution
        return static_cast<std::uint64_t>(high) ^ (static_cast<std::uint64_t>(low) * 0x9e3779b97f4a7c15ULL);
    }

public:
    explicit DatabaseStore(const std::string& connstr)
        : connstr_(connstr), conn_(connstr) {
        pending_compositions_.reserve(100000);
    }
    
    explicit DatabaseStore()
        : connstr_(ConnectionConfig::connection_string())
        , conn_(connstr_) {
        pending_compositions_.reserve(100000);
    }
    
    /// Get raw connection for advanced operations
    PGconn* connection() { return conn_.get(); }
    
    /// Ensure schema exists
    void ensure_schema() {
        conn_.exec_ok(Schema::CREATE_SCHEMA, "ensure_schema");
    }
    
    /// Clear all data (for testing)
    void clear_all() {
        conn_.exec_ok("TRUNCATE atom, composition CASCADE", "clear_all");
        decompose_cache_.clear();
        atom_cache_.clear();
        cache_loaded_ = false;
    }
    
    /// Store a composition (parent = left + right)
    void store_composition(NodeRef parent, NodeRef left, NodeRef right) {
        pending_compositions_.emplace_back(parent, left, right);
    }
    
    /// Fast integer to string - avoids snprintf overhead
    static char* write_int64(char* buf, std::int64_t val) {
        if (val < 0) {
            *buf++ = '-';
            val = -val;
        }
        if (val == 0) {
            *buf++ = '0';
            return buf;
        }
        char temp[20];
        char* p = temp + 20;
        while (val > 0) {
            *--p = '0' + (val % 10);
            val /= 10;
        }
        while (p < temp + 20) {
            *buf++ = *p++;
        }
        return buf;
    }
    
    /// Bulk COPY pre-built relation data directly to database
    void bulk_copy_relations(const std::string& data) {
        if (data.empty()) return;
        conn_.start_copy(Schema::COPY_COMPOSITIONS);
        conn_.put_copy_data(data);
        conn_.end_copy().expect_ok("bulk copy relations");
    }
    
    /// Fast bulk COPY with index optimization - drops indexes, copies, rebuilds
    void bulk_copy_relations_fast(const std::string& data) {
        if (data.empty()) return;
        
        // Drop indexes for faster insert
        PgResult drop1(PQexec(conn_.get(), 
            "DROP INDEX IF EXISTS idx_composition_relation_child"));
        
        conn_.start_copy(Schema::COPY_COMPOSITIONS);
        conn_.put_copy_data(data);
        conn_.end_copy().expect_ok("bulk copy relations");
        
        // Rebuild indexes
        PgResult idx1(PQexec(conn_.get(),
            "CREATE INDEX IF NOT EXISTS idx_composition_relation_child "
            "ON composition_relation (child_hilbert_high, child_hilbert_low)"));
    }
    
    /// Flush pending compositions to database - SINGLE bulk COPY
    /// One row per composition: (parent_high, parent_low, left_high, left_low, right_high, right_low)
    void flush() {
        if (pending_compositions_.empty()) return;
        
        std::string data;
        data.reserve(pending_compositions_.size() * 80);
        
        char buf[256];
        for (const auto& [parent, left, right] : pending_compositions_) {
            char* p = buf;
            p = write_int64(p, parent.id_high); *p++ = '\t';
            p = write_int64(p, parent.id_low); *p++ = '\t';
            p = write_int64(p, left.id_high); *p++ = '\t';
            p = write_int64(p, left.id_low); *p++ = '\t';
            p = write_int64(p, right.id_high); *p++ = '\t';
            p = write_int64(p, right.id_low); *p++ = '\n';
            data.append(buf, static_cast<std::size_t>(p - buf));
        }
        
        conn_.start_copy(Schema::COPY_COMPOSITIONS);
        conn_.put_copy_data(data);
        conn_.end_copy().expect_ok("copy compositions");
        
        pending_compositions_.clear();
    }
    
    /// Clear the read cache (forces fresh reads from DB)
    void clear_cache() const {
        decompose_cache_.clear();
        atom_cache_.clear();
        cache_loaded_ = false;
    }
    
    /// Bulk load ALL compositions into cache using COPY TO STDOUT
    void load_all_compositions() const {
        if (cache_loaded_) return;
        
        decompose_cache_.clear();
        atom_cache_.clear();
        decompose_cache_.reserve(500000);
        atom_cache_.reserve(300);
        
        // Precompute byte atoms (0-255) - deterministic, no DB query needed
        for (std::uint32_t byte = 0; byte < 256; ++byte) {
            AtomId id = SemanticDecompose::get_atom_id(byte);
            atom_cache_[make_key(id.high, id.low)] = true;
        }
        
        // Load all compositions in one query - single row per composition
        PgResult res(PQexec(conn_.get(),
            "COPY (SELECT hilbert_high, hilbert_low, left_high, left_low, "
            "right_high, right_low FROM composition) TO STDOUT"));
        
        if (res.status() == PGRES_COPY_OUT) {
            char* buffer = nullptr;
            int len;
            while ((len = PQgetCopyData(conn_.get(), &buffer, 0)) > 0) {
                const char* ptr = buffer;
                const char* end = buffer + len;
                while (ptr < end) {
                    std::int64_t vals[6];
                    for (int f = 0; f < 6 && ptr < end; ++f) {
                        vals[f] = 0;
                        bool neg = false;
                        if (*ptr == '-') { neg = true; ++ptr; }
                        while (ptr < end && *ptr >= '0' && *ptr <= '9') {
                            vals[f] = vals[f] * 10 + (*ptr - '0');
                            ++ptr;
                        }
                        if (neg) vals[f] = -vals[f];
                        if (ptr < end && (*ptr == '\t' || *ptr == '\n')) ++ptr;
                    }
                    
                    NodeRef left;
                    left.id_high = vals[2];
                    left.id_low = vals[3];
                    left.is_atom = is_atom_cached(left.id_high, left.id_low);
                    
                    NodeRef right;
                    right.id_high = vals[4];
                    right.id_low = vals[5];
                    right.is_atom = is_atom_cached(right.id_high, right.id_low);
                    
                    decompose_cache_[make_key(vals[0], vals[1])] = std::make_pair(left, right);
                }
                PQfreemem(buffer);
            }
            PQgetResult(conn_.get());
        }
        
        cache_loaded_ = true;
    }
    
    /// Decompose a composition - uses bulk-loaded cache
    [[nodiscard]] std::optional<std::pair<NodeRef, NodeRef>> decompose(NodeRef comp) const {
        if (comp.is_atom) {
            return std::nullopt;
        }
        
        // Ensure cache is loaded
        if (!cache_loaded_) {
            load_all_compositions();
        }
        
        auto it = decompose_cache_.find(make_key(comp.id_high, comp.id_low));
        if (it != decompose_cache_.end()) {
            return it->second;
        }
        
        return std::nullopt;
    }
    
    /// Check if an ID is an atom (from cache)
    [[nodiscard]] bool is_atom_cached(std::int64_t high, std::int64_t low) const {
        return atom_cache_.count(make_key(high, low)) > 0;
    }
    
    /// Check if an ID represents an atom (has codepoint)
    [[nodiscard]] bool is_atom_in_db(std::int64_t high, std::int64_t low) const {
        char query[256];
        std::snprintf(query, sizeof(query),
            "SELECT codepoint FROM atom "
            "WHERE hilbert_high = %lld AND hilbert_low = %lld",
            static_cast<long long>(high),
            static_cast<long long>(low));
        
        PgResult res(PQexec(conn_.get(), query));
        if (res.status() != PGRES_TUPLES_OK || res.row_count() == 0) {
            return false;
        }
        
        // If codepoint is not NULL, it's an atom
        const char* val = res.get_value(0, 0);
        return val && val[0] != '\0';
    }
    
    /// Decode a node recursively from database to bytes
    [[nodiscard]] std::vector<std::uint8_t> decode(NodeRef root) const {
        std::vector<std::uint8_t> result;
        decode_recursive(root, result);
        return result;
    }
    
    /// Get composition count from database
    [[nodiscard]] std::size_t composition_count() const {
        PgResult res(PQexec(conn_.get(), "SELECT COUNT(*) FROM composition"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoull(res.get_value(0, 0));
    }
    
    /// Get atom count from database
    [[nodiscard]] std::size_t atom_count() const {
        PgResult res(PQexec(conn_.get(), 
            "SELECT COUNT(*) FROM atom WHERE codepoint IS NOT NULL"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoull(res.get_value(0, 0));
    }

private:
    void decode_recursive(NodeRef root, std::vector<std::uint8_t>& out) const {
        // ITERATIVE decode using explicit stack - no recursion limit
        std::vector<NodeRef> stack;
        stack.reserve(1000000);
        stack.push_back(root);
        
        while (!stack.empty()) {
            NodeRef node = stack.back();
            stack.pop_back();
            
            if (node.id_high == 0 && node.id_low == 0 && !node.is_atom) {
                continue;  // Null node
            }
            
            if (node.is_atom) {
                out.push_back(ByteAtomTable::instance().to_byte(node));
                continue;
            }
            
            // Composition: decompose and push children (right first so left processes first)
            auto children = decompose(node);
            if (!children) {
                throw std::runtime_error("Cannot decode: composition not found in database");
            }
            
            stack.push_back(children->second);  // Right goes on stack first
            stack.push_back(children->first);   // Left processes first
        }
    }
};

} // namespace hartonomous::db
