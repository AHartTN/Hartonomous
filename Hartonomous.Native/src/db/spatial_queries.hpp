#pragma once

/// SPATIAL QUERIES - PostGIS-backed semantic proximity searches.
///
/// Find similar characters, case variants, diacriticals via geometry.
/// Uses 4D semantic coordinates: (page, type, base, variant).
///
/// Extracted from query_store.hpp for separation of concerns.

#include "connection.hpp"
#include "pg_result.hpp"
#include "types.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/semantic_decompose.hpp"
#include <libpq-fe.h>
#include <vector>
#include <string>
#include <cstdio>
#include <cctype>
#include <functional>

namespace hartonomous::db {

/// Spatial query operations using PostGIS.
/// Provides semantic similarity search using 4D geometry.
class SpatialQueries {
    PgConnection& conn_;
    std::function<NodeRef(const std::string&)> compute_root_fn_;

public:
    /// Constructor with connection and root computation callback.
    /// The callback is needed for case-insensitive search to compute hashes.
    SpatialQueries(PgConnection& conn,
                   std::function<NodeRef(const std::string&)> compute_root_fn)
        : conn_(conn), compute_root_fn_(std::move(compute_root_fn)) {}

    /// Find atoms within distance of a codepoint's semantic position.
    /// Uses semantic_distance for proper 4D distance calculation.
    [[nodiscard]] std::vector<SpatialMatch> find_near_codepoint(
        std::int32_t codepoint,
        double distance_threshold,
        std::size_t limit = 100) {
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

        return execute_query(query);
    }

    /// Find atoms semantically similar to a character.
    /// Uses semantic_distance for 4D proximity (includes M coordinate for case/variant).
    [[nodiscard]] std::vector<SpatialMatch> find_similar(
        std::int32_t codepoint,
        std::size_t limit = 20) {
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

        return execute_query(query);
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
            results.push_back(compute_root_fn_(text));

            std::string upper, lower;
            for (unsigned char c : text) {
                upper += static_cast<char>(std::toupper(c));
                lower += static_cast<char>(std::tolower(c));
            }
            results.push_back(compute_root_fn_(upper));
            results.push_back(compute_root_fn_(lower));

            return results;
        }
    }

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

private:
    /// Execute spatial query and parse results.
    [[nodiscard]] std::vector<SpatialMatch> execute_query(const char* query) {
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

    /// Enumerate all case variant combinations.
    [[nodiscard]] std::vector<NodeRef> enumerate_case_variants(
        const std::vector<std::vector<std::int32_t>>& variants) {
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
        std::vector<NodeRef>& results) {
        if (pos == variants.size()) {
            // Build string from codepoints
            std::string s;
            for (std::int32_t cp : current) {
                if (cp < 128) {
                    s += static_cast<char>(cp);
                }
            }
            if (!s.empty()) {
                results.push_back(compute_root_fn_(s));
            }
            return;
        }

        for (std::int32_t cp : variants[pos]) {
            current.push_back(cp);
            enumerate_helper(variants, pos + 1, current, results);
            current.pop_back();
        }
    }
};

} // namespace hartonomous::db
