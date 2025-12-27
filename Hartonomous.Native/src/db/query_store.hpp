#pragma once

/// QUERY STORE - The actual query interface that should have existed from day one.
///
/// This provides:
/// 1. Content-addressable lookup: text → root hash → composition
/// 2. Spatial queries using PostGIS
/// 3. Semantic similarity search
/// 4. Database-backed encoding (DB as source of truth, not dump target)

#include "connection.hpp"
#include "pg_result.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/byte_atom_table.hpp"
#include "../atoms/merkle_hash.hpp"
#include "../atoms/semantic_decompose.hpp"
#include <libpq-fe.h>
#include <string>
#include <vector>
#include <optional>
#include <cstdint>
#include <cstring>
#include <cmath>
#include <unordered_map>
#include <iostream>

namespace hartonomous::db {

/// Query result for spatial searches
struct SpatialMatch {
    std::int64_t hilbert_high;
    std::int64_t hilbert_low;
    std::int32_t codepoint;
    double distance;
};

/// Query result for composition lookup
struct CompositionResult {
    NodeRef root;
    bool exists;
    std::size_t byte_count;  // decoded size
};

/// Trajectory through semantic space - RLE compressed points
struct TrajectoryPoint {
    std::int16_t page;      // X: Unicode page
    std::int16_t type;      // Y: Character type
    std::int32_t base;      // Z: Base character
    std::uint8_t variant;   // M: Variant (case/diacritical)
    std::uint32_t count;    // RLE: repetition count (1 = no repeat)
};

/// A trajectory is a sequence of RLE-compressed semantic coordinates
struct Trajectory {
    std::vector<TrajectoryPoint> points;
    double weight;  // For model weights - salience score

    /// Build WKT for LineStringZM (expanding RLE)
    [[nodiscard]] std::string to_wkt() const {
        if (points.empty()) return "LINESTRINGZM EMPTY";

        std::string wkt = "LINESTRINGZM(";
        bool first = true;
        for (const auto& p : points) {
            for (std::uint32_t i = 0; i < p.count; ++i) {
                if (!first) wkt += ", ";
                first = false;
                char buf[64];
                std::snprintf(buf, sizeof(buf), "%d %d %d %d",
                    p.page, p.type, p.base, p.variant);
                wkt += buf;
            }
        }
        wkt += ")";
        return wkt;
    }

    /// Total length (with RLE expansion)
    [[nodiscard]] std::size_t expanded_length() const {
        std::size_t len = 0;
        for (const auto& p : points) len += p.count;
        return len;
    }
};

/// Relationship types
enum class RelType : std::int16_t {
    SEMANTIC_LINK = 0,         // General semantic relationship
    MODEL_WEIGHT = 1,          // Neural network weight (sparse/salient only)
    KNOWLEDGE_EDGE = 2,        // Knowledge graph edge
    TEMPORAL_NEXT = 3,         // Sequence/temporal relationship
    SPATIAL_NEAR = 4,          // Spatial proximity
    EMBEDDING_TRAJECTORY = 5,  // Token embedding as 384-point LineStringZM trajectory
};

/// Relationship result for queries (sparse - only stored relationships appear)
struct Relationship {
    NodeRef from;
    NodeRef to;
    double weight;
    std::int16_t rel_type;
    NodeRef context;
};

/// The ACTUAL query interface for the universal substrate.
/// Uses database as source of truth, not as a dump target.
class QueryStore {
    std::string connstr_;
    PgConnection conn_;

    // Local cache for encoding - mirrors DB, enables batch operations
    std::unordered_map<std::uint64_t, std::pair<NodeRef, NodeRef>> composition_cache_;

    static std::uint64_t make_key(std::int64_t high, std::int64_t low) noexcept {
        return static_cast<std::uint64_t>(high) ^
               (static_cast<std::uint64_t>(low) * 0x9e3779b97f4a7c15ULL);
    }

public:
    explicit QueryStore()
        : connstr_(ConnectionConfig::connection_string())
        , conn_(connstr_) {
        composition_cache_.reserve(100000);
    }

    explicit QueryStore(const std::string& connstr)
        : connstr_(connstr)
        , conn_(connstr) {
        composition_cache_.reserve(100000);
    }

    // =========================================================================
    // CONTENT-ADDRESSABLE LOOKUP
    // =========================================================================

    /// Compute root hash for content WITHOUT storing.
    /// This is the O(1) content addressing the system should provide.
    /// Returns the root NodeRef that would represent this content.
    [[nodiscard]] NodeRef compute_root(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};
        if (len == 1) return ByteAtomTable::instance()[data[0]];

        // Build balanced binary tree structure, compute hashes
        return build_tree_hashes(data, len);
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
            // Atoms always exist (they're the 256 byte values)
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
    [[nodiscard]] CompositionResult find_content(const std::string& text) {
        NodeRef root = compute_root(text);
        bool found = exists(root);
        return {root, found, text.size()};
    }

    // =========================================================================
    // SPATIAL QUERIES - Using PostGIS with 4D semantic distance
    // =========================================================================

    /// Find atoms within distance of a codepoint's semantic position.
    /// Uses semantic_distance for proper 4D distance calculation.
    [[nodiscard]] std::vector<SpatialMatch> find_near_codepoint(
        std::int32_t codepoint,
        double distance_threshold,
        std::size_t limit = 100)
    {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT a2.hilbert_high, a2.hilbert_low, a2.codepoint, "
            "       semantic_distance(a1.semantic_position, a2.semantic_position) as dist "
            "FROM atom a1, atom a2 "
            "WHERE a1.codepoint = %d "
            "  AND a2.codepoint IS NOT NULL "
            "  AND a2.codepoint != %d "
            "  AND semantic_distance(a1.semantic_position, a2.semantic_position) <= %f "
            "ORDER BY dist "
            "LIMIT %zu",
            codepoint, codepoint, distance_threshold, limit);

        return execute_spatial_query(query);
    }

    /// Find atoms semantically similar to a character.
    /// Uses semantic_distance for 4D proximity (includes M coordinate for case/variant).
    [[nodiscard]] std::vector<SpatialMatch> find_similar(
        std::int32_t codepoint,
        std::size_t limit = 20)
    {
        // Use semantic_distance for full 4D proximity
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT a2.hilbert_high, a2.hilbert_low, a2.codepoint, "
            "       semantic_distance(a1.semantic_position, a2.semantic_position) as dist "
            "FROM atom a1, atom a2 "
            "WHERE a1.codepoint = %d "
            "  AND a2.codepoint IS NOT NULL "
            "  AND a2.codepoint != %d "
            "ORDER BY semantic_distance(a1.semantic_position, a2.semantic_position) "
            "LIMIT %zu",
            codepoint, codepoint, limit);

        return execute_spatial_query(query);
    }

    /// Find all case variants of a character (same base, different variant).
    /// 'c' finds 'C', 'ç', 'Ç', etc. - automatic, no manual linking needed.
    [[nodiscard]] std::vector<SpatialMatch> find_case_variants(std::int32_t codepoint) {
        auto coord = SemanticDecompose::get_coord(codepoint);

        // Same page, type, base - different variant (M coordinate)
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT hilbert_high, hilbert_low, codepoint, "
            "       ST_M(semantic_position) as variant "
            "FROM atom "
            "WHERE ST_X(semantic_position) = %d "
            "  AND ST_Y(semantic_position) = %d "
            "  AND ST_Z(semantic_position) = %d "
            "  AND codepoint IS NOT NULL "
            "ORDER BY ST_M(semantic_position)",
            coord.page, coord.type, coord.base);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<SpatialMatch> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                SpatialMatch match;
                match.hilbert_high = std::stoll(res.get_value(i, 0));
                match.hilbert_low = std::stoll(res.get_value(i, 1));
                match.codepoint = std::stoi(res.get_value(i, 2));
                match.distance = std::stod(res.get_value(i, 3));
                results.push_back(match);
            }
        }

        return results;
    }

    /// Case-insensitive composition search using spatial proximity.
    /// "cat" finds compositions containing "Cat", "CAT", "CaT", etc.
    /// Uses the fact that case variants share the same base coordinate.
    [[nodiscard]] std::vector<NodeRef> find_case_insensitive(const std::string& text) {
        // Build set of all case-equivalent compositions
        std::vector<std::vector<std::int32_t>> char_variants;
        char_variants.reserve(text.size());

        for (unsigned char c : text) {
            auto variants = find_case_variants(static_cast<std::int32_t>(c));
            std::vector<std::int32_t> cps;
            cps.reserve(variants.size());
            for (const auto& v : variants) {
                cps.push_back(v.codepoint);
            }
            if (cps.empty()) {
                cps.push_back(static_cast<std::int32_t>(c));
            }
            char_variants.push_back(std::move(cps));
        }

        // For short strings, enumerate all combinations
        // For long strings, use probabilistic approach
        if (text.size() <= 8) {
            return enumerate_case_variants(char_variants);
        } else {
            // Just return the original + all-upper + all-lower
            std::vector<NodeRef> results;
            results.push_back(compute_root(text));

            std::string upper, lower;
            for (unsigned char c : text) {
                upper += static_cast<char>(std::toupper(c));
                lower += static_cast<char>(std::tolower(c));
            }
            results.push_back(compute_root(upper));
            results.push_back(compute_root(lower));

            return results;
        }
    }

private:
    /// Enumerate all case variant combinations
    std::vector<NodeRef> enumerate_case_variants(
        const std::vector<std::vector<std::int32_t>>& variants)
    {
        std::vector<NodeRef> results;
        if (variants.empty()) return results;

        // Recursive enumeration
        std::vector<std::int32_t> current;
        current.reserve(variants.size());
        enumerate_helper(variants, 0, current, results);

        return results;
    }

    void enumerate_helper(
        const std::vector<std::vector<std::int32_t>>& variants,
        std::size_t pos,
        std::vector<std::int32_t>& current,
        std::vector<NodeRef>& results)
    {
        if (pos == variants.size()) {
            // Build string from codepoints
            std::string s;
            for (std::int32_t cp : current) {
                if (cp < 128) {
                    s += static_cast<char>(cp);
                }
            }
            if (!s.empty()) {
                results.push_back(compute_root(s));
            }
            return;
        }

        for (std::int32_t cp : variants[pos]) {
            current.push_back(cp);
            enumerate_helper(variants, pos + 1, current, results);
            current.pop_back();
        }
    }

public:

    /// Find all diacritical variants of a base character.
    [[nodiscard]] std::vector<SpatialMatch> find_diacritical_variants(std::int32_t codepoint) {
        auto coord = SemanticDecompose::get_coord(codepoint);

        // Same page, type, base - any variant
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT hilbert_high, hilbert_low, codepoint, "
            "       ST_M(semantic_position) as variant "
            "FROM atom "
            "WHERE ST_Z(semantic_position) = %d "  // Same base character
            "  AND codepoint IS NOT NULL "
            "ORDER BY ST_M(semantic_position)",
            coord.base);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<SpatialMatch> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                SpatialMatch match;
                match.hilbert_high = std::stoll(res.get_value(i, 0));
                match.hilbert_low = std::stoll(res.get_value(i, 1));
                match.codepoint = std::stoi(res.get_value(i, 2));
                match.distance = std::stod(res.get_value(i, 3));
                results.push_back(match);
            }
        }

        return results;
    }

    // =========================================================================
    // DECODE - Get content back from root
    // =========================================================================

    /// Decode composition tree to bytes.
    [[nodiscard]] std::vector<std::uint8_t> decode(NodeRef root) {
        std::vector<std::uint8_t> result;
        result.reserve(1024);

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
                result.push_back(ByteAtomTable::instance().to_byte(node));
                continue;
            }

            auto children = lookup(node);
            if (!children) {
                throw std::runtime_error("Composition not found in database");
            }

            stack.push_back(children->second);
            stack.push_back(children->first);
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
    NodeRef encode_and_store(const std::uint8_t* data, std::size_t len) {
        if (len == 0) return NodeRef{};

        pending_compositions_.clear();
        NodeRef root = build_and_collect(data, len);

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
    // TRAJECTORIES - RLE-compressed paths through semantic space
    // =========================================================================

    /// Build RLE-compressed trajectory from text.
    /// "Hello" → H(1), e(1), l(2), o(1) - NOT 5 separate records
    [[nodiscard]] Trajectory build_trajectory(const std::string& text) {
        Trajectory traj;
        if (text.empty()) return traj;

        TrajectoryPoint current{};
        bool has_current = false;

        for (unsigned char c : text) {
            auto coord = SemanticDecompose::get_coord(static_cast<std::int32_t>(c));

            if (has_current &&
                current.page == coord.page &&
                current.type == coord.type &&
                current.base == coord.base &&
                current.variant == coord.variant) {
                // Same point - increment RLE count
                current.count++;
            } else {
                // New point - save current if exists
                if (has_current) {
                    traj.points.push_back(current);
                }
                current.page = coord.page;
                current.type = coord.type;
                current.base = coord.base;
                current.variant = coord.variant;
                current.count = 1;
                has_current = true;
            }
        }

        if (has_current) {
            traj.points.push_back(current);
        }

        return traj;
    }

    /// Store trajectory with weight (sparse - only call for salient relationships).
    /// This stores the ENTIRE path as ONE LineStringZM, not N separate records.
    void store_trajectory(NodeRef from, NodeRef to, const Trajectory& traj,
                          RelType type = RelType::SEMANTIC_LINK,
                          NodeRef context = NodeRef{}) {
        // Don't store empty trajectories or zero weights
        if (traj.points.empty()) return;

        std::string wkt = traj.to_wkt();

        char query[2048];
        std::snprintf(query, sizeof(query),
            "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
            "weight, trajectory, rel_type, context_high, context_low) "
            "VALUES (%lld, %lld, %lld, %lld, %f, ST_GeomFromText('%s'), %d, %lld, %lld) "
            "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
            "DO UPDATE SET weight = EXCLUDED.weight, trajectory = EXCLUDED.trajectory",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            traj.weight,
            wkt.c_str(),
            static_cast<int>(type),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low));

        PQexec(conn_.get(), query);
    }

    /// Retrieve trajectory from database and decode back to RLE form.
    [[nodiscard]] std::optional<Trajectory> get_trajectory(NodeRef from, NodeRef to,
                                                            NodeRef context = NodeRef{}) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT weight, ST_AsText(trajectory) FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld "
            "  AND to_high = %lld AND to_low = %lld "
            "  AND context_high = %lld AND context_low = %lld",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low));

        PgResult res(PQexec(conn_.get(), query));
        if (res.status() != PGRES_TUPLES_OK || res.row_count() == 0) {
            return std::nullopt;
        }

        Trajectory traj;
        traj.weight = std::stod(res.get_value(0, 0));

        const char* wkt = res.get_value(0, 1);
        if (wkt && wkt[0] != '\0') {
            traj = parse_trajectory_wkt(wkt, traj.weight);
        }

        return traj;
    }

    /// Export trajectory to text (inverse of build_trajectory).
    /// Expands RLE and converts semantic coords back to codepoints.
    [[nodiscard]] std::string trajectory_to_text(const Trajectory& traj) {
        std::string result;
        result.reserve(traj.expanded_length());

        for (const auto& pt : traj.points) {
            SemanticCoord coord{
                static_cast<std::uint8_t>(pt.page),
                static_cast<std::uint8_t>(pt.type),
                pt.base,
                pt.variant
            };
            std::int32_t cp = SemanticDecompose::to_codepoint(coord);

            // Expand RLE
            for (std::uint32_t i = 0; i < pt.count; ++i) {
                if (cp >= 0 && cp <= 127) {
                    result.push_back(static_cast<char>(cp));
                } else {
                    // UTF-8 encode for non-ASCII
                    encode_utf8(cp, result);
                }
            }
        }

        return result;
    }

    /// Export trajectory to RLE string representation: "H(1)e(1)l(2)o(1)"
    [[nodiscard]] std::string trajectory_to_rle_string(const Trajectory& traj) {
        std::string result;

        for (const auto& pt : traj.points) {
            SemanticCoord coord{
                static_cast<std::uint8_t>(pt.page),
                static_cast<std::uint8_t>(pt.type),
                pt.base,
                pt.variant
            };
            std::int32_t cp = SemanticDecompose::to_codepoint(coord);

            if (cp >= 32 && cp <= 126) {
                result.push_back(static_cast<char>(cp));
            } else {
                char buf[16];
                std::snprintf(buf, sizeof(buf), "\\u%04X", cp);
                result += buf;
            }

            if (pt.count > 1) {
                char buf[16];
                std::snprintf(buf, sizeof(buf), "(x%u)", pt.count);
                result += buf;
            }
        }

        return result;
    }

private:
    /// Parse WKT LineStringZM back to Trajectory with RLE compression.
    [[nodiscard]] Trajectory parse_trajectory_wkt(const char* wkt, double weight) {
        Trajectory traj;
        traj.weight = weight;

        // Skip "LINESTRINGZM(" prefix
        const char* p = wkt;
        while (*p && *p != '(') ++p;
        if (*p == '(') ++p;

        TrajectoryPoint current{};
        bool has_current = false;

        while (*p && *p != ')') {
            // Skip whitespace and commas
            while (*p == ' ' || *p == ',') ++p;
            if (*p == ')' || *p == '\0') break;

            // Parse four numbers: X Y Z M
            int page = 0, type = 0, base = 0, variant = 0;
            if (std::sscanf(p, "%d %d %d %d", &page, &type, &base, &variant) == 4) {
                // RLE compress: if same as current, increment count
                if (has_current &&
                    current.page == page &&
                    current.type == type &&
                    current.base == base &&
                    current.variant == static_cast<std::uint8_t>(variant)) {
                    current.count++;
                } else {
                    if (has_current) {
                        traj.points.push_back(current);
                    }
                    current.page = static_cast<std::int16_t>(page);
                    current.type = static_cast<std::int16_t>(type);
                    current.base = base;
                    current.variant = static_cast<std::uint8_t>(variant);
                    current.count = 1;
                    has_current = true;
                }
            }

            // Skip to next comma or end
            while (*p && *p != ',' && *p != ')') ++p;
        }

        if (has_current) {
            traj.points.push_back(current);
        }

        return traj;
    }

    /// UTF-8 encode a codepoint and append to string.
    static void encode_utf8(std::int32_t cp, std::string& out) {
        if (cp < 0x80) {
            out.push_back(static_cast<char>(cp));
        } else if (cp < 0x800) {
            out.push_back(static_cast<char>(0xC0 | (cp >> 6)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else if (cp < 0x10000) {
            out.push_back(static_cast<char>(0xE0 | (cp >> 12)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        } else {
            out.push_back(static_cast<char>(0xF0 | (cp >> 18)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 12) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | ((cp >> 6) & 0x3F)));
            out.push_back(static_cast<char>(0x80 | (cp & 0x3F)));
        }
    }

public:

    // =========================================================================
    // RELATIONSHIPS - Sparse/Salient storage only
    // =========================================================================

    /// Store a weighted relationship: from → to with weight.
    /// SPARSE: Only call this for salient (non-zero, meaningful) weights.
    void store_relationship(NodeRef from, NodeRef to, double weight,
                            RelType type = RelType::SEMANTIC_LINK,
                            NodeRef context = NodeRef{}) {
        // Sparse encoding: skip near-zero weights
        if (std::abs(weight) < 1e-9) return;

        char query[512];
        std::snprintf(query, sizeof(query),
            "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
            "weight, rel_type, context_high, context_low) "
            "VALUES (%lld, %lld, %lld, %lld, %f, %d, %lld, %lld) "
            "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
            "DO UPDATE SET weight = EXCLUDED.weight",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            weight,
            static_cast<int>(type),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low));

        PQexec(conn_.get(), query);
    }

    /// Find all relationships FROM a node (outgoing edges).
    [[nodiscard]] std::vector<Relationship> find_from(NodeRef from,
                                                       std::size_t limit = 100) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT to_high, to_low, weight, rel_type, context_high, context_low "
            "FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld "
            "ORDER BY weight DESC LIMIT %zu",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            limit);

        return execute_relationship_query(query, from, true);
    }

    /// Find all relationships TO a node (incoming edges).
    [[nodiscard]] std::vector<Relationship> find_to(NodeRef to,
                                                     std::size_t limit = 100) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT from_high, from_low, weight, rel_type, context_high, context_low "
            "FROM relationship "
            "WHERE to_high = %lld AND to_low = %lld "
            "ORDER BY weight DESC LIMIT %zu",
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            limit);

        return execute_relationship_query(query, to, false);
    }

    /// Find relationships by weight range (for model analysis).
    [[nodiscard]] std::vector<Relationship> find_by_weight(
        double min_weight, double max_weight,
        NodeRef context = NodeRef{},
        std::size_t limit = 1000)
    {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT from_high, from_low, to_high, to_low, weight, rel_type "
            "FROM relationship "
            "WHERE weight >= %f AND weight <= %f "
            "  AND context_high = %lld AND context_low = %lld "
            "ORDER BY weight DESC LIMIT %zu",
            min_weight, max_weight,
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low),
            limit);

        PgResult res(PQexec(conn_.get(), query));
        std::vector<Relationship> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                Relationship r;
                r.from.id_high = std::stoll(res.get_value(i, 0));
                r.from.id_low = std::stoll(res.get_value(i, 1));
                r.from.is_atom = is_atom(r.from.id_high, r.from.id_low);
                r.to.id_high = std::stoll(res.get_value(i, 2));
                r.to.id_low = std::stoll(res.get_value(i, 3));
                r.to.is_atom = is_atom(r.to.id_high, r.to.id_low);
                r.weight = std::stod(res.get_value(i, 4));
                r.rel_type = static_cast<std::int16_t>(std::stoi(res.get_value(i, 5)));
                r.context = context;
                results.push_back(r);
            }
        }

        return results;
    }

    /// Get the weight between two specific nodes.
    [[nodiscard]] std::optional<double> get_weight(NodeRef from, NodeRef to,
                                                    NodeRef context = NodeRef{}) {
        char query[256];
        std::snprintf(query, sizeof(query),
            "SELECT weight FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld "
            "  AND to_high = %lld AND to_low = %lld "
            "  AND context_high = %lld AND context_low = %lld",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low));

        PgResult res(PQexec(conn_.get(), query));
        if (res.status() == PGRES_TUPLES_OK && res.row_count() > 0) {
            return std::stod(res.get_value(0, 0));
        }
        return std::nullopt;
    }

    /// Bulk store model weights (for importing neural network parameters).
    /// Uses COPY for maximum throughput - handles millions of rows efficiently.
    void store_model_weights(
        const std::vector<std::tuple<NodeRef, NodeRef, double>>& weights,
        NodeRef model_context,
        RelType type = RelType::MODEL_WEIGHT)
    {
        if (weights.empty()) return;

        // COPY is 10-100x faster than INSERT for bulk loads
        // Use a temp table to handle conflicts gracefully
        PQexec(conn_.get(), 
            "CREATE TEMP TABLE IF NOT EXISTS weight_staging ("
            "from_high BIGINT, from_low BIGINT, to_high BIGINT, to_low BIGINT, "
            "weight DOUBLE PRECISION, rel_type SMALLINT, context_high BIGINT, context_low BIGINT"
            ") ON COMMIT DROP");
        
        PQexec(conn_.get(), "TRUNCATE weight_staging");

        // Start COPY
        PGresult* res = PQexec(conn_.get(),
            "COPY weight_staging (from_high, from_low, to_high, to_low, "
            "weight, rel_type, context_high, context_low) FROM STDIN");
        if (PQresultStatus(res) != PGRES_COPY_IN) {
            PQclear(res);
            return;
        }
        PQclear(res);

        // Stream all rows - no batching needed, COPY handles buffering
        char buf[256];
        for (const auto& [from, to, weight] : weights) {
            int len = std::snprintf(buf, sizeof(buf),
                "%lld\t%lld\t%lld\t%lld\t%.17g\t%d\t%lld\t%lld\n",
                static_cast<long long>(from.id_high),
                static_cast<long long>(from.id_low),
                static_cast<long long>(to.id_high),
                static_cast<long long>(to.id_low),
                weight,
                static_cast<int>(type),
                static_cast<long long>(model_context.id_high),
                static_cast<long long>(model_context.id_low));
            PQputCopyData(conn_.get(), buf, len);
        }

        // End COPY
        PQputCopyEnd(conn_.get(), nullptr);
        res = PQgetResult(conn_.get());
        PQclear(res);

        // Upsert from staging to real table
        PQexec(conn_.get(),
            "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
            "weight, rel_type, context_high, context_low) "
            "SELECT from_high, from_low, to_high, to_low, weight, rel_type, "
            "context_high, context_low FROM weight_staging "
            "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
            "DO UPDATE SET weight = EXCLUDED.weight");
    }

    /// Bulk store embedding trajectories (384-point LineStringZM for each token).
    /// Uses COPY protocol for maximum throughput.
    /// Each embedding becomes a trajectory: point[i] = (i, embed[i], i/64, i%64)
    /// ST_FrechetDistance replaces cosine similarity at query time.
    void store_embedding_trajectories(
        const float* embeddings,           // [vocab_size × hidden_dim] contiguous
        std::size_t vocab_size,
        std::size_t hidden_dim,
        const std::vector<NodeRef>& token_refs,  // NodeRef for each token
        NodeRef model_context,
        RelType type = RelType::EMBEDDING_TRAJECTORY)
    {
        if (vocab_size == 0 || hidden_dim == 0 || token_refs.empty()) return;

        std::cerr << "store_embedding_trajectories: " << vocab_size << " embeddings, " << hidden_dim << " dims\n";

        // Create staging table with trajectory column (no ON COMMIT DROP - we're in autocommit)
        PQexec(conn_.get(), "DROP TABLE IF EXISTS traj_staging");
        PGresult* create_res = PQexec(conn_.get(), 
            "CREATE UNLOGGED TABLE traj_staging ("
            "from_high BIGINT, from_low BIGINT, to_high BIGINT, to_low BIGINT, "
            "weight DOUBLE PRECISION, trajectory TEXT, rel_type SMALLINT, "
            "context_high BIGINT, context_low BIGINT"
            ")");
        if (PQresultStatus(create_res) != PGRES_COMMAND_OK) {
            std::cerr << "store_embedding_trajectories: CREATE failed: " << PQerrorMessage(conn_.get()) << "\n";
            PQclear(create_res);
            return;
        }
        PQclear(create_res);

        // Start COPY
        PGresult* res = PQexec(conn_.get(),
            "COPY traj_staging (from_high, from_low, to_high, to_low, "
            "weight, trajectory, rel_type, context_high, context_low) FROM STDIN");
        if (PQresultStatus(res) != PGRES_COPY_IN) {
            PQclear(res);
            return;
        }
        PQclear(res);

        // Pre-allocate WKT buffer for one trajectory
        // LINESTRINGZM(0 v0 0 0, 1 v1 0 1, ...) ≈ 25 chars per point × 384 = ~10KB
        std::string wkt;
        wkt.reserve(hidden_dim * 30);

        std::string row;
        row.reserve(hidden_dim * 30 + 200);

        std::size_t effective_size = std::min(vocab_size, token_refs.size());

        for (std::size_t token_idx = 0; token_idx < effective_size; ++token_idx) {
            const NodeRef& ref = token_refs[token_idx];
            
            // Skip tokens with null refs
            if (ref.id_high == 0 && ref.id_low == 0) continue;

            const float* embed = embeddings + token_idx * hidden_dim;

            // Compute L2 norm for weight
            double norm_sq = 0.0;
            for (std::size_t d = 0; d < hidden_dim; ++d) {
                norm_sq += static_cast<double>(embed[d]) * static_cast<double>(embed[d]);
            }
            double weight = std::sqrt(norm_sq);

            // Skip zero-norm embeddings
            if (weight < 1e-9) continue;

            // Build WKT: LINESTRINGZM(d embed[d] d/64 d%64, ...)
            wkt.clear();
            wkt += "LINESTRINGZM(";

            for (std::size_t d = 0; d < hidden_dim; ++d) {
                if (d > 0) wkt += ',';
                char pt[64];
                std::snprintf(pt, sizeof(pt), "%zu %.8g %zu %zu",
                    d, static_cast<double>(embed[d]), d / 64, d % 64);
                wkt += pt;
            }
            wkt += ')';

            // Build row: from_high \t from_low \t to_high \t to_low \t weight \t trajectory \t type \t ctx_high \t ctx_low
            row.clear();
            char header[256];
            std::snprintf(header, sizeof(header),
                "%lld\t%lld\t%lld\t%lld\t%.17g\t",
                static_cast<long long>(ref.id_high),
                static_cast<long long>(ref.id_low),
                static_cast<long long>(ref.id_high),  // to = from (self-reference for embedding)
                static_cast<long long>(ref.id_low),
                weight);
            row += header;
            row += wkt;

            char trailer[128];
            std::snprintf(trailer, sizeof(trailer),
                "\t%d\t%lld\t%lld\n",
                static_cast<int>(type),
                static_cast<long long>(model_context.id_high),
                static_cast<long long>(model_context.id_low));
            row += trailer;

            PQputCopyData(conn_.get(), row.data(), static_cast<int>(row.size()));
        }

        // End COPY
        if (PQputCopyEnd(conn_.get(), nullptr) != 1) {
            std::cerr << "store_embedding_trajectories: COPY end failed: " << PQerrorMessage(conn_.get()) << "\n";
        }
        res = PQgetResult(conn_.get());
        if (PQresultStatus(res) != PGRES_COMMAND_OK) {
            std::cerr << "store_embedding_trajectories: COPY failed: " << PQerrorMessage(conn_.get()) << "\n";
        }
        PQclear(res);

        // Check staging count
        PGresult* count_res = PQexec(conn_.get(), "SELECT COUNT(*) FROM traj_staging");
        if (PQresultStatus(count_res) == PGRES_TUPLES_OK) {
            std::cerr << "store_embedding_trajectories: staging has " << PQgetvalue(count_res, 0, 0) << " rows\n";
        }
        PQclear(count_res);

        // Upsert from staging to real table, converting WKT to geometry
        PGresult* upsert_res = PQexec(conn_.get(),
            "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
            "weight, trajectory, rel_type, context_high, context_low) "
            "SELECT from_high, from_low, to_high, to_low, weight, "
            "ST_GeomFromText(trajectory), rel_type, context_high, context_low "
            "FROM traj_staging "
            "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
            "DO UPDATE SET weight = EXCLUDED.weight, trajectory = EXCLUDED.trajectory");
        if (PQresultStatus(upsert_res) != PGRES_COMMAND_OK) {
            std::cerr << "store_embedding_trajectories: upsert failed: " << PQerrorMessage(conn_.get()) << "\n";
        } else {
            std::cerr << "store_embedding_trajectories: upsert complete\n";
        }
        PQclear(upsert_res);

        // Cleanup staging
        PQexec(conn_.get(), "DROP TABLE traj_staging");
    }

    /// Get relationship count.
    [[nodiscard]] std::size_t relationship_count() {
        PgResult res(PQexec(conn_.get(), "SELECT COUNT(*) FROM relationship"));
        if (res.status() != PGRES_TUPLES_OK) return 0;
        return std::stoull(res.get_value(0, 0));
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
            "SELECT to_high, to_low, weight, context_high, context_low "
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
                r.rel_type = static_cast<std::int16_t>(type);
                r.context.id_high = std::stoll(res.get_value(i, 3));
                r.context.id_low = std::stoll(res.get_value(i, 4));
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
        const auto& atoms = ByteAtomTable::instance();

        if (substring.size() == 1) {
            // Single byte - check if atom exists with relationships
            NodeRef atom = atoms[static_cast<unsigned char>(substring[0])];
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

        // For 2-4 bytes, compute the composition and check if it exists
        NodeRef root = compute_root(substring);
        return exists(root);
    }

public:
    std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> pending_compositions_;

    /// Build tree and collect compositions for batch insert.
    NodeRef build_and_collect(const std::uint8_t* data, std::size_t len) {
        const auto& atoms = ByteAtomTable::instance();

        if (len == 1) return atoms[data[0]];
        if (len == 2) {
            NodeRef left = atoms[data[0]];
            NodeRef right = atoms[data[1]];
            NodeRef children[2] = {left, right};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            NodeRef comp = NodeRef::comp(h, l);
            pending_compositions_.emplace_back(comp, left, right);
            return comp;
        }

        std::size_t mid = len / 2;
        NodeRef left = build_and_collect(data, mid);
        NodeRef right = build_and_collect(data + mid, len - mid);

        NodeRef children[2] = {left, right};
        auto [h, l] = MerkleHash::compute(children, children + 2);
        NodeRef comp = NodeRef::comp(h, l);
        pending_compositions_.emplace_back(comp, left, right);
        return comp;
    }

    /// Flush pending compositions to database via bulk COPY.
    void flush_pending() {
        if (pending_compositions_.empty()) return;

        // Use COPY for maximum throughput
        PQexec(conn_.get(), 
            "CREATE TEMP TABLE IF NOT EXISTS comp_staging ("
            "hilbert_high BIGINT, hilbert_low BIGINT, "
            "left_high BIGINT, left_low BIGINT, "
            "right_high BIGINT, right_low BIGINT"
            ") ON COMMIT DROP");
        
        PQexec(conn_.get(), "TRUNCATE comp_staging");

        // Start COPY
        PGresult* res = PQexec(conn_.get(),
            "COPY comp_staging (hilbert_high, hilbert_low, "
            "left_high, left_low, right_high, right_low) FROM STDIN");
        if (PQresultStatus(res) != PGRES_COPY_IN) {
            PQclear(res);
            pending_compositions_.clear();
            return;
        }
        PQclear(res);

        // Stream all rows
        char buf[256];
        for (const auto& [parent, left, right] : pending_compositions_) {
            int len = std::snprintf(buf, sizeof(buf),
                "%lld\t%lld\t%lld\t%lld\t%lld\t%lld\n",
                static_cast<long long>(parent.id_high),
                static_cast<long long>(parent.id_low),
                static_cast<long long>(left.id_high),
                static_cast<long long>(left.id_low),
                static_cast<long long>(right.id_high),
                static_cast<long long>(right.id_low));
            PQputCopyData(conn_.get(), buf, len);
        }

        // End COPY
        PQputCopyEnd(conn_.get(), nullptr);
        res = PQgetResult(conn_.get());
        PQclear(res);

        // Upsert from staging
        PQexec(conn_.get(),
            "INSERT INTO composition (hilbert_high, hilbert_low, "
            "left_high, left_low, right_high, right_low) "
            "SELECT hilbert_high, hilbert_low, left_high, left_low, "
            "right_high, right_low FROM comp_staging "
            "ON CONFLICT (hilbert_high, hilbert_low) DO NOTHING");

        pending_compositions_.clear();
    }

    /// Build tree structure and compute hashes (for compute_root).
    NodeRef build_tree_hashes(const std::uint8_t* data, std::size_t len) {
        const auto& atoms = ByteAtomTable::instance();

        if (len == 1) return atoms[data[0]];
        if (len == 2) {
            NodeRef children[2] = {atoms[data[0]], atoms[data[1]]};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            return NodeRef::comp(h, l);
        }

        // Balanced binary tree
        std::size_t mid = len / 2;
        NodeRef left = build_tree_hashes(data, mid);
        NodeRef right = build_tree_hashes(data + mid, len - mid);

        NodeRef children[2] = {left, right};
        auto [h, l] = MerkleHash::compute(children, children + 2);
        return NodeRef::comp(h, l);
    }

    /// Check if an ID is an atom (byte 0-255).
    bool is_atom(std::int64_t high, std::int64_t low) {
        // Byte atoms have known structure from ByteAtomTable
        for (std::uint32_t b = 0; b < 256; ++b) {
            NodeRef atom = ByteAtomTable::instance()[b];
            if (atom.id_high == high && atom.id_low == low) {
                return true;
            }
        }
        return false;
    }

    std::vector<Relationship> execute_relationship_query(
        const char* query, NodeRef known_node, bool known_is_from)
    {
        PgResult res(PQexec(conn_.get(), query));
        std::vector<Relationship> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                Relationship r;

                if (known_is_from) {
                    r.from = known_node;
                    r.to.id_high = std::stoll(res.get_value(i, 0));
                    r.to.id_low = std::stoll(res.get_value(i, 1));
                    r.to.is_atom = is_atom(r.to.id_high, r.to.id_low);
                } else {
                    r.from.id_high = std::stoll(res.get_value(i, 0));
                    r.from.id_low = std::stoll(res.get_value(i, 1));
                    r.from.is_atom = is_atom(r.from.id_high, r.from.id_low);
                    r.to = known_node;
                }

                r.weight = std::stod(res.get_value(i, 2));
                r.rel_type = static_cast<std::int16_t>(std::stoi(res.get_value(i, 3)));
                r.context.id_high = std::stoll(res.get_value(i, 4));
                r.context.id_low = std::stoll(res.get_value(i, 5));
                r.context.is_atom = false;

                results.push_back(r);
            }
        }

        return results;
    }

    std::vector<SpatialMatch> execute_spatial_query(const char* query) {
        PgResult res(PQexec(conn_.get(), query));
        std::vector<SpatialMatch> results;

        if (res.status() == PGRES_TUPLES_OK) {
            results.reserve(static_cast<std::size_t>(res.row_count()));
            for (int i = 0; i < res.row_count(); ++i) {
                SpatialMatch match;
                match.hilbert_high = std::stoll(res.get_value(i, 0));
                match.hilbert_low = std::stoll(res.get_value(i, 1));
                match.codepoint = std::stoi(res.get_value(i, 2));
                match.distance = std::stod(res.get_value(i, 3));
                results.push_back(match);
            }
        }

        return results;
    }
};

} // namespace hartonomous::db
