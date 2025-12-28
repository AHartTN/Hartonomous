#pragma once

/// RELATIONSHIP STORE - Sparse relationship storage and retrieval.
///
/// Relationships are weighted edges in the semantic graph.
/// SPARSE: Only store salient (non-zero, meaningful) weights.
/// WEIGHTED AVERAGE: On conflict, compute running average of weights.
///
/// Extracted from query_store.hpp for separation of concerns.

#include "connection.hpp"
#include "pg_result.hpp"
#include "types.hpp"
#include "../atoms/node_ref.hpp"
#include <libpq-fe.h>
#include <optional>
#include <vector>
#include <cmath>
#include <cstdio>
#include <string>

namespace hartonomous::db {

/// Forward declaration for is_atom check
bool is_atom(std::int64_t high, std::int64_t low);

/// Relationship storage and retrieval operations.
/// Implements sparse encoding - near-zero weights are skipped.
class RelationshipStore {
    PgConnection& conn_;

public:
    explicit RelationshipStore(PgConnection& conn) : conn_(conn) {}

    /// Store a weighted relationship: from → to with weight.
    /// SPARSE: Only call this for salient (non-zero, meaningful) weights.
    /// WEIGHTED AVERAGE: On conflict, computes running average of weights.
    void store(NodeRef from, NodeRef to, double weight,
               RelType type = REL_DEFAULT, NodeRef context = NodeRef{}) {
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

        return execute_query(query, from, true);
    }

    /// Find all relationships FROM a node within a specific context.
    [[nodiscard]] std::vector<Relationship> find_from_with_context(
        NodeRef from, NodeRef context, std::size_t limit = 100) {
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

        return execute_query(query, from, true);
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

        return execute_query(query, to, false);
    }

    /// Find relationships by weight range (for model analysis).
    [[nodiscard]] std::vector<Relationship> find_by_weight(
        double min_weight, double max_weight,
        NodeRef context = NodeRef{},
        std::size_t limit = 1000) {
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
    void remove(NodeRef from, NodeRef to, NodeRef context = NodeRef{}) {
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

    /// Get relationship count.
    [[nodiscard]] std::int64_t count() {
        PgResult res(PQexec(conn_.get(), "SELECT COUNT(*) FROM relationship"));
        if (res.status() == PGRES_TUPLES_OK && res.row_count() > 0) {
            return std::stoll(res.get_value(0, 0));
        }
        return 0;
    }

private:
    /// Execute relationship query and parse results.
    [[nodiscard]] std::vector<Relationship> execute_query(
        const char* query, NodeRef fixed_node, bool fixed_is_from) {
        PgResult res(PQexec(conn_.get(), query));
        std::vector<Relationship> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                Relationship r;
                if (fixed_is_from) {
                    r.from = fixed_node;
                    r.to.id_high = std::stoll(res.get_value(i, 0));
                    r.to.id_low = std::stoll(res.get_value(i, 1));
                    r.to.is_atom = is_atom(r.to.id_high, r.to.id_low);
                } else {
                    r.from.id_high = std::stoll(res.get_value(i, 0));
                    r.from.id_low = std::stoll(res.get_value(i, 1));
                    r.from.is_atom = is_atom(r.from.id_high, r.from.id_low);
                    r.to = fixed_node;
                }
                r.weight = std::stod(res.get_value(i, 2));
                r.obs_count = std::stoi(res.get_value(i, 3));
                r.rel_type = static_cast<std::int16_t>(std::stoi(res.get_value(i, 4)));
                r.context.id_high = std::stoll(res.get_value(i, 5));
                r.context.id_low = std::stoll(res.get_value(i, 6));
                results.push_back(r);
            }
        }

        return results;
    }
};

} // namespace hartonomous::db
