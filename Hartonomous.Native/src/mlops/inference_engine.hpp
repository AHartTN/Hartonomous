#pragma once

/// INFERENCE ENGINE - AI/MLOps queries using the substrate.
///
/// Implements inference without matrix multiplication by using
/// trajectory intersection in 4D semantic space.
///
/// Key operations:
/// - find_similar_tokens: KNN via trajectory distance
/// - compute_attention: Trajectory intersection = shared meaning
/// - forward_pass: Output distribution via aggregate intersection
///
/// Extracted from query_store.hpp for separation of concerns.

#include "../db/connection.hpp"
#include "../db/pg_result.hpp"
#include "../db/types.hpp"
#include "../atoms/node_ref.hpp"
#include <libpq-fe.h>
#include <vector>
#include <tuple>
#include <string>
#include <cstdio>

namespace hartonomous::mlops {

// Forward declaration
bool is_atom(std::int64_t high, std::int64_t low);

/// Inference engine using trajectory-based semantic search.
/// Replaces matrix multiplication with geometric intersection queries.
class InferenceEngine {
    db::PgConnection& conn_;

public:
    explicit InferenceEngine(db::PgConnection& conn) : conn_(conn) {}

    /// Find tokens with similar embeddings (trajectory intersection in 4D space).
    /// This is HOW inference works without matrix multiplication.
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> find_similar_tokens(
        NodeRef token_ref, NodeRef model_context, std::size_t limit = 10) {
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
        const std::vector<NodeRef>& tokens, NodeRef model_context, double threshold = 1.0) {
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

            db::PgResult res(PQexec(conn_.get(), query));
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
        const std::vector<NodeRef>& input_tokens, NodeRef model_context, std::size_t top_k = 10) {
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

        db::PgResult res(PQexec(conn_.get(), query));
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
        NodeRef model_context, std::size_t num_buckets = 20) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT width_bucket(weight, -1, 1, %zu) as bucket, COUNT(*) "
            "FROM relationship "
            "WHERE context_high = %lld AND context_low = %lld "
            "GROUP BY bucket ORDER BY bucket",
            num_buckets,
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low));

        db::PgResult res(PQexec(conn_.get(), query));
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
    [[nodiscard]] std::vector<db::Relationship> top_weights(
        NodeRef model_context, std::size_t limit = 100) {
        char query[512];
        std::snprintf(query, sizeof(query),
            "SELECT from_high, from_low, to_high, to_low, weight, obs_count, rel_type "
            "FROM relationship "
            "WHERE context_high = %lld AND context_low = %lld "
            "ORDER BY ABS(weight) DESC LIMIT %zu",
            static_cast<long long>(model_context.id_high),
            static_cast<long long>(model_context.id_low),
            limit);

        db::PgResult res(PQexec(conn_.get(), query));
        std::vector<db::Relationship> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                db::Relationship r;
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

        db::PgResult res(PQexec(conn_.get(), query));
        if (res.status() == PGRES_COMMAND_OK) {
            return static_cast<std::size_t>(std::atoll(PQcmdTuples(res.get())));
        }
        return 0;
    }

private:
    /// Execute trajectory query and return results.
    [[nodiscard]] std::vector<std::pair<NodeRef, double>> execute_trajectory_query(
        const char* query) {
        db::PgResult res(PQexec(conn_.get(), query));
        std::vector<std::pair<NodeRef, double>> results;

        if (res.status() == PGRES_TUPLES_OK) {
            for (int i = 0; i < res.row_count(); ++i) {
                NodeRef ref;
                ref.id_high = std::stoll(res.get_value(i, 0));
                ref.id_low = std::stoll(res.get_value(i, 1));
                ref.is_atom = is_atom(ref.id_high, ref.id_low);
                double dist = std::stod(res.get_value(i, 2));
                results.emplace_back(ref, dist);
            }
        }

        return results;
    }
};

} // namespace hartonomous::mlops
