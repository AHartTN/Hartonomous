#pragma once

/// ATTENTION OPERATOR - Trajectory intersection-based attention
///
/// Traditional attention:
///   Q, K, V = projections of input
///   A = softmax(QK^T / sqrt(d))
///   Output = A @ V
///
/// Hartonomous attention:
///   Query = NodeRef with trajectory through 4D space
///   Keys = Trajectories that INTERSECT with query trajectory
///   Scores = Inverse distance at intersection points
///   Output = Nodes whose trajectories cross query trajectory
///
/// THE KEY INSIGHT: Meaning emerges at TRAJECTORY INTERSECTIONS.

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include "trajectory_utils.hpp"
#include <vector>
#include <algorithm>
#include <cmath>

namespace hartonomous::mlops {

using db::QueryStore;
using db::Relationship;
using db::RelType;
using db::REL_DEFAULT;
using db::Trajectory;

/// Attention result: a weighted set of attended nodes
struct AttentionResult {
    struct AttendedNode {
        NodeRef ref;
        double attention_weight;  // Normalized (sums to 1)
        double raw_score;         // Pre-normalization score
    };

    std::vector<AttendedNode> attended;
    NodeRef query_ref;  // What we were attending FROM

    /// Get top-K attended nodes
    [[nodiscard]] std::vector<AttendedNode> top_k(std::size_t k) const {
        std::vector<AttendedNode> result;
        result.reserve(std::min(k, attended.size()));
        for (std::size_t i = 0; i < std::min(k, attended.size()); ++i) {
            result.push_back(attended[i]);
        }
        return result;
    }
};

/// Attention operator - Trajectory Intersection based
class AttentionOp {
    QueryStore& store_;

public:
    explicit AttentionOp(QueryStore& store) : store_(store) {}

    /// Attend via TRAJECTORY INTERSECTION.
    /// Finds all compositions whose trajectories intersect with query's trajectory.
    ///
    /// @param query The node to attend FROM
    /// @param context Optional context for filtering
    /// @param max_attend Maximum nodes to attend to
    /// @return Nodes whose trajectories intersect query trajectory
    [[nodiscard]] AttentionResult attend(
        NodeRef query,
        NodeRef context = NodeRef{},
        std::size_t max_attend = 64)
    {
        AttentionResult result;
        result.query_ref = query;

        // PRIMARY: Find trajectory intersections - where meaning lives
        auto intersections = store_.query_trajectory_intersections(query, 1.0);

        if (!intersections.empty()) {
            // Score by inverse distance at intersection (closer = stronger)
            double total_score = 0.0;
            for (const auto& [ref, dist] : intersections) {
                total_score += 1.0 / (dist + 0.01);
            }

            result.attended.reserve(std::min(max_attend, intersections.size()));
            for (std::size_t i = 0; i < std::min(max_attend, intersections.size()); ++i) {
                const auto& [ref, dist] = intersections[i];
                AttentionResult::AttendedNode node;
                node.ref = ref;
                node.raw_score = 1.0 / (dist + 0.01);
                node.attention_weight = node.raw_score / total_score;
                result.attended.push_back(node);
            }

            std::sort(result.attended.begin(), result.attended.end(),
                [](const auto& a, const auto& b) {
                    return a.attention_weight > b.attention_weight;
                });

            return result;
        }

        // FALLBACK: Use relationship edges when no trajectories stored yet
        std::vector<Relationship> rels;
        if (context.id_high != 0 || context.id_low != 0) {
            rels = store_.find_by_type(query, REL_DEFAULT, max_attend * 2);
            rels.erase(
                std::remove_if(rels.begin(), rels.end(),
                    [&](const Relationship& r) {
                        return r.context.id_high != context.id_high ||
                               r.context.id_low != context.id_low;
                    }),
                rels.end());
        } else {
            rels = store_.find_from(query, max_attend * 2);
        }

        if (rels.empty()) return result;

        double total_abs_weight = 0.0;
        for (const auto& rel : rels) {
            total_abs_weight += std::abs(rel.weight);
        }

        if (total_abs_weight < 1e-10) return result;

        result.attended.reserve(std::min(max_attend, rels.size()));
        for (std::size_t i = 0; i < std::min(max_attend, rels.size()); ++i) {
            const auto& rel = rels[i];
            AttentionResult::AttendedNode node;
            node.ref = rel.to;
            node.raw_score = rel.weight;
            node.attention_weight = std::abs(rel.weight) / total_abs_weight;
            result.attended.push_back(node);
        }

        std::sort(result.attended.begin(), result.attended.end(),
            [](const auto& a, const auto& b) {
                return a.attention_weight > b.attention_weight;
            });

        return result;
    }

    /// Cross-attention: attend from query to a set of key nodes.
    /// Uses spatial proximity as attention score.
    [[nodiscard]] AttentionResult cross_attend(
        NodeRef query,
        const std::vector<NodeRef>& keys,
        double distance_scale = 100.0)
    {
        AttentionResult result;
        result.query_ref = query;

        if (keys.empty()) return result;

        auto query_traj_opt = store_.get_trajectory(query, query);
        if (!query_traj_opt) {
            return attend(query, NodeRef{}, keys.size());
        }

        std::vector<std::pair<NodeRef, double>> scores;
        scores.reserve(keys.size());

        for (const auto& key : keys) {
            auto key_traj_opt = store_.get_trajectory(key, key);
            if (!key_traj_opt) continue;

            double dist = trajectory_distance(*query_traj_opt, *key_traj_opt);
            double score = distance_scale / (dist + 1.0);
            scores.emplace_back(key, score);
        }

        double total_score = 0.0;
        for (const auto& [_, score] : scores) {
            total_score += score;
        }

        if (total_score < 1e-10) return result;

        result.attended.reserve(scores.size());
        for (const auto& [ref, score] : scores) {
            AttentionResult::AttendedNode node;
            node.ref = ref;
            node.raw_score = score;
            node.attention_weight = score / total_score;
            result.attended.push_back(node);
        }

        std::sort(result.attended.begin(), result.attended.end(),
            [](const auto& a, const auto& b) {
                return a.attention_weight > b.attention_weight;
            });

        return result;
    }
};

} // namespace hartonomous::mlops
