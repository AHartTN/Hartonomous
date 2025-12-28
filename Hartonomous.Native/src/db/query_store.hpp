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
#include "../threading/threading.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/codepoint_atom_table.hpp"
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
#include <unordered_set>
#include <iomanip>
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

/// Relationship type is DEPRECATED - relationships are universal.
/// Source/context is encoded in context_high/context_low (NodeRef of source).
/// The rel_type column remains for backward compatibility but should be 0.
using RelType = std::int16_t;
constexpr RelType REL_DEFAULT = 0;

/// Relationship result for queries (sparse - only stored relationships appear)
struct Relationship {
    NodeRef from;
    NodeRef to;
    double weight;
    std::int32_t obs_count;  // How many times this edge was observed
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

        // Decode UTF-8 to codepoints - use UNICODE atoms, not bytes
        auto codepoints = UTF8Decoder::decode(data, len);
        if (codepoints.size() == 1) {
            return CodepointAtomTable::instance().ref(codepoints[0]);
        }

        // Build balanced binary tree on codepoints
        return build_tree_codepoints(codepoints, 0, codepoints.size());
    }

    /// Build tree on codepoints (no collection, just compute hashes)
    [[nodiscard]] NodeRef build_tree_codepoints(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end)
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;

        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);

        if (len == 2) {
            NodeRef children[2] = {atoms.ref(codepoints[start]), atoms.ref(codepoints[start + 1])};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            return NodeRef::comp(h, l);
        }

        std::size_t mid = start + len / 2;
        NodeRef left = build_tree_codepoints(codepoints, start, mid);
        NodeRef right = build_tree_codepoints(codepoints, mid, end);

        NodeRef children[2] = {left, right};
        auto [h, l] = MerkleHash::compute(children, children + 2);
        return NodeRef::comp(h, l);
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
                          RelType type = REL_DEFAULT,
                          NodeRef context = NodeRef{}) {
        // Don't store empty trajectories or zero weights
        if (traj.points.empty()) return;

        std::string wkt = traj.to_wkt();

        char query[2048];
        std::snprintf(query, sizeof(query),
            "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
            "weight, obs_count, trajectory, rel_type, context_high, context_low) "
            "VALUES (%lld, %lld, %lld, %lld, %f, 1, ST_GeomFromText('%s'), %d, %lld, %lld) "
            "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
            "DO UPDATE SET weight = relationship.weight + EXCLUDED.weight, "
            "obs_count = relationship.obs_count + 1, trajectory = EXCLUDED.trajectory",
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
    /// WEIGHTED AVERAGE: On conflict, computes running average of weights.
    void store_relationship(NodeRef from, NodeRef to, double weight,
                            RelType type = REL_DEFAULT,
                            NodeRef context = NodeRef{}) {
        // Sparse encoding: skip near-zero weights
        if (std::abs(weight) < 1e-9) return;

        // On conflict: compute weighted average
        // new_avg = (old_weight * old_count + new_weight) / (old_count + 1)
        char query[512];
        std::snprintf(query, sizeof(query),
            "INSERT INTO relationship (from_high, from_low, to_high, to_low, "
            "weight, obs_count, rel_type, context_high, context_low) "
            "VALUES (%lld, %lld, %lld, %lld, %f, 1, %d, %lld, %lld) "
            "ON CONFLICT (from_high, from_low, to_high, to_low, context_high, context_low) "
            "DO UPDATE SET weight = (relationship.weight * relationship.obs_count + EXCLUDED.weight) / (relationship.obs_count + 1), "
            "obs_count = relationship.obs_count + 1",
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
            "SELECT to_high, to_low, weight, obs_count, rel_type, context_high, context_low "
            "FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld "
            "ORDER BY weight DESC LIMIT %zu",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            limit);

        return execute_relationship_query(query, from, true);
    }

    /// Find all relationships FROM a node within a specific context.
    [[nodiscard]] std::vector<Relationship> find_from(NodeRef from, NodeRef context,
                                                       std::size_t limit = 100) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT to_high, to_low, weight, obs_count, rel_type, context_high, context_low "
            "FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld "
            "AND context_high = %lld AND context_low = %lld "
            "ORDER BY weight DESC LIMIT %zu",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low),
            limit);

        return execute_relationship_query(query, from, true);
    }

    /// Find all relationships TO a node (incoming edges).
    [[nodiscard]] std::vector<Relationship> find_to(NodeRef to,
                                                     std::size_t limit = 100) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT from_high, from_low, weight, obs_count, rel_type, context_high, context_low "
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
            "SELECT from_high, from_low, to_high, to_low, weight, obs_count, rel_type "
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
                r.obs_count = std::stoi(res.get_value(i, 5));
                r.rel_type = static_cast<std::int16_t>(std::stoi(res.get_value(i, 6)));
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

    /// Delete a specific relationship.
    void delete_relationship(NodeRef from, NodeRef to,
                             NodeRef context = NodeRef{}) {
        char query[256];
        std::snprintf(query, sizeof(query),
            "DELETE FROM relationship "
            "WHERE from_high = %lld AND from_low = %lld "
            "  AND to_high = %lld AND to_low = %lld "
            "  AND context_high = %lld AND context_low = %lld",
            static_cast<long long>(from.id_high),
            static_cast<long long>(from.id_low),
            static_cast<long long>(to.id_high),
            static_cast<long long>(to.id_low),
            static_cast<long long>(context.id_high),
            static_cast<long long>(context.id_low));

        PQexec(conn_.get(), query);
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

    /// Build tree and collect compositions for batch insert.
    /// Decodes UTF-8 → codepoints → builds tree on UNICODE atoms (1.1M), not bytes.
    NodeRef build_and_collect(const std::uint8_t* data, std::size_t len) {
        // Decode UTF-8 to codepoints - this is the CORRECT approach
        auto codepoints = UTF8Decoder::decode(data, len);
        return build_and_collect_codepoints(codepoints, 0, codepoints.size());
    }

    /// Build tree on codepoint range (recursive, balanced binary tree)
    NodeRef build_and_collect_codepoints(
        const std::vector<std::int32_t>& codepoints,
        std::size_t start, std::size_t end)
    {
        const auto& atoms = CodepointAtomTable::instance();
        std::size_t len = end - start;

        if (len == 0) return NodeRef{};
        if (len == 1) return atoms.ref(codepoints[start]);

        if (len == 2) {
            NodeRef left = atoms.ref(codepoints[start]);
            NodeRef right = atoms.ref(codepoints[start + 1]);
            NodeRef children[2] = {left, right};
            auto [h, l] = MerkleHash::compute(children, children + 2);
            NodeRef comp = NodeRef::comp(h, l);
            pending_compositions_.emplace_back(comp, left, right);
            return comp;
        }

        std::size_t mid = start + len / 2;
        NodeRef left = build_and_collect_codepoints(codepoints, start, mid);
        NodeRef right = build_and_collect_codepoints(codepoints, mid, end);

        NodeRef children[2] = {left, right};
        auto [h, l] = MerkleHash::compute(children, children + 2);
        NodeRef comp = NodeRef::comp(h, l);
        pending_compositions_.emplace_back(comp, left, right);
        return comp;
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
    /// Uses inverse Hilbert encoding to verify it's a valid atom.
    bool is_atom(std::int64_t high, std::int64_t low) {
        // Try to decode as an atom - if it produces a valid codepoint, it's an atom
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
                r.obs_count = std::stoi(res.get_value(i, 3));
                r.rel_type = static_cast<std::int16_t>(std::stoi(res.get_value(i, 4)));
                r.context.id_high = std::stoll(res.get_value(i, 5));
                r.context.id_low = std::stoll(res.get_value(i, 6));
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
