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
/// - CpeEncoder: Cascading Pair Encoding for content hashing
/// - MlopsQueries: AI/MLOps query operations
/// - BulkOperations: High-throughput data loading

#include "connection.hpp"
#include "pg_result.hpp"
#include "types.hpp"
#include "trajectory_store.hpp"
#include "relationship_store.hpp"
#include "spatial_queries.hpp"
#include "cpe_encoder.hpp"
#include "mlops_queries.hpp"
#include "bulk_operations.hpp"
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

namespace hartonomous::db {

/// Check if an ID represents a codepoint atom.
/// Uses inverse Hilbert encoding to verify it's a valid atom.
inline bool is_atom(std::int64_t high, std::int64_t low) {
    AtomId id{high, low};
    std::int32_t cp = SemanticDecompose::atom_to_codepoint(id);
    if (cp >= 0 && cp <= 0x10FFFF && !(cp >= 0xD800 && cp <= 0xDFFF)) {
        NodeRef verify = CodepointAtomTable::instance().ref(cp);
        return verify.id_high == high && verify.id_low == low;
    }
    return false;
}

/// The ACTUAL query interface for the universal substrate.
/// Uses database as source of truth, not as a dump target.
class QueryStore {
    std::string connstr_;
    PgConnection conn_;
    
    // Component stores - delegate specialized operations
    TrajectoryStore trajectory_store_;
    RelationshipStore relationship_store_;
    std::unique_ptr<SpatialQueries> spatial_queries_;
    CpeEncoder cpe_encoder_;
    std::unique_ptr<MlopsQueries> mlops_queries_;
    std::unique_ptr<BulkOperations> bulk_ops_;

    // Local cache for encoding - mirrors DB, enables batch operations
    std::unordered_map<std::uint64_t, std::pair<NodeRef, NodeRef>> composition_cache_;

    static std::uint64_t make_key(std::int64_t high, std::int64_t low) noexcept {
        return static_cast<std::uint64_t>(high) ^
               (static_cast<std::uint64_t>(low) * 0x9e3779b97f4a7c15ULL);
    }

public:
    std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> pending_compositions_;

    explicit QueryStore()
        : connstr_(ConnectionConfig::connection_string())
        , conn_(connstr_)
        , trajectory_store_(conn_)
        , relationship_store_(conn_)
        , spatial_queries_(std::make_unique<SpatialQueries>(conn_, 
            [this](const std::string& s) { return compute_root(s); }))
        , mlops_queries_(std::make_unique<MlopsQueries>(conn_))
        , bulk_ops_(std::make_unique<BulkOperations>(conn_)) {
        composition_cache_.reserve(100000);
    }

    explicit QueryStore(const std::string& connstr)
        : connstr_(connstr)
        , conn_(connstr)
        , trajectory_store_(conn_)
        , relationship_store_(conn_)
        , spatial_queries_(std::make_unique<SpatialQueries>(conn_,
            [this](const std::string& s) { return compute_root(s); }))
        , mlops_queries_(std::make_unique<MlopsQueries>(conn_))
        , bulk_ops_(std::make_unique<BulkOperations>(conn_)) {
        composition_cache_.reserve(100000);
    }

    // =========================================================================
    // CONTENT-ADDRESSABLE LOOKUP
    // =========================================================================

    /// Compute root hash for content WITHOUT storing.
    [[nodiscard]] NodeRef compute_root(const std::uint8_t* data, std::size_t len) const {
        if (len == 0) return NodeRef{};

        HierarchicalChunker chunker;
        auto chunks = chunker.chunk(data, len);
        if (chunks.empty()) return NodeRef{};

        std::vector<NodeRef> token_roots;
        token_roots.reserve(chunks.size());

        for (const auto& chunk : chunks) {
            auto codepoints = UTF8Decoder::decode(chunk.data, chunk.length);
            if (codepoints.empty()) continue;

            NodeRef token_root;
            if (codepoints.size() == 1) {
                token_root = CodepointAtomTable::instance().ref(codepoints[0]);
            } else {
                token_root = compute_word_balanced_hash(codepoints);
            }
            token_roots.push_back(token_root);
        }

        if (token_roots.empty()) return NodeRef{};
        if (token_roots.size() == 1) return token_roots[0];

        // Left-to-right composition
        NodeRef current = token_roots[0];
        for (std::size_t i = 1; i < token_roots.size(); ++i) {
            NodeRef children[2] = {current, token_roots[i]};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            current = NodeRef::comp(h, l);
        }
        return current;
    }

    [[nodiscard]] NodeRef compute_root(const char* text) {
        return compute_root(reinterpret_cast<const std::uint8_t*>(text), std::strlen(text));
    }

    [[nodiscard]] NodeRef compute_root(const std::string& text) {
        return compute_root(reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
    }

    /// Compute balanced tree hash WITHOUT storing.
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

    /// Compute word balanced hash.
    [[nodiscard]] NodeRef compute_word_balanced_hash(const std::vector<std::int32_t>& codepoints) const {
        const auto& atoms = CodepointAtomTable::instance();
        std::vector<NodeRef> nodes;
        nodes.reserve(codepoints.size());
        for (auto cp : codepoints) {
            nodes.push_back(atoms.ref(cp));
        }
        return compute_balanced_tree_hash(nodes);
    }

    /// Compute CPE hash - delegated to CpeEncoder.
    [[nodiscard]] NodeRef compute_cpe_hash(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end) const {
        return cpe_encoder_.compute_cpe_hash(codepoints, start, end);
    }

    [[nodiscard]] NodeRef compute_root_for_query(const std::string& text) const {
        return compute_root(
            reinterpret_cast<const std::uint8_t*>(text.data()), text.size());
    }

    static std::uint64_t compute_pair_key(NodeRef left, NodeRef right) {
        return CpeEncoder::make_pair_key(left, right);
    }

    /// Check if a composition exists in the database.
    [[nodiscard]] bool exists(NodeRef ref) {
        if (ref.is_atom) return true;

        char query[256];
        std::snprintf(query, sizeof(query),
            "SELECT 1 FROM composition WHERE hilbert_high = %lld AND hilbert_low = %lld LIMIT 1",
            static_cast<long long>(ref.id_high),
            static_cast<long long>(ref.id_low));

        PgResult res(PQexec(conn_.get(), query));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    /// Lookup composition by root hash.
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
        left.is_atom = is_atom(left.id_high, left.id_low);
        right.is_atom = is_atom(right.id_high, right.id_low);

        return std::make_pair(left, right);
    }

    /// Full content lookup: text → root → exists?
    [[nodiscard]] CompositionResult find_content(const std::string& text) {
        NodeRef root = compute_root(text);
        bool found = exists(root);
        return {root, found, text.size()};
    }

    [[nodiscard]] bool content_contains(NodeRef content_root, const std::string& query) {
        std::string decoded = decode_string(content_root);
        return decoded.find(query) != std::string::npos;
    }

    // =========================================================================
    // SPATIAL QUERIES - Delegated to SpatialQueries component
    // =========================================================================

    [[nodiscard]] std::vector<SpatialMatch> find_near_codepoint(
        std::int32_t codepoint, double distance_threshold, std::size_t limit = 100) {
        return spatial_queries_->find_near_codepoint(codepoint, distance_threshold, limit);
    }

    [[nodiscard]] std::vector<SpatialMatch> find_similar(std::int32_t codepoint, std::size_t limit = 20) {
        return spatial_queries_->find_similar(codepoint, limit);
    }

    [[nodiscard]] std::vector<SpatialMatch> find_case_variants(std::int32_t codepoint) {
        return spatial_queries_->find_case_variants(codepoint);
    }

    [[nodiscard]] std::vector<NodeRef> find_case_insensitive(const std::string& text) {
        return spatial_queries_->find_case_insensitive(text);
    }

    [[nodiscard]] std::vector<SpatialMatch> find_diacritical_variants(std::int32_t codepoint) {
        return spatial_queries_->find_diacritical_variants(codepoint);
    }

    // =========================================================================
    // DECODE - Get content back from root
    // =========================================================================

    [[nodiscard]] std::vector<std::uint8_t> decode(NodeRef root) {
        std::vector<std::int32_t> codepoints;
        codepoints.reserve(1024);

        std::vector<NodeRef> stack;
        stack.reserve(10000);
        stack.push_back(root);

        while (!stack.empty()) {
            NodeRef node = stack.back();
            stack.pop_back();

            if (node.id_high == 0 && node.id_low == 0 && !node.is_atom) continue;

            if (node.is_atom) {
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

        std::vector<std::uint8_t> result;
        result.reserve(codepoints.size() * 2);
        std::uint8_t buf[4];
        for (std::int32_t cp : codepoints) {
            std::size_t len = UTF8Decoder::encode_one(cp, buf);
            result.insert(result.end(), buf, buf + len);
        }
        return result;
    }

    [[nodiscard]] std::string decode_string(NodeRef root) {
        auto bytes = decode(root);
        return std::string(bytes.begin(), bytes.end());
    }

    // =========================================================================
    // ENCODE AND STORE
    // =========================================================================

    NodeRef encode_and_store(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};

        // Don't clear pending - allow batching
        HierarchicalChunker chunker;
        auto chunks = chunker.chunk(data, len);
        if (chunks.empty()) return NodeRef{};
        
        std::vector<NodeRef> chunk_roots;
        chunk_roots.reserve(chunks.size());
        
        for (const auto& chunk : chunks) {
            auto codepoints = UTF8Decoder::decode(chunk.data, chunk.length);
            if (codepoints.empty()) continue;

            NodeRef chunk_root;
            if (codepoints.size() == 1) {
                chunk_root = CodepointAtomTable::instance().ref(codepoints[0]);
            } else {
                chunk_root = encode_word_balanced(codepoints);
            }
            chunk_roots.push_back(chunk_root);
        }
        
        store_phrase_compositions(chunk_roots, 20);
        
        // Use same composition logic as compute_root for consistent hashing
        NodeRef root;
        if (chunk_roots.size() == 1) {
            root = chunk_roots[0];
        } else {
            // Left-to-right composition
            NodeRef current = chunk_roots[0];
            for (std::size_t i = 1; i < chunk_roots.size(); ++i) {
                NodeRef children[2] = {current, chunk_roots[i]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pending_compositions_.emplace_back(comp, current, chunk_roots[i]);
                current = comp;
            }
            root = current;
        }
        
        // Auto-flush if pending buffer is getting large
        if (pending_compositions_.size() > 50000) {
            flush_pending();
        }
        
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
    // TRAJECTORIES - Delegated to TrajectoryStore
    // =========================================================================

    [[nodiscard]] Trajectory build_trajectory(const std::string& text) {
        return TrajectoryStore::build_trajectory(text);
    }

    void store_trajectory(NodeRef from, NodeRef to, const Trajectory& traj,
                          RelType type = REL_DEFAULT, NodeRef context = NodeRef{}) {
        trajectory_store_.store(from, to, traj, type, context);
    }

    [[nodiscard]] std::optional<Trajectory> get_trajectory(NodeRef from, NodeRef to,
                                                            NodeRef context = NodeRef{}) {
        return trajectory_store_.get(from, to, context);
    }

    [[nodiscard]] std::string trajectory_to_text(const Trajectory& traj) {
        return TrajectoryStore::to_text(traj);
    }

    [[nodiscard]] std::string trajectory_to_rle_string(const Trajectory& traj) {
        return TrajectoryStore::to_rle_string(traj);
    }

    // =========================================================================
    // RELATIONSHIPS - Delegated to RelationshipStore
    // =========================================================================

    void store_relationship(NodeRef from, NodeRef to, double weight,
                            RelType type = REL_DEFAULT, NodeRef context = NodeRef{}) {
        relationship_store_.store(from, to, weight, type, context);
    }

    [[nodiscard]] std::vector<Relationship> find_from(NodeRef from, std::size_t limit = 100) {
        return relationship_store_.find_from(from, limit);
    }

    [[nodiscard]] std::vector<Relationship> find_from(NodeRef from, NodeRef context, std::size_t limit = 100) {
        return relationship_store_.find_from_with_context(from, context, limit);
    }

    [[nodiscard]] std::vector<Relationship> find_to(NodeRef to, std::size_t limit = 100) {
        return relationship_store_.find_to(to, limit);
    }

    [[nodiscard]] std::vector<Relationship> find_by_weight(
        double min_weight, double max_weight, NodeRef context = NodeRef{}, std::size_t limit = 1000) {
        return relationship_store_.find_by_weight(min_weight, max_weight, context, limit);
    }

    [[nodiscard]] std::optional<double> get_weight(NodeRef from, NodeRef to, NodeRef context = NodeRef{}) {
        return relationship_store_.get_weight(from, to, context);
    }

    void delete_relationship(NodeRef from, NodeRef to, NodeRef context = NodeRef{}) {
        relationship_store_.remove(from, to, context);
    }

    [[nodiscard]] std::vector<Relationship> find_by_type(NodeRef from, RelType type, std::size_t limit = 100) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT to_high, to_low, weight, obs_count, context_high, context_low "
            "FROM relationship WHERE from_high = %lld AND from_low = %lld AND rel_type = %d "
            "ORDER BY weight DESC LIMIT %zu",
            static_cast<long long>(from.id_high), static_cast<long long>(from.id_low),
            static_cast<int>(type), limit);

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
                results.push_back(r);
            }
        }
        return results;
    }

    // =========================================================================
    // BULK OPERATIONS - Delegated to BulkOperations
    // =========================================================================

    void store_model_weights(
        const std::vector<std::tuple<NodeRef, NodeRef, double>>& weights,
        NodeRef model_context, RelType type = REL_DEFAULT) {
        bulk_ops_->store_model_weights(weights, model_context, type);
    }

    void store_embedding_trajectories(
        const float* embeddings, std::size_t vocab_size, std::size_t hidden_dim,
        const std::vector<NodeRef>& token_refs, NodeRef model_context, RelType type = REL_DEFAULT) {
        bulk_ops_->store_embedding_trajectories(embeddings, vocab_size, hidden_dim,
            token_refs, model_context, type);
    }

    /// Bulk store compositions (parent, left, right)
    void bulk_store_compositions(
        const std::vector<std::tuple<NodeRef, NodeRef, NodeRef>>& compositions) {
        if (compositions.empty()) return;
        
        // Build COPY data
        std::string data;
        data.reserve(compositions.size() * 80);
        
        for (const auto& [parent, left, right] : compositions) {
            char buf[256];
            char* p = buf;
            p += std::sprintf(p, "%lld\t%lld\t%lld\t%lld\t%lld\t%lld\n",
                static_cast<long long>(parent.id_high),
                static_cast<long long>(parent.id_low),
                static_cast<long long>(left.id_high),
                static_cast<long long>(left.id_low),
                static_cast<long long>(right.id_high),
                static_cast<long long>(right.id_low));
            data.append(buf, static_cast<std::size_t>(p - buf));
        }
        
        // Stage and insert - use DISTINCT to avoid duplicate key in same batch
        PQexec(conn_.get(), "DROP TABLE IF EXISTS _vocab_comp_stage");
        PQexec(conn_.get(),
            "CREATE UNLOGGED TABLE _vocab_comp_stage ("
            "h BIGINT, l BIGINT, lh BIGINT, ll BIGINT, rh BIGINT, rl BIGINT)");
        
        PGresult* res = PQexec(conn_.get(), "COPY _vocab_comp_stage FROM STDIN");
        if (PQresultStatus(res) == PGRES_COPY_IN) {
            PQclear(res);
            PQputCopyData(conn_.get(), data.c_str(), static_cast<int>(data.size()));
            PQputCopyEnd(conn_.get(), nullptr);
            PGresult* copy_res = PQgetResult(conn_.get());
            PQclear(copy_res);
            
            // Use DISTINCT ON to dedup within batch
            PGresult* ins_res = PQexec(conn_.get(),
                "INSERT INTO composition (hilbert_high, hilbert_low, left_high, left_low, right_high, right_low, obs_count) "
                "SELECT DISTINCT ON (h, l) h, l, lh, ll, rh, rl, 1 FROM _vocab_comp_stage "
                "ON CONFLICT (hilbert_high, hilbert_low) DO UPDATE SET obs_count = composition.obs_count + 1");
            PQclear(ins_res);
        } else {
            PQclear(res);
        }
        
        PQexec(conn_.get(), "DROP TABLE IF EXISTS _vocab_comp_stage");
    }

    // =========================================================================
    // MLOPS QUERIES - Delegated to MlopsQueries
    // =========================================================================

    [[nodiscard]] std::vector<std::pair<NodeRef, double>> find_similar_tokens(
        NodeRef token_ref, NodeRef model_context, std::size_t limit = 10) {
        return mlops_queries_->find_similar_tokens(token_ref, model_context, limit);
    }

    [[nodiscard]] std::vector<std::tuple<NodeRef, NodeRef, double>> compute_attention(
        const std::vector<NodeRef>& tokens, NodeRef model_context, double threshold = 1.0) {
        return mlops_queries_->compute_attention(tokens, model_context, threshold);
    }

    [[nodiscard]] std::vector<std::pair<NodeRef, double>> forward_pass(
        const std::vector<NodeRef>& input_tokens, NodeRef model_context, std::size_t top_k = 10) {
        return mlops_queries_->forward_pass(input_tokens, model_context, top_k);
    }

    [[nodiscard]] std::vector<std::pair<double, std::size_t>> weight_histogram(
        NodeRef model_context, std::size_t num_buckets = 20) {
        return mlops_queries_->weight_histogram(model_context, num_buckets);
    }

    [[nodiscard]] std::vector<Relationship> top_weights(NodeRef model_context, std::size_t limit = 100) {
        return mlops_queries_->top_weights(model_context, limit);
    }

    std::size_t prune_weights(NodeRef model_context, double threshold = 1e-6) {
        return mlops_queries_->prune_weights(model_context, threshold);
    }

    [[nodiscard]] std::vector<std::pair<NodeRef, double>> query_trajectory_intersections(
        NodeRef ref, double distance_threshold = 0.1) {
        return mlops_queries_->query_trajectory_intersections(ref, distance_threshold);
    }

    [[nodiscard]] std::vector<std::pair<NodeRef, double>> query_trajectory_neighbors(
        NodeRef ref, std::size_t limit = 10) {
        return mlops_queries_->query_trajectory_neighbors(ref, limit);
    }

    [[nodiscard]] std::vector<NodeRef> query_bounding_box(
        double page_min, double page_max, double type_min, double type_max,
        double base_min, double base_max, double variant_min, double variant_max,
        std::size_t limit = 100) {
        return mlops_queries_->query_bounding_box(page_min, page_max, type_min, type_max,
            base_min, base_max, variant_min, variant_max, limit);
    }

    [[nodiscard]] std::vector<NodeRef> query_trajectories_through_point(
        double page, double type, double base, double variant,
        double radius = 1.0, std::size_t limit = 100) {
        return mlops_queries_->query_trajectories_through_point(page, type, base, variant, radius, limit);
    }

    // =========================================================================
    // STATISTICS AND UTILITIES
    // =========================================================================

    [[nodiscard]] std::size_t relationship_count() {
        PgResult res(PQexec(conn_.get(), "SELECT COUNT(*) FROM relationship"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoull(res.get_value(0, 0));
    }

    [[nodiscard]] std::int64_t database_size() {
        PgResult res(PQexec(conn_.get(), "SELECT pg_database_size(current_database())"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoll(res.get_value(0, 0));
    }

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

    void store_composition(NodeRef parent, NodeRef left, NodeRef right) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "INSERT INTO composition (hilbert_high, hilbert_low, "
            "left_high, left_low, right_high, right_low) "
            "VALUES (%lld, %lld, %lld, %lld, %lld, %lld) "
            "ON CONFLICT (hilbert_high, hilbert_low) DO NOTHING",
            static_cast<long long>(parent.id_high), static_cast<long long>(parent.id_low),
            static_cast<long long>(left.id_high), static_cast<long long>(left.id_low),
            static_cast<long long>(right.id_high), static_cast<long long>(right.id_low));
        PQexec(conn_.get(), query);
    }

    [[nodiscard]] std::pair<bool, std::string> explain_query(const char* query) {
        std::string explain = "EXPLAIN ANALYZE ";
        explain += query;
        PgResult res(PQexec(conn_.get(), explain.c_str()));
        if (res.status() != PGRES_TUPLES_OK) return {false, "Query failed"};

        std::string plan;
        bool uses_index = false;
        for (int i = 0; i < res.row_count(); ++i) {
            std::string line = res.get_value(i, 0);
            plan += line + "\n";
            if (line.find("Index Scan") != std::string::npos ||
                line.find("Index Only Scan") != std::string::npos ||
                line.find("Bitmap Index Scan") != std::string::npos) {
                uses_index = true;
            }
        }
        return {uses_index, plan};
    }

    [[nodiscard]] bool verify_composition_index_usage() {
        PgResult res(PQexec(conn_.get(),
            "SELECT 1 FROM pg_indexes WHERE tablename = 'composition' AND indexname = 'composition_pkey'"));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    [[nodiscard]] bool verify_spatial_index_usage() {
        PgResult res(PQexec(conn_.get(),
            "SELECT 1 FROM pg_indexes WHERE tablename = 'atom' AND indexname = 'idx_atom_semantic_position'"));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    [[nodiscard]] bool verify_relationship_index_usage() {
        PgResult res(PQexec(conn_.get(),
            "SELECT 1 FROM pg_indexes WHERE tablename = 'relationship' AND indexname = 'idx_relationship_from'"));
        return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
    }

    // =========================================================================
    // SUBSTRING CONTAINMENT QUERIES
    // =========================================================================

    [[nodiscard]] bool contains_substring(const std::string& substring) {
        if (substring.empty()) return true;
        NodeRef substr_root = compute_root(substring);
        if (exists(substr_root)) return true;
        if (substring.size() <= 4) return contains_short_substring(substring);
        return false;
    }

    [[nodiscard]] std::vector<NodeRef> find_containing(const std::string& substring, std::size_t limit = 100) {
        std::vector<NodeRef> results;
        if (substring.empty()) return results;

        NodeRef substr_root = compute_root(substring);
        char query[1024];
        std::snprintf(query, sizeof(query),
            "WITH RECURSIVE ancestors AS ("
            "  SELECT c.hilbert_high, c.hilbert_low FROM composition c "
            "  WHERE (c.left_high = %lld AND c.left_low = %lld) "
            "     OR (c.right_high = %lld AND c.right_low = %lld) "
            "  UNION "
            "  SELECT c.hilbert_high, c.hilbert_low FROM composition c "
            "  JOIN ancestors a ON (c.left_high = a.hilbert_high AND c.left_low = a.hilbert_low) "
            "                   OR (c.right_high = a.hilbert_high AND c.right_low = a.hilbert_low) "
            ") SELECT DISTINCT hilbert_high, hilbert_low FROM ancestors LIMIT %zu",
            static_cast<long long>(substr_root.id_high), static_cast<long long>(substr_root.id_low),
            static_cast<long long>(substr_root.id_high), static_cast<long long>(substr_root.id_low),
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
    bool contains_short_substring(const std::string& substring) {
        const auto& atoms = CodepointAtomTable::instance();
        auto codepoints = UTF8Decoder::decode(substring);
        
        if (codepoints.size() == 1) {
            NodeRef atom = atoms.ref(codepoints[0]);
            char query[256];
            std::snprintf(query, sizeof(query),
                "SELECT 1 FROM composition WHERE "
                "(left_high = %lld AND left_low = %lld) OR "
                "(right_high = %lld AND right_low = %lld) LIMIT 1",
                static_cast<long long>(atom.id_high), static_cast<long long>(atom.id_low),
                static_cast<long long>(atom.id_high), static_cast<long long>(atom.id_low));
            PgResult res(PQexec(conn_.get(), query));
            return res.status() == PGRES_TUPLES_OK && res.row_count() > 0;
        }
        NodeRef root = compute_root(substring);
        return exists(root);
    }

public:
    // =========================================================================
    // CPE AND TREE BUILDING
    // =========================================================================

    NodeRef build_cpe_and_collect(
        const std::vector<std::int32_t>& codepoints, std::size_t start, std::size_t end) {
        return cpe_encoder_.build_cpe_and_collect(codepoints, start, end, pending_compositions_);
    }

    std::vector<NodeRef> tokenize_greedy(const std::vector<std::int32_t>& codepoints) {
        std::vector<NodeRef> tokens;
        if (codepoints.empty()) return tokens;
        
        std::vector<std::pair<std::size_t, std::size_t>> word_ranges;
        std::size_t word_start = 0;
        bool in_word = false;
        
        auto is_word_char = [](std::int32_t cp) {
            return (cp >= 'A' && cp <= 'Z') || (cp >= 'a' && cp <= 'z') || 
                   (cp >= '0' && cp <= '9') || (cp >= 0x80);
        };
        
        for (std::size_t i = 0; i < codepoints.size(); ++i) {
            bool is_word = is_word_char(codepoints[i]);
            if (is_word && !in_word) { word_start = i; in_word = true; }
            else if (!is_word && in_word) { word_ranges.push_back({word_start, i}); in_word = false; }
            if (!is_word) tokens.push_back(CodepointAtomTable::instance().ref(codepoints[i]));
        }
        if (in_word) word_ranges.push_back({word_start, codepoints.size()});
        
        for (const auto& [start, end] : word_ranges) {
            NodeRef word_ref = compute_cpe_hash(codepoints, start, end);
            if (!cache_contains(word_ref)) {
                word_ref = build_cpe_and_collect(codepoints, start, end);
            }
            tokens.push_back(word_ref);
        }
        return tokens;
    }

    [[nodiscard]] bool cache_contains(NodeRef ref) const {
        if (ref.is_atom) return true;
        return composition_cache_.count(make_key(ref.id_high, ref.id_low)) > 0;
    }

    void cache_add(NodeRef parent, NodeRef left, NodeRef right) {
        composition_cache_[make_key(parent.id_high, parent.id_low)] = {left, right};
    }

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
                left.id_high = std::stoll(res.get_value(i, 2));
                left.id_low = std::stoll(res.get_value(i, 3));
                right.id_high = std::stoll(res.get_value(i, 4));
                right.id_low = std::stoll(res.get_value(i, 5));
                composition_cache_[make_key(parent.id_high, parent.id_low)] = {left, right};
            }
        }
    }

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

    NodeRef encode_word_balanced(const std::vector<std::int32_t>& codepoints) {
        const auto& atoms = CodepointAtomTable::instance();
        std::vector<NodeRef> nodes;
        nodes.reserve(codepoints.size());
        for (auto cp : codepoints) nodes.push_back(atoms.ref(cp));

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
            if (nodes.size() % 2 == 1) next.push_back(nodes.back());
            nodes = std::move(next);
        }
        return nodes[0];
    }

    void store_phrase_compositions(const std::vector<NodeRef>& tokens, std::size_t max_phrase_tokens) {
        if (tokens.size() < 2) return;
        for (std::size_t start = 0; start < tokens.size(); ++start) {
            NodeRef current = tokens[start];
            std::size_t end = std::min(start + max_phrase_tokens, tokens.size());
            for (std::size_t i = start + 1; i < end; ++i) {
                NodeRef children[2] = {current, tokens[i]};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef comp = NodeRef::comp(h, l);
                pending_compositions_.emplace_back(comp, current, tokens[i]);
                current = comp;
            }
        }
    }

    NodeRef build_balanced_tree_and_collect(const std::vector<NodeRef>& nodes) {
        if (nodes.empty()) return NodeRef{};
        if (nodes.size() == 1) return nodes[0];
        
        std::vector<NodeRef> current = nodes;
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
            if (current.size() % 2 == 1) next_level.push_back(current.back());
            current = std::move(next_level);
        }
        return current[0];
    }

    NodeRef build_and_collect(const std::uint8_t* data, std::size_t len) {
        auto codepoints = UTF8Decoder::decode(data, len);
        if (codepoints.empty()) return NodeRef{};
        if (codepoints.size() == 1) return CodepointAtomTable::instance().ref(codepoints[0]);
        return build_cpe_and_collect(codepoints, 0, codepoints.size());
    }

    NodeRef build_and_collect_codepoints(
        const std::vector<std::int32_t>& codepoints, std::size_t start, std::size_t end) {
        return build_cpe_and_collect(codepoints, start, end);
    }

    void flush_pending() {
        if (pending_compositions_.empty()) return;

        std::unordered_set<std::uint64_t> seen;
        seen.reserve(pending_compositions_.size());
        auto new_end = std::remove_if(pending_compositions_.begin(), pending_compositions_.end(),
            [&](const auto& tuple) {
                const auto& parent = std::get<0>(tuple);
                std::uint64_t key = make_key(parent.id_high, parent.id_low);
                return !seen.insert(key).second;
            });
        pending_compositions_.erase(new_end, pending_compositions_.end());
        if (pending_compositions_.empty()) return;

        // Create staging table if needed
        PGresult* res = PQexec(conn_.get(), "DROP TABLE IF EXISTS _comp_stage");
        PQclear(res);
        res = PQexec(conn_.get(), 
            "CREATE UNLOGGED TABLE _comp_stage ("
            "h BIGINT, l BIGINT, lh BIGINT, ll BIGINT, rh BIGINT, rl BIGINT)");
        PQclear(res);

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
            composition_cache_[make_key(p.id_high, p.id_low)] = {l, r};
        }
        i16(-1);

        PQputCopyData(conn_.get(), buf.data(), static_cast<int>(buf.size()));
        PQputCopyEnd(conn_.get(), nullptr);
        res = PQgetResult(conn_.get());
        PQclear(res);

        res = PQexec(conn_.get(),
            "INSERT INTO composition (hilbert_high, hilbert_low, left_high, left_low, right_high, right_low) "
            "SELECT h, l, lh, ll, rh, rl FROM _comp_stage ON CONFLICT DO NOTHING");
        PQclear(res);
        
        // Cleanup
        res = PQexec(conn_.get(), "DROP TABLE IF EXISTS _comp_stage");
        PQclear(res);
        
        pending_compositions_.clear();
    }

    [[nodiscard]] bool is_atom(std::int64_t high, std::int64_t low) const {
        return hartonomous::db::is_atom(high, low);
    }
};

} // namespace hartonomous::db
