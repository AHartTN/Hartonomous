#pragma once

/// TRANSFORMATION OPERATOR - Weighted edge aggregation
///
/// Traditional transformation:
///   y = Wx + b
///   or y = activation(W₂ * activation(W₁x + b₁) + b₂)
///
/// Hartonomous transformation:
///   input_ref → follow weighted edges → aggregate destination nodes
///   Each edge IS a weight connection
///   Aggregation replaces matrix multiplication
///
/// The key insight: W[i,j] * x[j] is "how much does input j contribute to output i"
/// This is exactly a weighted edge from input node j to output node i.

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

/// Transformation result: weighted aggregation of transformed inputs
struct TransformResult {
    struct Component {
        NodeRef ref;
        double weight;
        double contribution;  // Normalized contribution to output
    };

    std::vector<Component> components;
    double total_weight;

    /// Aggregate components into single output (weighted centroid)
    /// Returns the component with highest contribution
    [[nodiscard]] NodeRef aggregate() const {
        if (components.empty()) return NodeRef{};
        return components[0].ref;  // Top component
    }

    /// Get top-k components by contribution
    [[nodiscard]] std::vector<Component> top_k(std::size_t k) const {
        std::size_t count = std::min(k, components.size());
        return std::vector<Component>(components.begin(), components.begin() + count);
    }
};

/// Transformation operator - replaces linear layer / MLP
class TransformOp {
    QueryStore& store_;

public:
    explicit TransformOp(QueryStore& store) : store_(store) {}

    /// Apply transformation to input.
    /// Follows edges and aggregates by weight.
    ///
    /// @param input Input composition to transform
    /// @param context Transformation context (which layer/model)
    /// @param max_outputs Maximum output components
    [[nodiscard]] TransformResult transform(
        NodeRef input,
        NodeRef context = NodeRef{},
        std::size_t max_outputs = 100)
    {
        TransformResult result;
        result.total_weight = 0.0;

        // Get transformation edges
        auto edges = store_.find_by_type(input, REL_DEFAULT, max_outputs * 2);

        // Filter by context if specified
        if (context.id_high != 0 || context.id_low != 0) {
            edges.erase(
                std::remove_if(edges.begin(), edges.end(),
                    [&](const Relationship& r) {
                        return r.context.id_high != context.id_high ||
                               r.context.id_low != context.id_low;
                    }),
                edges.end());
        }

        if (edges.empty()) return result;

        // Compute total weight for normalization
        for (const auto& edge : edges) {
            result.total_weight += std::abs(edge.weight);
        }

        // Build components
        result.components.reserve(std::min(max_outputs, edges.size()));
        for (std::size_t i = 0; i < std::min(max_outputs, edges.size()); ++i) {
            const auto& edge = edges[i];
            TransformResult::Component comp;
            comp.ref = edge.to;
            comp.weight = edge.weight;
            comp.contribution = std::abs(edge.weight) / result.total_weight;
            result.components.push_back(comp);
        }

        // Sort by contribution
        std::sort(result.components.begin(), result.components.end(),
            [](const auto& a, const auto& b) {
                return a.contribution > b.contribution;
            });

        return result;
    }

    /// Apply multi-layer transformation (composition of transforms).
    /// Chains multiple transform() calls.
    ///
    /// @param input Input composition
    /// @param layer_contexts Ordered list of layer contexts to apply
    [[nodiscard]] TransformResult transform_multi(
        NodeRef input,
        const std::vector<NodeRef>& layer_contexts)
    {
        if (layer_contexts.empty()) {
            return TransformResult{};
        }

        NodeRef current = input;
        TransformResult result;

        for (const auto& layer_ctx : layer_contexts) {
            auto layer_result = transform(current, layer_ctx);
            if (layer_result.components.empty()) {
                break;  // No more transformations available
            }

            // Use top component as input to next layer
            current = layer_result.aggregate();
            result = layer_result;
        }

        return result;
    }

    /// Compute transformation similarity using trajectory Frechet-like distance.
    [[nodiscard]] double transform_similarity(NodeRef a, NodeRef b) {
        // Get trajectories
        auto traj_a = store_.get_trajectory(a, a);
        auto traj_b = store_.get_trajectory(b, b);

        if (!traj_a || !traj_b) {
            return 0.0;  // No trajectories, can't compare
        }

        // Use trajectory_distance from trajectory_utils
        double distance = trajectory_distance(*traj_a, *traj_b);

        // Convert distance to similarity (inverse relationship)
        // similarity = 1 / (1 + distance) -> [0, 1]
        return distance_to_similarity(distance);
    }
};

} // namespace hartonomous::mlops
