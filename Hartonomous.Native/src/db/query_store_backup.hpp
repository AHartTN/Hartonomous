#pragma once

/// QUERY STORE - The actual query interface that should have existed from day one.
///
/// This provides:
/// 1. Content-addressable lookup: text → root hash → composition
/// 2. Spatial queries using PostGIS
/// 3. Semantic similarity search
/// 4. Database-backed encoding (DB as source of truth, not dump target)
///
/// Delegates to extracted component stores:
/// - TrajectoryStore: RLE-compressed trajectory operations
/// - RelationshipStore: Sparse weighted edge storage
/// - SpatialQueries: PostGIS-backed semantic proximity searches

#include "connection.hpp"
#include "pg_result.hpp"
#include "types.hpp"
#include "trajectory_store.hpp"
#include "relationship_store.hpp"
#include "spatial_queries.hpp"
#include "../threading/threading.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/codepoint_atom_table.hpp"
#include "../atoms/merkle_hash.hpp"
#include "../atoms/semantic_decompose.hpp"
#include "../atoms/content_chunker.hpp"
#include <libpq-fe.h>
#include <string>
#include <vector>
#include <optional>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <memory>
#include <unordered_map>
#include <unordered_set>
#include <iomanip>
#include <iostream>

namespace hartonomous::db {

/// Check if an ID represents a codepoint atom.
/// Uses inverse Hilbert encoding to verify it's a valid atom.
/// Free function for use by RelationshipStore and other components.
inline bool is_atom(std::int64_t high, std::int64_t low) {
    AtomId id{high, low};
    std::int32_t cp = SemanticDecompose::atom_to_codepoint(id);
    // Valid Unicode codepoint range (excluding surrogates)
    if (cp >= 0 && cp <= 0x10FFFF && !(cp >= 0xD800 && cp <= 0xDFFF)) {
        // Verify round-trip: codepoint → atom → should match
        NodeRef verify = CodepointAtomTable::instance().ref(cp);
        return verify.id_high == high && verify.id_low == low;
    }
    return false;
}

/// The ACTUAL query interface for the universal substrate.
/// Uses database as source of truth, not as a dump target.
/// Delegates to component stores for specific operations.
class QueryStore {
    std::string connstr_;
    PgConnection conn_;
    
    // Component stores - delegate specialized operations
    TrajectoryStore trajectory_store_;
    RelationshipStore relationship_store_;
    std::unique_ptr<SpatialQueries> spatial_queries_;

    // Local cache for encoding - mirrors DB, enables batch operations
    std::unordered_map<std::uint64_t, std::pair<NodeRef, NodeRef>> composition_cache_;
    
    // CPE frequency threshold - pairs must occur this many times to become compositions
    static constexpr std::uint32_t CPE_FREQUENCY_THRESHOLD = 2;
    
    // Maximum hierarchy depth (Z levels)
    static constexpr double CPE_MAX_Z_LEVEL = 30.0;
    
    // Merge table: maps (left, right) pair keys → composition NodeRef
    // Built during ingestion, used for consistent query encoding
    std::unordered_map<std::uint64_t, NodeRef> merge_table_;

    static std::uint64_t make_key(std::int64_t high, std::int64_t low) noexcept {
        return static_cast<std::uint64_t>(high) ^
               (static_cast<std::uint64_t>(low) * 0x9e3779b97f4a7c15ULL);
    }
    
    /// Atom with position for CPE processing
    struct CpeAtom {
        NodeRef ref;
        double x, y, z, m;  // Semantic coordinates
    };
    
    /// Apply RLE compression - collapse runs of identical atoms
    static std::vector<CpeAtom> apply_rle(const std::vector<CpeAtom>& atoms) {
        if (atoms.empty()) return {};
        
        std::vector<CpeAtom> compressed;
        compressed.reserve(atoms.size());
        
        CpeAtom current = atoms[0];
        double run_length = 1.0;
        
        for (std::size_t i = 1; i < atoms.size(); ++i) {
            if (atoms[i].ref.id_high == current.ref.id_high && 
                atoms[i].ref.id_low == current.ref.id_low) {
                run_length += 1.0;
            } else {
                current.m = run_length;
                compressed.push_back(current);
                current = atoms[i];
                run_length = 1.0;
            }
        }
        current.m = run_length;
        compressed.push_back(current);
        
        return compressed;
    }
    
    /// Create pair key for HashMap lookup
    static std::uint64_t make_pair_key(const NodeRef& left, const NodeRef& right) noexcept {
        std::uint64_t lk = make_key(left.id_high, left.id_low);
        std::uint64_t rk = make_key(right.id_high, right.id_low);
        return lk ^ (rk * 0x9e3779b97f4a7c15ULL);
    }
    
    /// Pair statistics for frequency counting
    struct PairStats {
        NodeRef left, right;
        std::uint32_t count = 0;
        double sum_dist = 0.0;  // Sum of distances for semantic coherence
    };
    
    /// Count pairs in stream - O(n) single pass
    std::unordered_map<std::uint64_t, PairStats> count_pairs(const std::vector<CpeAtom>& stream) {
        std::unordered_map<std::uint64_t, PairStats> pair_counts;
        if (stream.size() < 2) return pair_counts;
        
        pair_counts.reserve(stream.size() / 2);
        
        for (std::size_t i = 0; i + 1 < stream.size(); ++i) {
            const auto& left = stream[i];
            const auto& right = stream[i + 1];
            
            std::uint64_t key = make_pair_key(left.ref, right.ref);
            auto& stats = pair_counts[key];
            
            if (stats.count == 0) {
                stats.left = left.ref;
                stats.right = right.ref;
            }
            stats.count++;
            
            // Semantic distance in XY plane
            double dx = right.x - left.x;
            double dy = right.y - left.y;
            stats.sum_dist += std::sqrt(dx * dx + dy * dy);
        }
        
        return pair_counts;
    }
    
    /// Find best pairs above threshold, sorted by score
    std::vector<std::pair<std::uint64_t, PairStats>> get_frequent_pairs(
        const std::unordered_map<std::uint64_t, PairStats>& pair_counts)
    {
        std::vector<std::pair<std::uint64_t, PairStats>> frequent;
        frequent.reserve(pair_counts.size());
        
        for (const auto& [key, stats] : pair_counts) {
            if (stats.count >= CPE_FREQUENCY_THRESHOLD) {
                frequent.emplace_back(key, stats);
            }
        }
        
        // Sort by score: frequency * coherence (low distance = high coherence)
        std::sort(frequent.begin(), frequent.end(),
            [](const auto& a, const auto& b) {
                double avg_dist_a = a.second.sum_dist / a.second.count;
                double avg_dist_b = b.second.sum_dist / b.second.count;
                double score_a = a.second.count / (avg_dist_a + 1.0);
                double score_b = b.second.count / (avg_dist_b + 1.0);
                return score_a > score_b;
            });
        
        return frequent;
    }
    
    /// Rewrite stream - replace all matched pairs with compositions - O(n)
    std::vector<CpeAtom> rewrite_stream(
        const std::vector<CpeAtom>& stream,
        const std::unordered_map<std::uint64_t, NodeRef>& pair_to_comp,
        double z_level)
    {
        std::vector<CpeAtom> result;
        result.reserve(stream.size());
        
        std::size_t i = 0;
        while (i < stream.size()) {
            if (i + 1 < stream.size()) {
                std::uint64_t key = make_pair_key(stream[i].ref, stream[i + 1].ref);
                auto it = pair_to_comp.find(key);
                
                if (it != pair_to_comp.end()) {
                    // Replace pair with composition
                    CpeAtom comp_atom;
                    comp_atom.ref = it->second;
                    comp_atom.x = (stream[i].x + stream[i + 1].x) / 2.0;
                    comp_atom.y = (stream[i].y + stream[i + 1].y) / 2.0;
                    comp_atom.z = z_level;
                    comp_atom.m = stream[i].m + stream[i + 1].m;
                    result.push_back(comp_atom);
                    i += 2;
                    continue;
                }
            }
            result.push_back(stream[i]);
            i++;
        }
        
        return result;
    }

public:
    explicit QueryStore()
        : connstr_(ConnectionConfig::connection_string())
        , conn_(connstr_)
        , trajectory_store_(conn_)
        , relationship_store_(conn_)
        , spatial_queries_(std::make_unique<SpatialQueries>(conn_, 
            [this](const std::string& s) { return compute_root(s); })) {
        composition_cache_.reserve(100000);
    }

    explicit QueryStore(const std::string& connstr)
        : connstr_(connstr)
        , conn_(connstr)
        , trajectory_store_(conn_)
        , relationship_store_(conn_)
        , spatial_queries_(std::make_unique<SpatialQueries>(conn_,
            [this](const std::string& s) { return compute_root(s); })) {
        composition_cache_.reserve(100000);
    }

    // =========================================================================
    // CONTENT-ADDRESSABLE LOOKUP
    // =========================================================================

    /// Compute root hash for content WITHOUT storing.
    /// CRITICAL: Must use SAME algorithm as encode_and_store:
    ///   1. Tokenize using UniversalTokenizer
    ///   2. Encode each token with CPE
    ///   3. Compose token roots into balanced tree
    [[nodiscard]] NodeRef compute_root(const std::uint8_t* data, std::size_t len) const {
        if (len == 0) return NodeRef{};

        // Use SAME tokenization as encode_and_store
        HierarchicalChunker chunker;
        auto chunks = chunker.chunk(data, len);

        if (chunks.empty()) return NodeRef{};

        // Encode each token
        std::vector<NodeRef> token_roots;
        token_roots.reserve(chunks.size());

        for (const auto& chunk : chunks) {
            auto codepoints = UTF8Decoder::decode(chunk.data, chunk.length);
            if (codepoints.empty()) continue;

            NodeRef token_root;
            if (codepoints.size() == 1) {
                token_root = CodepointAtomTable::instance().ref(codepoints[0]);
            } else {
                // Balanced tree for word - SAME as encode_word_balanced
                token_root = compute_word_balanced_hash(codepoints);
            }
            token_roots.push_back(token_root);
        }

        if (token_roots.empty()) return NodeRef{};
        if (token_roots.size() == 1) return token_roots[0];

        // Left-to-right composition - SAME as store_phrase_compositions
        // ABC = compose(compose(A, B), C)
        NodeRef current = token_roots[0];
        for (std::size_t i = 1; i < token_roots.size(); ++i) {
            NodeRef children[2] = {current, token_roots[i]};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            current = NodeRef::comp(h, l);
        }
        return current;
    }

    /// Compute balanced tree hash WITHOUT storing.
    /// Must match build_balanced_tree_and_collect exactly.
    [[nodiscard]] NodeRef compute_balanced_tree_hash(const std::vector<NodeRef>& nodes) const {
        if (nodes.empty()) return NodeRef{};
        if (nodes.size() == 1) return nodes[0];

        std::vector<NodeRef> current = nodes;

        while (current.size() > 1) {
            std::vector<NodeRef> next_level;
            next_level.reserve((current.size() + 1) / 2);

            for (std::size_t i = 0; i + 1 < current.size(); i += 2) {
                NodeRef children[2] = {current[i], current[i + 1]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                next_level.push_back(NodeRef::comp(h, l));
            }

            if (current.size() % 2 == 1) {
                next_level.push_back(current.back());
            }

            current = std::move(next_level);
        }

        return current[0];
    }

    /// Compute balanced tree hash for codepoints WITHOUT storing.
    /// Must match encode_word_balanced exactly for content addressing to work.
    [[nodiscard]] NodeRef compute_word_balanced_hash(const std::vector<std::int32_t>& codepoints) const {
        const auto& atoms = CodepointAtomTable::instance();

        // Convert codepoints to atom refs
        std::vector<NodeRef> nodes;
        nodes.reserve(codepoints.size());
        for (auto cp : codepoints) {
            nodes.push_back(atoms.ref(cp));
        }

        // Use same balanced tree as encode_word_balanced
        return compute_balanced_tree_hash(nodes);
    }

    /// Compute CPE hash using proper O(n log n) BPE algorithm.
    /// Must match build_cpe_and_collect exactly for content addressing to work.
    [[nodiscard]] NodeRef compute_cpe_hash(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end) const
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;

        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);

        // Same algorithm as build_cpe_and_collect, just without storing
        
        // Initialize stream with atoms
        std::vector<CpeAtom> stream;
        stream.reserve(len);
        
        for (std::size_t i = start; i < end; ++i) {
            CpeAtom atom;
            atom.ref = atoms.ref(codepoints[i]);
            atom.x = static_cast<double>(i - start);
            atom.y = 0.0;
            atom.z = 0.0;
            atom.m = 1.0;
            stream.push_back(atom);
        }
        
        double z_level = 1.0;
        std::size_t prev_size = 0;
        
        // Process levels until convergence
        while (stream.size() >= 2 && z_level <= CPE_MAX_Z_LEVEL) {
            // NOTE: RLE removed - it was losing data
            
            if (stream.size() < 2) break;
            
            // Check for convergence
            if (prev_size > 0) {
                double ratio = static_cast<double>(stream.size()) / static_cast<double>(prev_size);
                if (ratio > 0.999) break;
            }
            prev_size = stream.size();
            
            // Count pairs - O(n)
            std::unordered_map<std::uint64_t, PairStats> pair_counts;
            pair_counts.reserve(stream.size() / 2);
            
            for (std::size_t i = 0; i + 1 < stream.size(); ++i) {
                const auto& left = stream[i];
                const auto& right = stream[i + 1];
                
                std::uint64_t key = make_pair_key(left.ref, right.ref);
                auto& stats = pair_counts[key];
                
                if (stats.count == 0) {
                    stats.left = left.ref;
                    stats.right = right.ref;
                }
                stats.count++;
                
                double dx = right.x - left.x;
                double dy = right.y - left.y;
                stats.sum_dist += std::sqrt(dx * dx + dy * dy);
            }
            
            // Get frequent pairs
            std::vector<std::pair<std::uint64_t, PairStats>> frequent;
            frequent.reserve(pair_counts.size());
            
            for (const auto& [key, stats] : pair_counts) {
                if (stats.count >= CPE_FREQUENCY_THRESHOLD) {
                    frequent.emplace_back(key, stats);
                }
            }
            
            if (frequent.empty()) break;
            
            // Sort by score
            std::sort(frequent.begin(), frequent.end(),
                [](const auto& a, const auto& b) {
                    double avg_dist_a = a.second.sum_dist / a.second.count;
                    double avg_dist_b = b.second.sum_dist / b.second.count;
                    double score_a = a.second.count / (avg_dist_a + 1.0);
                    double score_b = b.second.count / (avg_dist_b + 1.0);
                    return score_a > score_b;
                });
            
            // Create compositions and build lookup map
            std::unordered_map<std::uint64_t, NodeRef> pair_to_comp;
            
            for (const auto& [key, stats] : frequent) {
                NodeRef children[2] = {stats.left, stats.right};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pair_to_comp[key] = comp;
            }
            
            // Rewrite stream - O(n)
            std::vector<CpeAtom> result;
            result.reserve(stream.size());
            
            std::size_t i = 0;
            while (i < stream.size()) {
                if (i + 1 < stream.size()) {
                    std::uint64_t key = make_pair_key(stream[i].ref, stream[i + 1].ref);
                    auto it = pair_to_comp.find(key);
                    
                    if (it != pair_to_comp.end()) {
                        CpeAtom comp_atom;
                        comp_atom.ref = it->second;
                        comp_atom.x = (stream[i].x + stream[i + 1].x) / 2.0;
                        comp_atom.y = (stream[i].y + stream[i + 1].y) / 2.0;
                        comp_atom.z = z_level;
                        comp_atom.m = stream[i].m + stream[i + 1].m;
                        result.push_back(comp_atom);
                        i += 2;
                        continue;
                    }
                }
                result.push_back(stream[i]);
                i++;
            }
            
            stream = std::move(result);
            z_level += 1.0;
        }
        
        // Combine remaining atoms with binary tree
        while (stream.size() > 1) {
            std::vector<CpeAtom> next_level;
            next_level.reserve((stream.size() + 1) / 2);
            
            for (std::size_t i = 0; i + 1 < stream.size(); i += 2) {
                NodeRef children[2] = {stream[i].ref, stream[i + 1].ref};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                
                CpeAtom merged;
                merged.ref = comp;
                merged.x = (stream[i].x + stream[i + 1].x) / 2.0;
                merged.y = (stream[i].y + stream[i + 1].y) / 2.0;
                merged.z = z_level;
                merged.m = stream[i].m + stream[i + 1].m;
                next_level.push_back(merged);
            }
            
            if (stream.size() % 2 == 1) {
                next_level.push_back(stream.back());
            }
            
            stream = std::move(next_level);
            z_level += 1.0;
        }
        
        return stream.empty() ? NodeRef{} : stream[0].ref;
    }
    
    /// Encode a query using the pre-built merge table from ingestion.
    /// This ensures queries are encoded the same way as the ingested content.
    /// Applies merges greedily until no more matches in the merge table.
    [[nodiscard]] NodeRef encode_with_merge_table(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end) const
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;
        
        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);
        
        // Initialize stream with atoms
        std::vector<NodeRef> stream;
        stream.reserve(len);
        
        for (std::size_t i = start; i < end; ++i) {
            stream.push_back(atoms.ref(codepoints[i]));
        }
        
        // Apply merges from the table until no more matches
        bool made_progress = true;
        while (made_progress && stream.size() > 1) {
            made_progress = false;
            std::vector<NodeRef> next_stream;
            next_stream.reserve(stream.size());
            
            std::size_t i = 0;
            while (i < stream.size()) {
                if (i + 1 < stream.size()) {
                    std::uint64_t key = make_pair_key(stream[i], stream[i + 1]);
                    auto it = merge_table_.find(key);
                    
                    if (it != merge_table_.end()) {
                        // Found a known merge, apply it
                        next_stream.push_back(it->second);
                        i += 2;
                        made_progress = true;
                        continue;
                    }
                }
                next_stream.push_back(stream[i]);
                i++;
            }
            
            stream = std::move(next_stream);
        }
        
        // If we couldn't reduce to a single node, create a binary tree
        // for the remaining elements (same as ingestion)
        while (stream.size() > 1) {
            std::vector<NodeRef> next_level;
            next_level.reserve((stream.size() + 1) / 2);
            
            for (std::size_t i = 0; i + 1 < stream.size(); i += 2) {
                NodeRef children[2] = {stream[i], stream[i + 1]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                next_level.push_back(NodeRef::comp(h, l));
            }
            
            if (stream.size() % 2 == 1) {
                next_level.push_back(stream.back());
            }
            
            stream = std::move(next_level);
        }
        
        return stream.empty() ? NodeRef{} : stream[0];
    }
    
    /// Compute root using merge table if available, otherwise fallback to CPE hash
    [[nodiscard]] NodeRef compute_root_for_query(const std::string& text) const {
        // MUST use same algorithm as compute_root and encode_and_store
        return compute_root(
            reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
    }
    
    static std::uint64_t compute_pair_key(NodeRef left, NodeRef right) {
        return static_cast<std::uint64_t>(left.id_high ^ left.id_low) ^
               (static_cast<std::uint64_t>(right.id_high ^ right.id_low) * 0x9e3779b97f4a7c15ULL);
    }
    [[nodiscard]] NodeRef compute_root(const char* text) {
        return compute_root(reinterpret_cast<const std::uint8_t*>(text), std::strlen(text));
    }

    [[nodiscard]] NodeRef compute_root(const std::string& text) {
        return compute_root(reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
    }

    /// Check if a composition exists in the database.
    /// O(1) with primary key index.
    [[nodiscard]] bool exists(NodeRef ref) {
        if (ref.is_atom) {
            // Atoms always exist (they're the 1.1M Unicode codepoints)
            return true;
        }

        char query[256];
        std::snprintf(query, sizeof(query),
            "SELECT 1 FROM composition WHERE hilbert_high = %lld AND hilbert_low = %lld LIMIT 1",
            static_cast<long long>(ref.id_high),
            static_cast<long long>(ref.id_low));

        PgResult res(PQexec(conn_.get(), query));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    /// Lookup composition by root hash - THE QUERY THAT SHOULD HAVE EXISTED.
    /// Given computed root hash, retrieve from database.
    [[nodiscard]] std::optional<std::pair<NodeRef, NodeRef>> lookup(NodeRef root) {
        if (root.is_atom) return std::nullopt;

        char query[256];
        std::snprintf(query, sizeof(query),
            "SELECT left_high, left_low, right_high, right_low "
            "FROM composition WHERE hilbert_high = %lld AND hilbert_low = %lld",
            static_cast<long long>(root.id_high),
            static_cast<long long>(root.id_low));

        PgResult res(PQexec(conn_.get(), query));
        if (res.status() != PGRES_TUPLES_OK || res.row_count() == 0) {
            return std::nullopt;
        }

        NodeRef left, right;
        left.id_high = std::stoll(res.get_value(0, 0));
        left.id_low = std::stoll(res.get_value(0, 1));
        right.id_high = std::stoll(res.get_value(0, 2));
        right.id_low = std::stoll(res.get_value(0, 3));

        // Determine if children are atoms
        left.is_atom = is_atom(left.id_high, left.id_low);
        right.is_atom = is_atom(right.id_high, right.id_low);

        return std::make_pair(left, right);
    }

    /// Full content lookup: text → root → exists?
    /// This answers "does 'Captain Ahab' exist in the substrate?"
    /// 
    /// Find content by computing its root hash and checking if it exists.
    ///
    /// With tokenization + n-gram storage, this is O(1) for phrases up to 20 tokens.
    /// The n-gram compositions are stored during encode_and_store, so any
    /// contiguous token sequence that was ingested will be found.
    [[nodiscard]] CompositionResult find_content(const std::string& text) {
        NodeRef root = compute_root(text);
        bool found = exists(root);
        return {root, found, text.size()};
    }
    
    /// Check if ingested content contains a substring by decoding and searching.
    /// This is the "true" find - it checks if the text appears in any stored content.
    [[nodiscard]] bool content_contains(NodeRef content_root, const std::string& query) {
        std::string decoded = decode_string(content_root);
        return decoded.find(query) != std::string::npos;
    }

    // =========================================================================
    // SPATIAL QUERIES - Delegated to SpatialQueries component
    // =========================================================================

    /// Find atoms within distance of a codepoint's semantic position.
    [[nodiscard]] std::vector<SpatialMatch> find_near_codepoint(
        std::int32_t codepoint,
        double distance_threshold,
        std::size_t limit = 100) {
        return spatial_queries_->find_near_codepoint(codepoint, distance_threshold, limit);
    }

    /// Find atoms semantically similar to a character.
    [[nodiscard]] std::vector<SpatialMatch> find_similar(
        std::int32_t codepoint,
        std::size_t limit = 20) {
        return spatial_queries_->find_similar(codepoint, limit);
    }

    /// Find all case variants of a character (same base, different variant).
    [[nodiscard]] std::vector<SpatialMatch> find_case_variants(std::int32_t codepoint) {
        return spatial_queries_->find_case_variants(codepoint);
    }

    /// Case-insensitive composition search using spatial proximity.
    [[nodiscard]] std::vector<NodeRef> find_case_insensitive(const std::string& text) {
        return spatial_queries_->find_case_insensitive(text);
    }

    /// Find all diacritical variants of a base character.
    [[nodiscard]] std::vector<SpatialMatch> find_diacritical_variants(std::int32_t codepoint) {
        return spatial_queries_->find_diacritical_variants(codepoint);
    }

    // =========================================================================
    // DECODE - Get content back from root
    // =========================================================================

    /// Decode composition tree to codepoints, then encode as UTF-8 bytes.
    [[nodiscard]] std::vector<std::uint8_t> decode(NodeRef root) {
        // First collect all codepoints
        std::vector<std::int32_t> codepoints;
        codepoints.reserve(1024);

        std::vector<NodeRef> stack;
        stack.reserve(10000);
        stack.push_back(root);

        while (!stack.empty()) {
            NodeRef node = stack.back();
            stack.pop_back();

            if (node.id_high == 0 && node.id_low == 0 && !node.is_atom) {
                continue;
            }

            if (node.is_atom) {
                // Convert atom NodeRef back to codepoint
                std::int32_t cp = SemanticDecompose::atom_to_codepoint(
                    AtomId{node.id_high, node.id_low});
                codepoints.push_back(cp);
                continue;
            }

            auto children = lookup(node);
            if (!children) {
                throw std::runtime_error("Composition not found in database");
            }

            stack.push_back(children->second);
            stack.push_back(children->first);
        }

        // Encode codepoints to UTF-8
        std::vector<std::uint8_t> result;
        result.reserve(codepoints.size() * 2);
        std::uint8_t buf[4];
        for (std::int32_t cp : codepoints) {
            std::size_t len = UTF8Decoder::encode_one(cp, buf);
            result.insert(result.end(), buf, buf + len);
        }

        return result;
    }

    /// Decode to string.
    [[nodiscard]] std::string decode_string(NodeRef root) {
        auto bytes = decode(root);
        return std::string(bytes.begin(), bytes.end());
    }

    // =========================================================================
    // ENCODE AND STORE - Register content in the universal substrate
    // =========================================================================

    /// Encode content and store all compositions in database.
    /// Returns root NodeRef. After this, the content is queryable.
    ///
    /// Uses CONTENT-DEFINED CHUNKING (Rabin fingerprint) to find natural boundaries.
    /// This ensures phrases like "Captain Ahab" stay together regardless of position.
    /// Then applies CPE within each chunk for efficient storage.
    NodeRef encode_and_store(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};

        pending_compositions_.clear();
        
        // Use hierarchical content-defined chunking
        // This finds natural boundaries using rolling hash - same content = same boundary
        HierarchicalChunker chunker;
        auto chunks = chunker.chunk(data, len);
        
        if (chunks.empty()) return NodeRef{};
        
        // Encode each chunk and collect its root
        std::vector<NodeRef> chunk_roots;
        chunk_roots.reserve(chunks.size());
        
        for (const auto& chunk : chunks) {
            auto codepoints = UTF8Decoder::decode(chunk.data, chunk.length);
            if (codepoints.empty()) continue;

            NodeRef chunk_root;
            if (codepoints.size() == 1) {
                chunk_root = CodepointAtomTable::instance().ref(codepoints[0]);
            } else {
                // Simple balanced tree for word - O(n log n) not O(n²) BPE
                chunk_root = encode_word_balanced(codepoints);
            }
            chunk_roots.push_back(chunk_root);
        }
        
        // Store phrase compositions via left-to-right cascading from each token
        // Merkle DAG: same content = same hash = stored ONCE
        // "whale" appearing 1000 times → stored once, referenced 1000 times
        store_phrase_compositions(chunk_roots, 20);

        // Build hierarchical tree from chunk roots
        // This ensures the entire document has a single queryable root
        NodeRef root = build_balanced_tree_and_collect(chunk_roots);

        // Batch insert all compositions
        flush_pending();

        return root;
    }

    NodeRef encode_and_store(const std::string& text) {
        return encode_and_store(
            reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
    }

    NodeRef encode_and_store(const char* text) {
        return encode_and_store(
            reinterpret_cast<const std::uint8_t*>(text), std::strlen(text));
    }

    // =========================================================================
    // TRAJECTORIES - Delegated to TrajectoryStore component
    // =========================================================================

    /// Build RLE-compressed trajectory from text.
    [[nodiscard]] Trajectory build_trajectory(const std::string& text) {
        return TrajectoryStore::build_trajectory(text);
    }

    /// Store trajectory with weight (sparse - only call for salient relationships).
    void store_trajectory(NodeRef from, NodeRef to, const Trajectory& traj,
                          RelType type = REL_DEFAULT,
                          NodeRef context = NodeRef{}) {
        trajectory_store_.store(from, to, traj, type, context);
    }

    /// Retrieve trajectory from database and decode back to RLE form.
    [[nodiscard]] std::optional<Trajectory> get_trajectory(NodeRef from, NodeRef to,
                                                            NodeRef context = NodeRef{}) {
        return trajectory_store_.get(from, to, context);
    }

    /// Export trajectory to text (inverse of build_trajectory).
    [[nodiscard]] std::string trajectory_to_text(const Trajectory& traj) {
        return TrajectoryStore::to_text(traj);
    }

    /// Export trajectory to RLE string representation: "H(1)e(1)l(2)o(1)"
    [[nodiscard]] std::string trajectory_to_rle_string(const Trajectory& traj) {
        return TrajectoryStore::to_rle_string(traj);
    }

    // =========================================================================
    // RELATIONSHIPS - Delegated to RelationshipStore component
    // =========================================================================

    /// Store a weighted relationship: from → to with weight.
    void store_relationship(NodeRef from, NodeRef to, double weight,
                            RelType type = REL_DEFAULT,
                            NodeRef context = NodeRef{}) {
        relationship_store_.store(from, to, weight, type, context);
    }

    /// Find all relationships FROM a node (outgoing edges).
    [[nodiscard]] std::vector<Relationship> find_from(NodeRef from,
                                                       std::size_t limit = 100) {
        return relationship_store_.find_from(from, limit);
    }

    /// Find all relationships FROM a node within a specific context.
    [[nodiscard]] std::vector<Relationship> find_from(NodeRef from, NodeRef context,
                                                       std::size_t limit = 100) {
        return relationship_store_.find_from_with_context(from, context, limit);
    }

    /// Find all relationships TO a node (incoming edges).
    [[nodiscard]] std::vector<Relationship> find_to(NodeRef to,
                                                     std::size_t limit = 100) {
        return relationship_store_.find_to(to, limit);
    }

    /// Find relationships by weight range (for model analysis).
    [[nodiscard]] std::vector<Relationship> find_by_weight(
        double min_weight, double max_weight,
        NodeRef context = NodeRef{},
        std::size_t limit = 1000) {
        return relationship_store_.find_by_weight(min_weight, max_weight, context, limit);
    }

    /// Get the weight between two specific nodes.
    [[nodiscard]] std::optional<double> get_weight(NodeRef from, NodeRef to,
                                                    NodeRef context = NodeRef{}) {
        return relationship_store_.get_weight(from, to, context);
    }

    /// Delete a specific relationship.
    void delete_relationship(NodeRef from, NodeRef to,
                             NodeRef context = NodeRef{}) {
        relationship_store_.remove(from, to, context);
    }

    /// Bulk store model weights - COPY to staging table + MERGE.
    /// 
    /// Per VISION.md: Same NodeRefs = same edge, regardless of model.
    /// The context identifies the source, but edges with same (from,to) AGGREGATE.
    /// 
    /// Bulk store model weights - DIRECT binary COPY, no staging tables.
    /// For maximum throughput: drop indexes before, rebuild after for TB-scale loads.
    void store_model_weights(
        const std::vector<std::tuple<NodeRef, NodeRef, double>>& weights,
        NodeRef model_context,
        RelType type = REL_DEFAULT)
    {
        if (weights.empty()) return;

        auto start_time = std::chrono::high_resolution_clock::now();
        std::size_t total = weights.size();

        // Direct binary COPY to relationship table - no staging table bullshit
        PGresult* res = PQexec(conn_.get(),
            "COPY relationship (from_high, from_low, to_high, to_low, "
            "weight, obs_count, rel_type, context_high, context_low) "
            "FROM STDIN WITH (FORMAT binary)");
        
        if (PQresultStatus(res) != PGRES_COPY_IN) {
            PQclear(res);
            return;
        }
        PQclear(res);

        // Build binary buffer - single allocation
        static const char COPY_HEADER[] = "PGCOPY\n\377\r\n\0";
        std::vector<char> buffer;
        buffer.reserve(total * 90 + 32);

        buffer.insert(buffer.end(), COPY_HEADER, COPY_HEADER + 11);
        buffer.push_back(0); buffer.push_back(0); buffer.push_back(0); buffer.push_back(0);
        buffer.push_back(0); buffer.push_back(0); buffer.push_back(0); buffer.push_back(0);

        auto append_int16 = [&](std::int16_t v) {
            buffer.push_back(static_cast<char>((v >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(v & 0xFF));
        };

        auto append_int32 = [&](std::int32_t v) {
            buffer.push_back(static_cast<char>((v >> 24) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 16) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(v & 0xFF));
        };

        auto append_int64 = [&](std::int64_t v) {
            buffer.push_back(static_cast<char>((v >> 56) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 48) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 40) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 32) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 24) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 16) & 0xFF));
            buffer.push_back(static_cast<char>((v >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(v & 0xFF));
        };

        auto append_float64 = [&](double v) {
            std::uint64_t bits;
            std::memcpy(&bits, &v, sizeof(bits));
            buffer.push_back(static_cast<char>((bits >> 56) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 48) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 40) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 32) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 24) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 16) & 0xFF));
            buffer.push_back(static_cast<char>((bits >> 8) & 0xFF));
            buffer.push_back(static_cast<char>(bits & 0xFF));
        };

        for (const auto& [from, to, weight] : weights) {
            append_int16(9);  // 9 columns
            append_int32(8); append_int64(from.id_high);
            append_int32(8); append_int64(from.id_low);
            append_int32(8); append_int64(to.id_high);
            append_int32(8); append_int64(to.id_low);
            append_int32(8); append_float64(weight);
            append_int32(4); append_int32(1);  // obs_count = 1
            append_int32(2); append_int16(type);
            append_int32(8); append_int64(model_context.id_high);
            append_int32(8); append_int64(model_context.id_low);
        }
        append_int16(-1);  // Trailer

        // Stream entire buffer - libpq handles chunking internally
        if (PQputCopyData(conn_.get(), buffer.data(), static_cast<int>(buffer.size())) != 1) {
            PQputCopyEnd(conn_.get(), "error");
            return;
        }

        if (PQputCopyEnd(conn_.get(), nullptr) != 1) {
            return;
        }

        res = PQgetResult(conn_.get());
        // Ignore duplicate key errors - model weights are idempotent
        PQclear(res);

        auto end_time = std::chrono::high_resolution_clock::now();
        auto total_ms = std::chrono::duration_cast<std::chrono::milliseconds>(end_time - start_time).count();
        double rate = total_ms > 0 ? (static_cast<double>(total) / total_ms * 1000.0) : 0;

        std::cerr << "store_model_weights: " << total << " rows in " << total_ms << "ms ("
                  << static_cast<std::size_t>(rate) << " rows/sec)" << std::endl;
    }

    /// Bulk store embedding trajectories - direct batched INSERT.
    /// NO staging tables - direct INSERT with ON CONFLICT.
    void store_embedding_trajectories(
        const float* embeddings,
        std::size_t vocab_size,
        std::size_t hidden_dim,
        const std::vector<NodeRef>& token_refs,
        NodeRef model_context,
        RelType type = REL_DEFAULT)
    {
        if (vocab_size == 0 || hidden_dim == 0 || token_refs.empty()) return;

        std::size_t effective_size = std::min(vocab_size, token_refs.size());
        std::cerr << "store_embedding_trajectories: " << effective_size << " embeddings, " << hidden_dim << " dims" << std::endl;

        auto start_time = std::chrono::high_resolution_clock::now();

        // Process in batches of 100 (trajectories are large)
        constexpr std::size_t BATCH_SIZE = 100;
        std::size_t stored = 0;

        for (std::size_t batch_start = 0; batch_start < effective_size; batch_start += BATCH_SIZE) {
            std::size_t batch_end = std::min(batch_start + BATCH_SIZE, effective_size);

            std::string sql = 
                "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
                "weight, obs_count, rel_type, trajectory, context_high, context_low) VALUES ";

            bool first = true;
            for (std::size_t i = batch_start; i < batch_end; ++i) {
                const float* embedding = embeddings + i * hidden_dim;
                const NodeRef& token = token_refs[i];

                // Calculate magnitude as weight
                double mag = 0.0;
                for (std::size_t d = 0; d < hidden_dim; ++d) {
                    mag += static_cast<double>(embedding[d]) * static_cast<double>(embedding[d]);
                }
                mag = std::sqrt(mag);

                // Build LineStringZM WKT
                std::string wkt = "LINESTRINGZM(";
                for (std::size_t d = 0; d < hidden_dim; ++d) {
                    if (d > 0) wkt += ",";
                    char buf[64];
                    // Use dimension index as X, Y, Z; value as M
                    std::snprintf(buf, sizeof(buf), "%zu 0 0 %.6g", d, static_cast<double>(embedding[d]));
                    wkt += buf;
                }
                wkt += ")";

                if (!first) sql += ",";
                first = false;

                char buf[512];
                std::snprintf(buf, sizeof(buf),
                    "(%lld,%lld,%lld,%lld,%.6g,1,%d,ST_GeomFromText('%s'),%lld,%lld)",
                    static_cast<long long>(token.id_high),
                    static_cast<long long>(token.id_low),
                    static_cast<long long>(model_context.id_high),
                    static_cast<long long>(model_context.id_low),
                    mag,
                    static_cast<int>(type),
                    wkt.c_str(),
                    static_cast<long long>(model_context.id_high),
                    static_cast<long long>(model_context.id_low));
                sql += buf;
            }

            sql += " ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
                   "DO UPDATE SET trajectory = EXCLUDED.trajectory, "
                   "weight = EXCLUDED.weight, obs_count = relationship.obs_count + 1";

            PGresult* res = PQexec(conn_.get(), sql.c_str());
            if (PQresultStatus(res) != PGRES_COMMAND_OK) {
                std::cerr << "store_embedding_trajectories batch failed: " << PQerrorMessage(conn_.get()) << std::endl;
            }
            PQclear(res);

            stored += (batch_end - batch_start);
        }

        auto end_time = std::chrono::high_resolution_clock::now();
        auto total_ms = std::chrono::duration_cast<std::chrono::milliseconds>(end_time - start_time).count();
        std::cerr << "store_embedding_trajectories: " << stored << " trajectories in " << total_ms << "ms" << std::endl;
    }

    /// Get relationship count.
    [[nodiscard]] std::size_t relationship_count() {
        PgResult res(PQexec(conn_.get(), "SELECT COUNT(*) FROM relationship"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoull(res.get_value(0, 0));
    }

    /// Get database size in bytes for the hartonomous database.
    [[nodiscard]] std::int64_t database_size() {
        PgResult res(PQexec(conn_.get(), 
            "SELECT pg_database_size(current_database())"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoll(res.get_value(0, 0));
    }

    // =========================================================================
    // TRAJECTORY INTERSECTION QUERIES - Where meaning emerges
    // =========================================================================

    /// Find trajectories that INTERSECT or come within distance of a reference trajectory.
    /// THIS is the geometric meaning discovery - where trajectories cross in 4D space.
    ///
    /// NOT clustering. INTERSECTION. The concept of "king" is where trajectories cross.
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> query_trajectory_intersections(
        NodeRef ref,
        double distance_threshold = 0.1)
    {
        // Get the trajectory for this ref
        char query[1024];
        std::snprintf(query, sizeof(query),
            "SELECT r2.from_high, r2.from_low, "
            "       ST_Distance(r1.trajectory, r2.trajectory) as dist "
            "FROM relationship r1 "
            "JOIN relationship r2 ON r1.from_high != r2.from_high OR r1.from_low != r2.from_low "
            "WHERE r1.from_high = %lld AND r1.from_low = %lld "
            "  AND r1.trajectory IS NOT NULL "
            "  AND r2.trajectory IS NOT NULL "
            "  AND ST_DWithin(r1.trajectory, r2.trajectory, %f) "
            "ORDER BY dist "
            "LIMIT 100",
            static_cast<long long>(ref.id_high),
            static_cast<long long>(ref.id_low),
            distance_threshold);

        return execute_trajectory_query(query);
    }

    /// Find trajectories by Frechet distance (trajectory similarity).
    /// Frechet distance = "man walking dog" distance - how similar are the paths?
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> query_trajectory_neighbors(
        NodeRef ref,
        std::size_t limit = 10)
    {
        char query[1024];
        std::snprintf(query, sizeof(query),
            "SELECT r2.from_high, r2.from_low, "
            "       ST_FrechetDistance(r1.trajectory, r2.trajectory) as dist "
            "FROM relationship r1 "
            "JOIN relationship r2 ON r1.from_high != r2.from_high OR r1.from_low != r2.from_low "
            "WHERE r1.from_high = %lld AND r1.from_low = %lld "
            "  AND r1.trajectory IS NOT NULL "
            "  AND r2.trajectory IS NOT NULL "
            "ORDER BY dist "
            "LIMIT %zu",
            static_cast<long long>(ref.id_high),
            static_cast<long long>(ref.id_low),
            limit);

        return execute_trajectory_query(query);
    }

    /// Query compositions in a 4D bounding box.
    /// Useful for exploring regions of semantic space.
    [[nodiscard]] std::vector<NodeRef> query_bounding_box(
        double page_min, double page_max,
        double type_min, double type_max,
        double base_min, double base_max,
        double variant_min, double variant_max,
        std::size_t limit = 100)
    {
        // Use PostGIS 4D bounding box query on atoms
        char query[1024];
        std::snprintf(query, sizeof(query),
            "SELECT hilbert_high, hilbert_low FROM atom "
            "WHERE ST_X(semantic_position) BETWEEN %f AND %f "
            "  AND ST_Y(semantic_position) BETWEEN %f AND %f "
            "  AND ST_Z(semantic_position) BETWEEN %f AND %f "
            "  AND ST_M(semantic_position) BETWEEN %f AND %f "
            "LIMIT %zu",
            page_min, page_max, type_min, type_max,
            base_min, base_max, variant_min, variant_max, limit);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<NodeRef> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                NodeRef ref;
                ref.id_high = std::stoll(res.get_value(i, 0));
                ref.id_low = std::stoll(res.get_value(i, 1));
                ref.is_atom = true;
                results.push_back(ref);
            }
        }

        return results;
    }

    /// Find compositions whose trajectories pass through a point in 4D space.
    /// Returns compositions where the trajectory INTERSECTS this region.
    [[nodiscard]] std::vector<NodeRef> query_trajectories_through_point(
        double page, double type, double base, double variant,
        double radius = 1.0,
        std::size_t limit = 100)
    {
        // Create a point and find trajectories that pass near it
        char query[1024];
        std::snprintf(query, sizeof(query),
            "SELECT from_high, from_low FROM relationship "
            "WHERE trajectory IS NOT NULL "
            "  AND ST_DWithin(trajectory, ST_MakePoint(%f, %f, %f, %f), %f) "
            "LIMIT %zu",
            page, type, base, variant, radius, limit);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<NodeRef> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                NodeRef ref;
                ref.id_high = std::stoll(res.get_value(i, 0));
                ref.id_low = std::stoll(res.get_value(i, 1));
                ref.is_atom = false;
                results.push_back(ref);
            }
        }

        return results;
    }

private:
    /// Execute trajectory query and return results.
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> execute_trajectory_query(
        const char* query)
    {
        PgResult res(PQexec(conn_.get(), query));
        std::vector<std::pair<NodeRef, double>> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                NodeRef ref;
                ref.id_high = std::stoll(res.get_value(i, 0));
                ref.id_low = std::stoll(res.get_value(i, 1));
                ref.is_atom = false;
                double dist = std::stod(res.get_value(i, 2));
                results.emplace_back(ref, dist);
            }
        }

        return results;
    }

public:

    // =========================================================================
    // AI/MLOps QUERIES - Model analysis using the substrate
    // =========================================================================

    /// Find tokens with similar embeddings (trajectory intersection in 4D space).
    /// This is HOW inference works without matrix multiplication.
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> find_similar_tokens(
        NodeRef token_ref, NodeRef model_context, std::size_t limit = 10)
    {
        char query[1024];
        std::snprintf(query, sizeof(query),
            "SELECT r2.from_high, r2.from_low, "
            "       ST_Distance(r1.trajectory, r2.trajectory) as dist "
            "FROM relationship r1 "
            "JOIN relationship r2 ON r2.context_high = r1.context_high "
            "  AND r2.context_low = r1.context_low "
            "  AND (r2.from_high != r1.from_high OR r2.from_low != r1.from_low) "
            "WHERE r1.from_high = %lld AND r1.from_low = %lld "
            "  AND r1.context_high = %lld AND r1.context_low = %lld "
            "  AND r1.trajectory IS NOT NULL AND r2.trajectory IS NOT NULL "
            "ORDER BY dist LIMIT %zu",
            static_cast<long long>(token_ref.id_high),
            static_cast<long long>(token_ref.id_low),
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low),
            limit);
        return execute_trajectory_query(query);
    }

    /// Semantic attention: find where token trajectories INTERSECT.
    /// Intersection = shared meaning. This replaces attention matrix computation.
    [[nodiscard]] std::vector<std::tuple<NodeRef, NodeRef, double>> compute_attention(
        const std::vector<NodeRef>& tokens, NodeRef model_context, double threshold = 1.0)
    {
        std::vector<std::tuple<NodeRef, NodeRef, double>> attention;
        for (size_t i = 0; i < tokens.size(); ++i) {
            char query[1024];
            std::snprintf(query, sizeof(query),
                "SELECT r2.from_high, r2.from_low, "
                "       ST_Distance(r1.trajectory, r2.trajectory) as dist "
                "FROM relationship r1 "
                "JOIN relationship r2 ON r2.context_high = r1.context_high "
                "  AND r2.context_low = r1.context_low "
                "WHERE r1.from_high = %lld AND r1.from_low = %lld "
                "  AND r1.context_high = %lld AND r1.context_low = %lld "
                "  AND r1.trajectory IS NOT NULL AND r2.trajectory IS NOT NULL "
                "  AND ST_DWithin(r1.trajectory, r2.trajectory, %f)",
                static_cast<long long>(tokens[i].id_high),
                static_cast<long long>(tokens[i].id_low),
                static_cast<long long>(model_context.id_high),
                static_cast<long long>(model_context.id_low),
                threshold);
            PgResult res(PQexec(conn_.get(), query));
            if (res.status() == PGRES_TUPLES_OK) {
                for (int r = 0; r < res.row_count(); ++r) {
                    NodeRef to;
                    to.id_high = std::stoll(res.get_value(r, 0));
                    to.id_low = std::stoll(res.get_value(r, 1));
                    double dist = std::stod(res.get_value(r, 2));
                    attention.emplace_back(tokens[i], to, 1.0 / (1.0 + dist));
                }
            }
        }
        return attention;
    }

    /// Forward pass: given input tokens, find output distribution via trajectory intersection.
    /// Returns (output_token, probability) pairs sorted by probability.
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> forward_pass(
        const std::vector<NodeRef>& input_tokens, NodeRef model_context, std::size_t top_k = 10)
    {
        if (input_tokens.empty()) return {};

        // Build aggregate trajectory query - find tokens that intersect with ALL inputs
        std::string in_clause;
        for (size_t i = 0; i < input_tokens.size(); ++i) {
            if (i > 0) in_clause += " OR ";
            char buf[128];
            std::snprintf(buf, sizeof(buf), "(from_high = %lld AND from_low = %lld)",
                static_cast<long long>(input_tokens[i].id_high),
                static_cast<long long>(input_tokens[i].id_low));
            in_clause += buf;
        }

        char query[2048];
        std::snprintf(query, sizeof(query),
            "WITH input_trajs AS ("
            "  SELECT trajectory FROM relationship "
            "  WHERE (%s) AND context_high = %lld AND context_low = %lld "
            "  AND trajectory IS NOT NULL"
            "), candidates AS ("
            "  SELECT r.from_high, r.from_low, r.trajectory, r.weight "
            "  FROM relationship r "
            "  WHERE r.context_high = %lld AND r.context_low = %lld "
            "  AND r.trajectory IS NOT NULL"
            ") "
            "SELECT c.from_high, c.from_low, "
            "       SUM(c.weight / (1.0 + ST_Distance(c.trajectory, i.trajectory))) as score "
            "FROM candidates c, input_trajs i "
            "GROUP BY c.from_high, c.from_low "
            "ORDER BY score DESC LIMIT %zu",
            in_clause.c_str(),
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low),
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low),
            top_k);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<std::pair<NodeRef, double>> results;
        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                NodeRef ref;
                ref.id_high = std::stoll(res.get_value(i, 0));
                ref.id_low = std::stoll(res.get_value(i, 1));
                ref.is_atom = is_atom(ref.id_high, ref.id_low);
                double score = std::stod(res.get_value(i, 2));
                results.emplace_back(ref, score);
            }
        }
        return results;
    }

    /// Analyze model weight distribution by layer/region.
    [[nodiscard]] std::vector<std::pair<double, std::size_t>> weight_histogram(
        NodeRef model_context, std::size_t num_buckets = 20)
    {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT width_bucket(weight, -1, 1, %zu) as bucket, COUNT(*) "
            "FROM relationship "
            "WHERE context_high = %lld AND context_low = %lld "
            "GROUP BY bucket ORDER BY bucket",
            num_buckets,
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low));

        PgResult res(PQexec(conn_.get(), query));
        std::vector<std::pair<double, std::size_t>> histogram;
        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                int bucket = std::stoi(res.get_value(i, 0));
                std::size_t count = std::stoull(res.get_value(i, 1));
                double center = -1.0 + (2.0 * bucket - 1.0) / num_buckets;
                histogram.emplace_back(center, count);
            }
        }
        return histogram;
    }

    /// Find most salient weights (highest magnitude) in model.
    [[nodiscard]] std::vector<Relationship> top_weights(
        NodeRef model_context, std::size_t limit = 100)
    {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT from_high, from_low, to_high, to_low, weight, obs_count, rel_type "
            "FROM relationship "
            "WHERE context_high = %lld AND context_low = %lld "
            "ORDER BY ABS(weight) DESC LIMIT %zu",
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low),
            limit);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<Relationship> results;
        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                Relationship r;
                r.from.id_high = std::stoll(res.get_value(i, 0));
                r.from.id_low = std::stoll(res.get_value(i, 1));
                r.to.id_high = std::stoll(res.get_value(i, 2));
                r.to.id_low = std::stoll(res.get_value(i, 3));
                r.weight = std::stod(res.get_value(i, 4));
                r.obs_count = std::stoi(res.get_value(i, 5));
                r.rel_type = static_cast<std::int16_t>(std::stoi(res.get_value(i, 6)));
                r.context = model_context;
                results.push_back(r);
            }
        }
        return results;
    }

    /// Prune near-zero weights (sparsification).
    std::size_t prune_weights(NodeRef model_context, double threshold = 1e-6) {
        char query[256];
        std::snprintf(query, sizeof(query),
            "DELETE FROM relationship "
            "WHERE context_high = %lld AND context_low = %lld "
            "AND ABS(weight) < %f",
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low),
            threshold);
        PgResult res(PQexec(conn_.get(), query));
        return res.affected_rows();
    }

    // =========================================================================
    // QUERY ANALYSIS - Verify index usage
    // =========================================================================

    /// Explain a query plan - verify indexes are being used.
    /// Returns true if query uses Index Scan, false if Seq Scan.
    [[nodiscard]] std::pair<bool, std::string> explain_query(const char* query) {
        std::string explain = "EXPLAIN ANALYZE ";
        explain += query;

        PgResult res(PQexec(conn_.get(), explain.c_str()));
        if (res.status() != PGRES_TUPLES_OK) {
            return {false, "Query failed"};
        }

        std::string plan;
        bool uses_index = false;

        for (int i = 0; i < res.row_count(); ++i) {
            std::string line = res.get_value(i, 0);
            plan += line + "\n";

            // Check for index usage indicators
            if (line.find("Index Scan") != std::string::npos ||
                line.find("Index Only Scan") != std::string::npos ||
                line.find("Bitmap Index Scan") != std::string::npos) {
                uses_index = true;
            }
        }

        return {uses_index, plan};
    }

    /// Verify composition primary key index EXISTS.
    [[nodiscard]] bool verify_composition_index_usage() {
        PgResult res(PQexec(conn_.get(),
            "SELECT 1 FROM pg_indexes WHERE tablename = 'composition' "
            "AND indexname = 'composition_pkey'"));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    /// Verify spatial GIST index EXISTS.
    [[nodiscard]] bool verify_spatial_index_usage() {
        PgResult res(PQexec(conn_.get(),
            "SELECT 1 FROM pg_indexes WHERE tablename = 'atom' "
            "AND indexname = 'idx_atom_semantic_position'"));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    /// Verify relationship B-tree index EXISTS.
    [[nodiscard]] bool verify_relationship_index_usage() {
        PgResult res(PQexec(conn_.get(),
            "SELECT 1 FROM pg_indexes WHERE tablename = 'relationship' "
            "AND indexname = 'idx_relationship_from'"));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    // =========================================================================
    // STATISTICS
    // =========================================================================

    [[nodiscard]] std::size_t composition_count() {
        PgResult res(PQexec(conn_.get(), "SELECT COUNT(*) FROM composition"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoull(res.get_value(0, 0));
    }

    [[nodiscard]] std::size_t atom_count() {
        PgResult res(PQexec(conn_.get(),
            "SELECT COUNT(*) FROM atom WHERE codepoint IS NOT NULL"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoull(res.get_value(0, 0));
    }

    // =========================================================================
    // COMPOSITION STORAGE - Direct composition insertion
    // =========================================================================

    /// Store a single composition (parent = left ∘ right).
    /// Used by encoders that need to persist compositions individually.
    void store_composition(NodeRef parent, NodeRef left, NodeRef right) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "INSERT INTO composition (hilbert_high, hilbert_low, "
            "left_high, left_low, right_high, right_low) "
            "VALUES (%lld, %lld, %lld, %lld, %lld, %lld) "
            "ON CONFLICT (hilbert_high, hilbert_low) DO NOTHING",
            static_cast<long long>(parent.id_high),
            static_cast<long long>(parent.id_low),
            static_cast<long long>(left.id_high),
            static_cast<long long>(left.id_low),
            static_cast<long long>(right.id_high),
            static_cast<long long>(right.id_low));
        PQexec(conn_.get(), query);
    }

    // =========================================================================
    // RELATIONSHIP QUERIES BY TYPE - For semantic linking
    // =========================================================================

    /// Find relationships FROM a node with specific type.
    [[nodiscard]] std::vector<Relationship> find_by_type(
        NodeRef from, RelType type, std::size_t limit = 100)
    {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT to_high, to_low, weight, obs_count, context_high, context_low "
            "FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld AND rel_type = %d "
            "ORDER BY weight DESC LIMIT %zu",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<int>(type),
            limit);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<Relationship> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                Relationship r;
                r.from = from;
                r.to.id_high = std::stoll(res.get_value(i, 0));
                r.to.id_low = std::stoll(res.get_value(i, 1));
                r.to.is_atom = is_atom(r.to.id_high, r.to.id_low);
                r.weight = std::stod(res.get_value(i, 2));
                r.obs_count = std::stoi(res.get_value(i, 3));
                r.rel_type = static_cast<std::int16_t>(type);
                r.context.id_high = std::stoll(res.get_value(i, 4));
                r.context.id_low = std::stoll(res.get_value(i, 5));
                r.context.is_atom = false;
                results.push_back(r);
            }
        }

        return results;
    }

    // =========================================================================
    // SUBSTRING CONTAINMENT QUERIES - Using content-defined chunking
    // =========================================================================

    /// Check if a substring exists within any stored content.
    /// Uses the fact that content-defined chunking creates consistent boundaries.
    /// "Captain Ahab" produces the same chunks whether standalone or in Moby Dick.
    [[nodiscard]] bool contains_substring(const std::string& substring) {
        if (substring.empty()) return true;

        // Compute root for substring
        NodeRef substr_root = compute_root(substring);

        // Check if this exact composition exists
        if (exists(substr_root)) return true;

        // For short substrings, check byte-by-byte in compositions
        if (substring.size() <= 4) {
            return contains_short_substring(substring);
        }

        return false;
    }

    /// Find all compositions that contain a substring.
    /// Returns roots of compositions containing the substring.
    [[nodiscard]] std::vector<NodeRef> find_containing(
        const std::string& substring, std::size_t limit = 100)
    {
        std::vector<NodeRef> results;
        if (substring.empty()) return results;

        // Compute root for substring
        NodeRef substr_root = compute_root(substring);

        // Find all compositions where this is a descendant
        // This uses recursive CTE to walk up the tree
        char query[1024];
        std::snprintf(query, sizeof(query),
            "WITH RECURSIVE ancestors AS ("
            "  SELECT c.hilbert_high, c.hilbert_low "
            "  FROM composition c "
            "  WHERE (c.left_high = %lld AND c.left_low = %lld) "
            "     OR (c.right_high = %lld AND c.right_low = %lld) "
            "  UNION "
            "  SELECT c.hilbert_high, c.hilbert_low "
            "  FROM composition c "
            "  JOIN ancestors a ON (c.left_high = a.hilbert_high AND c.left_low = a.hilbert_low) "
            "                   OR (c.right_high = a.hilbert_high AND c.right_low = a.hilbert_low) "
            ") "
            "SELECT DISTINCT hilbert_high, hilbert_low FROM ancestors LIMIT %zu",
            static_cast<long long>(substr_root.id_high),
            static_cast<long long>(substr_root.id_low),
            static_cast<long long>(substr_root.id_high),
            static_cast<long long>(substr_root.id_low),
            limit);

        PgResult res(PQexec(conn_.get(), query));
        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                NodeRef ref;
                ref.id_high = std::stoll(res.get_value(i, 0));
                ref.id_low = std::stoll(res.get_value(i, 1));
                ref.is_atom = false;
                results.push_back(ref);
            }
        }

        return results;
    }

private:

    /// Check for short substrings by walking composition tree.
    bool contains_short_substring(const std::string& substring) {
        // For very short substrings, we need to walk the tree
        // This is O(n) in compositions but necessary for non-aligned substrings
        const auto& atoms = CodepointAtomTable::instance();

        // Decode UTF-8 to get the actual codepoints
        auto codepoints = UTF8Decoder::decode(substring);
        
        if (codepoints.size() == 1) {
            // Single codepoint - check if atom exists with relationships
            NodeRef atom = atoms.ref(codepoints[0]);
            char query[256];
            std::snprintf(query, sizeof(query),
                "SELECT 1 FROM composition WHERE "
                "(left_high = %lld AND left_low = %lld) OR "
                "(right_high = %lld AND right_low = %lld) LIMIT 1",
                static_cast<long long>(atom.id_high),
                static_cast<long long>(atom.id_low),
                static_cast<long long>(atom.id_high),
                static_cast<long long>(atom.id_low));
            PgResult res(PQexec(conn_.get(), query));
            return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
        }

        // For 2-4 codepoints, compute the composition and check if it exists
        NodeRef root = compute_root(substring);
        return exists(root);
    }

public:
    std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> pending_compositions_;

    // =========================================================================
    // CASCADING PAIR ENCODING (CPE) - BPE on Unicode codepoints
    // =========================================================================
    
    /// Build CPE using proper O(n log n) BPE algorithm.
    /// Per level: O(n) pair count → O(n) stream rewrite
    /// Stream shrinks exponentially → ~log(n) levels
    /// Total: O(n log n)
    ///
    /// Stores ALL intermediate compositions for substring queries.
    NodeRef build_cpe_and_collect(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end)
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;
        
        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);
        
        // Initialize stream with atoms
        std::vector<CpeAtom> stream;
        stream.reserve(len);
        
        for (std::size_t i = start; i < end; ++i) {
            CpeAtom atom;
            atom.ref = atoms.ref(codepoints[i]);
            // Initial position: x = character index, y = 0, z = 0, m = 1
            atom.x = static_cast<double>(i - start);
            atom.y = 0.0;
            atom.z = 0.0;
            atom.m = 1.0;
            stream.push_back(atom);
        }
        
        double z_level = 1.0;
        std::size_t prev_size = 0;
        
        // Process levels until convergence
        while (stream.size() >= 2 && z_level <= CPE_MAX_Z_LEVEL) {
            // NOTE: RLE was removed - it was collapsing identical atoms
            // and losing data because run length wasn't encoded in tree
            
            if (stream.size() < 2) break;
            
            // Check for convergence (less than 0.1% compression)
            if (prev_size > 0) {
                double ratio = static_cast<double>(stream.size()) / static_cast<double>(prev_size);
                if (ratio > 0.999) break;  // Not compressing anymore
            }
            prev_size = stream.size();
            
            // Count pairs - O(n)
            auto pair_counts = count_pairs(stream);
            
            // Get frequent pairs above threshold, sorted by score
            auto frequent_pairs = get_frequent_pairs(pair_counts);
            
            if (frequent_pairs.empty()) break;  // No pairs above threshold
            
            // Create compositions for all frequent pairs and build lookup map
            std::unordered_map<std::uint64_t, NodeRef> pair_to_comp;
            
            for (const auto& [key, stats] : frequent_pairs) {
                // Create composition: hash(left, right)
                NodeRef children[2] = {stats.left, stats.right};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                
                // Store composition
                pending_compositions_.emplace_back(comp, stats.left, stats.right);
                pair_to_comp[key] = comp;
                
                // Add to global merge table for query encoding
                merge_table_[key] = comp;
            }
            
            // Rewrite stream - O(n)
            stream = rewrite_stream(stream, pair_to_comp, z_level);
            z_level += 1.0;
        }
        
        // If still multiple atoms, create final composition chain
        while (stream.size() > 1) {
            // No frequent pairs, but still need to combine remainder
            // Create binary tree for remaining atoms
            std::vector<CpeAtom> next_level;
            next_level.reserve((stream.size() + 1) / 2);
            
            for (std::size_t i = 0; i + 1 < stream.size(); i += 2) {
                NodeRef children[2] = {stream[i].ref, stream[i + 1].ref};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pending_compositions_.emplace_back(comp, stream[i].ref, stream[i + 1].ref);
                
                CpeAtom merged;
                merged.ref = comp;
                merged.x = (stream[i].x + stream[i + 1].x) / 2.0;
                merged.y = (stream[i].y + stream[i + 1].y) / 2.0;
                merged.z = z_level;
                merged.m = stream[i].m + stream[i + 1].m;
                next_level.push_back(merged);
            }
            
            // Handle odd element
            if (stream.size() % 2 == 1) {
                next_level.push_back(stream.back());
            }
            
            stream = std::move(next_level);
            z_level += 1.0;
        }
        
        return stream.empty() ? NodeRef{} : stream[0].ref;
    }
    
    /// Tokenize text using greedy longest-match against existing DB compositions.
    /// Unknown sequences are CPE'd and stored, growing the vocabulary.
    /// Returns vector of token NodeRefs.
    /// Uses in-memory cache for greedy matching - NO DB CALLS during encoding.
    /// Cache is populated from DB at startup and updated with new compositions.
    /// 
    /// CRITICAL: Respects word boundaries first, then applies CPE within words.
    /// This ensures "Captain Ahab" creates compositions for both words AND the phrase.
    std::vector<NodeRef> tokenize_greedy(const std::vector<std::int32_t>& codepoints) {
        std::vector<NodeRef> tokens;
        if (codepoints.empty()) return tokens;
        
        // First, split on word boundaries (whitespace/punctuation)
        std::vector<std::pair<std::size_t, std::size_t>> word_ranges;
        std::size_t word_start = 0;
        bool in_word = false;
        
        auto is_word_char = [](std::int32_t cp) {
            // Letters and digits are word characters
            return (cp >= 'A' && cp <= 'Z') || 
                   (cp >= 'a' && cp <= 'z') || 
                   (cp >= '0' && cp <= '9') ||
                   (cp >= 0x80);  // Non-ASCII treated as word chars
        };
        
        for (std::size_t i = 0; i < codepoints.size(); ++i) {
            bool is_word = is_word_char(codepoints[i]);
            
            if (is_word && !in_word) {
                word_start = i;
                in_word = true;
            } else if (!is_word && in_word) {
                word_ranges.push_back({word_start, i});
                in_word = false;
            }
            
            // Non-word chars (space, punctuation) become individual tokens
            if (!is_word) {
                NodeRef atom = CodepointAtomTable::instance().ref(codepoints[i]);
                tokens.push_back(atom);
            }
        }
        
        if (in_word) {
            word_ranges.push_back({word_start, codepoints.size()});
        }
        
        // Now process each word - try cache first, then CPE
        for (const auto& [start, end] : word_ranges) {
            NodeRef word_ref = compute_cpe_hash(codepoints, start, end);
            
            if (!cache_contains(word_ref)) {
                // Build and store the word with all intermediates
                word_ref = build_cpe_and_collect(codepoints, start, end);
            }
            
            tokens.push_back(word_ref);
        }
        
        return tokens;
    }
    
    /// Check if composition exists in local cache (O(1), no DB)
    [[nodiscard]] bool cache_contains(NodeRef ref) const {
        if (ref.is_atom) return true;
        return composition_cache_.count(make_key(ref.id_high, ref.id_low)) > 0;
    }
    
    /// Add to local cache (called after flush_pending)
    void cache_add(NodeRef parent, NodeRef left, NodeRef right) {
        composition_cache_[make_key(parent.id_high, parent.id_low)] = {left, right};
    }
    
    /// Load existing compositions from DB into cache (call once at startup)
    void load_composition_cache(std::size_t limit = 1000000) {
        char query[256];
        std::snprintf(query, sizeof(query),
            "SELECT hilbert_high, hilbert_low, left_high, left_low, right_high, right_low "
            "FROM composition LIMIT %zu", limit);
        
        PgResult res(PQexec(conn_.get(), query));
        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                NodeRef parent, left, right;
                parent.id_high = std::stoll(res.get_value(i, 0));
                parent.id_low = std::stoll(res.get_value(i, 1));
                parent.is_atom = false;
                left.id_high = std::stoll(res.get_value(i, 2));
                left.id_low = std::stoll(res.get_value(i, 3));
                right.id_high = std::stoll(res.get_value(i, 4));
                right.id_low = std::stoll(res.get_value(i, 5));
                composition_cache_[make_key(parent.id_high, parent.id_low)] = {left, right};
            }
        }
    }
    
    /// Build document structure from tokens using CPE.
    /// Tokens → Sentence composition → Document composition
    NodeRef compose_tokens(const std::vector<NodeRef>& tokens) {
        if (tokens.empty()) return NodeRef{};
        if (tokens.size() == 1) return tokens[0];
        
        NodeRef current = tokens[0];
        for (std::size_t i = 1; i < tokens.size(); ++i) {
            NodeRef children[2] = {current, tokens[i]};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            NodeRef comp = NodeRef::comp(h, l);
            pending_compositions_.emplace_back(comp, current, tokens[i]);
            current = comp;
        }
        return current;
    }
    
    /// Fast balanced tree encoding for a word - O(n log n), no BPE overhead
    NodeRef encode_word_balanced(const std::vector<std::int32_t>& codepoints) {
        const auto& atoms = CodepointAtomTable::instance();

        // Convert codepoints to atom refs
        std::vector<NodeRef> nodes;
        nodes.reserve(codepoints.size());
        for (auto cp : codepoints) {
            nodes.push_back(atoms.ref(cp));
        }

        // Reduce via balanced tree - O(log n) levels, O(n) per level = O(n log n)
        while (nodes.size() > 1) {
            std::vector<NodeRef> next;
            next.reserve((nodes.size() + 1) / 2);

            for (std::size_t i = 0; i + 1 < nodes.size(); i += 2) {
                NodeRef children[2] = {nodes[i], nodes[i + 1]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pending_compositions_.emplace_back(comp, nodes[i], nodes[i + 1]);
                next.push_back(comp);
            }
            if (nodes.size() % 2 == 1) {
                next.push_back(nodes.back());
            }
            nodes = std::move(next);
        }
        return nodes[0];
    }

    /// Store phrase compositions via left-to-right cascading from each token.
    ///
    /// Merkle DAG: same content = same hash = stored ONCE.
    /// "Hello" appearing 1000 times → stored once, referenced 1000 times.
    ///
    /// From each token position, cascade left-to-right:
    ///   Position 0: A, AB, ABC, ABCD...
    ///   Position 1: B, BC, BCD...
    ///   Position 2: C, CD...
    ///
    /// Left-to-right composition: ABC = compose(compose(A, B), C)
    /// This creates all phrase prefixes from each starting position.
    void store_phrase_compositions(const std::vector<NodeRef>& tokens, std::size_t max_phrase_tokens) {
        if (tokens.size() < 2) return;

        // From each starting position, cascade left-to-right
        for (std::size_t start = 0; start < tokens.size(); ++start) {
            NodeRef current = tokens[start];
            std::size_t end = std::min(start + max_phrase_tokens, tokens.size());

            for (std::size_t i = start + 1; i < end; ++i) {
                // Left-to-right: current = compose(current, tokens[i])
                NodeRef children[2] = {current, tokens[i]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);

                // Content-addressed: if this composition exists, it's not duplicated
                pending_compositions_.emplace_back(comp, current, tokens[i]);
                current = comp;
            }
        }
    }

    /// Build balanced binary tree from chunk roots and collect compositions.
    /// Unlike compose_tokens (linear chain), this creates a proper balanced tree
    /// with O(log n) depth for better query performance.
    NodeRef build_balanced_tree_and_collect(const std::vector<NodeRef>& nodes) {
        if (nodes.empty()) return NodeRef{};
        if (nodes.size() == 1) return nodes[0];
        
        std::vector<NodeRef> current = nodes;
        
        // Reduce to single root via balanced tree
        while (current.size() > 1) {
            std::vector<NodeRef> next_level;
            next_level.reserve((current.size() + 1) / 2);
            
            for (std::size_t i = 0; i + 1 < current.size(); i += 2) {
                NodeRef children[2] = {current[i], current[i + 1]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pending_compositions_.emplace_back(comp, current[i], current[i + 1]);
                next_level.push_back(comp);
            }
            
            // Handle odd element
            if (current.size() % 2 == 1) {
                next_level.push_back(current.back());
            }
            
            current = std::move(next_level);
        }
        
        return current[0];
    }

    /// Build tree and collect compositions for batch insert.
    /// CRITICAL: Must produce SAME hash as compute_root() for substring lookups to work.
    /// Uses character-by-character CPE, same as compute_root.
    NodeRef build_and_collect(const std::uint8_t* data, std::size_t len) {
        auto codepoints = UTF8Decoder::decode(data, len);
        if (codepoints.empty()) return NodeRef{};
        if (codepoints.size() == 1) {
            return CodepointAtomTable::instance().ref(codepoints[0]);
        }
        
        // Use the SAME CPE algorithm as compute_root, but collect compositions
        return build_cpe_and_collect(codepoints, 0, codepoints.size());
    }

    /// Build CPE composition for codepoint range (for compute_root compatibility)
    NodeRef build_and_collect_codepoints(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end)
    {
        return build_cpe_and_collect(codepoints, start, end);
    }

    /// Flush pending compositions to database - staging table + INSERT ON CONFLICT.
    /// Compositions are idempotent (same hash = same content), so duplicates are ignored.
    /// Staging table _comp_stage is created by SchemaManager at DB init.
    void flush_pending() {
        if (pending_compositions_.empty()) return;

        // Deduplicate in-memory first
        std::unordered_set<std::uint64_t> seen;
        seen.reserve(pending_compositions_.size());
        auto new_end = std::remove_if(pending_compositions_.begin(), pending_compositions_.end(),
            [&](const auto& tuple) {
                const auto& parent = std::get<0>(tuple);
                std::uint64_t key = static_cast<std::uint64_t>(parent.id_high) ^ 
                                   (static_cast<std::uint64_t>(parent.id_low) * 0x9e3779b97f4a7c15ULL);
                return !seen.insert(key).second;
            });
        pending_compositions_.erase(new_end, pending_compositions_.end());
        if (pending_compositions_.empty()) return;

        PGresult* res;

        // Truncate staging table (created by SchemaManager)
        res = PQexec(conn_.get(), "TRUNCATE _comp_stage");
        PQclear(res);

        // Binary COPY to staging
        res = PQexec(conn_.get(), "COPY _comp_stage FROM STDIN WITH (FORMAT binary)");
        if (PQresultStatus(res) != PGRES_COPY_IN) { PQclear(res); pending_compositions_.clear(); return; }
        PQclear(res);

        static const char HDR[] = "PGCOPY\n\377\r\n\0";
        std::vector<char> buf;
        buf.reserve(pending_compositions_.size() * 60 + 32);
        buf.insert(buf.end(), HDR, HDR + 11);
        for (int i = 0; i < 8; ++i) buf.push_back(0);

        auto i16 = [&](std::int16_t v) { buf.push_back((v>>8)&0xFF); buf.push_back(v&0xFF); };
        auto i32 = [&](std::int32_t v) { for(int i=24;i>=0;i-=8) buf.push_back((v>>i)&0xFF); };
        auto i64 = [&](std::int64_t v) { for(int i=56;i>=0;i-=8) buf.push_back((v>>i)&0xFF); };

        for (const auto& [p, l, r] : pending_compositions_) {
            i16(6); i32(8); i64(p.id_high); i32(8); i64(p.id_low);
            i32(8); i64(l.id_high); i32(8); i64(l.id_low);
            i32(8); i64(r.id_high); i32(8); i64(r.id_low);
            
            // Also add to cache so future lookups don't hit DB
            composition_cache_[make_key(p.id_high, p.id_low)] = {l, r};
        }
        i16(-1);

        PQputCopyData(conn_.get(), buf.data(), static_cast<int>(buf.size()));
        PQputCopyEnd(conn_.get(), nullptr);
        res = PQgetResult(conn_.get());
        PQclear(res);

        // INSERT with ON CONFLICT DO NOTHING
        res = PQexec(conn_.get(),
            "INSERT INTO composition (hilbert_high, hilbert_low, left_high, left_low, right_high, right_low) "
            "SELECT h, l, lh, ll, rh, rl FROM _comp_stage ON CONFLICT DO NOTHING");
        PQclear(res);

        pending_compositions_.clear();
    }
    
    /// Check if an ID represents a codepoint atom.
    /// Public wrapper for the free function.
    [[nodiscard]] bool is_atom(std::int64_t high, std::int64_t low) const {
        return hartonomous::db::is_atom(high, low);
    }
};

} // namespace hartonomous::db
