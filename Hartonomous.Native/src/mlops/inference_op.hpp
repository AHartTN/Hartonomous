#pragma once

/// INFERENCE OPERATOR - Graph pathfinding as forward pass
///
/// Traditional inference:
///   x₀ = input
///   x₁ = layer₁(x₀)
///   x₂ = layer₂(x₁)
///   ...
///   output = xₙ
///
/// Hartonomous inference:
///   input_ref = composition of input
///   output_ref = A* pathfinding through relationship graph
///   Each hop = one layer's transformation
///
/// The key insight: neural network layers ARE graph edges.
/// "Apply layer to input" = "Follow edge from input node"

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include <vector>
#include <queue>
#include <unordered_map>
#include <unordered_set>
#include <limits>
#include <cstdint>
#include <algorithm>

namespace hartonomous::mlops {

using db::QueryStore;
using db::Relationship;
using db::RelType;
using db::REL_DEFAULT;

/// Inference result: path through semantic graph from input to output
struct InferenceResult {
    struct Hop {
        NodeRef from;
        NodeRef to;
        double weight;
        RelType rel_type;
    };

    std::vector<Hop> path;
    NodeRef input_ref;
    NodeRef output_ref;
    double total_weight;   // Product of hop weights
    double path_length;    // Number of hops

    [[nodiscard]] bool success() const { return !path.empty(); }
};

/// Inference operator - replaces forward pass through network layers
class InferenceOp {
    QueryStore& store_;

    /// Hash function for NodeRef
    static std::uint64_t make_key(NodeRef r) {
        return static_cast<std::uint64_t>(r.id_high) ^
               (static_cast<std::uint64_t>(r.id_low) * 0x9e3779b97f4a7c15ULL);
    }

public:
    explicit InferenceOp(QueryStore& store) : store_(store) {}

    /// Run inference from input to find optimal output.
    /// Uses weighted A* pathfinding through relationship graph.
    ///
    /// @param input Input composition
    /// @param max_hops Maximum number of transformation steps
    /// @param model_context Model to use
    /// @return Best path through relationship graph
    [[nodiscard]] InferenceResult infer(
        NodeRef input,
        std::size_t max_hops = 6,
        NodeRef model_context = NodeRef{})
    {
        InferenceResult result;
        result.input_ref = input;
        result.total_weight = 1.0;
        result.path_length = 0;

        // Priority queue: (negative_weight, depth, current_node, path)
        using QueueEntry = std::tuple<double, std::size_t, NodeRef, std::vector<InferenceResult::Hop>>;
        auto cmp = [](const QueueEntry& a, const QueueEntry& b) {
            return std::get<0>(a) > std::get<0>(b);  // Max-heap by weight
        };
        std::priority_queue<QueueEntry, std::vector<QueueEntry>, decltype(cmp)> pq(cmp);

        // Visited nodes to avoid cycles
        std::unordered_set<std::uint64_t> visited;

        pq.emplace(0.0, 0, input, std::vector<InferenceResult::Hop>{});
        visited.insert(make_key(input));

        InferenceResult best;
        best.total_weight = -std::numeric_limits<double>::infinity();

        while (!pq.empty()) {
            auto [neg_weight, depth, current, path] = pq.top();
            pq.pop();

            // Check if this is better than best found
            if (-neg_weight > best.total_weight && !path.empty()) {
                best.path = path;
                best.input_ref = input;
                best.output_ref = current;
                best.total_weight = -neg_weight;
                best.path_length = static_cast<double>(path.size());
            }

            // Stop expanding if at max depth
            if (depth >= max_hops) continue;

            // Get outgoing edges
            std::vector<Relationship> edges;
            if (model_context.id_high != 0 || model_context.id_low != 0) {
                edges = store_.find_by_type(current, REL_DEFAULT, 100);
            } else {
                edges = store_.find_from(current, 100);
            }

            for (const auto& edge : edges) {
                std::uint64_t key = make_key(edge.to);
                if (visited.count(key)) continue;
                visited.insert(key);

                // Add hop to path
                std::vector<InferenceResult::Hop> new_path = path;
                new_path.push_back({current, edge.to, edge.weight,
                                    static_cast<RelType>(edge.rel_type)});

                // Compute cumulative weight (additive in log-space = multiplicative)
                double new_weight = -neg_weight + edge.weight;

                pq.emplace(-new_weight, depth + 1, edge.to, std::move(new_path));
            }
        }

        return best;
    }

    /// Run inference targeting a specific output.
    /// Finds best path from input to target using bidirectional A*.
    [[nodiscard]] InferenceResult infer_to(
        NodeRef input,
        NodeRef target,
        std::size_t max_hops = 10,
        NodeRef model_context = NodeRef{})
    {
        InferenceResult result;
        result.input_ref = input;
        result.output_ref = target;
        result.total_weight = 0.0;
        result.path_length = 0;

        // Bidirectional BFS from input and target
        using PathEntry = std::pair<double, std::vector<InferenceResult::Hop>>;
        std::unordered_map<std::uint64_t, PathEntry> forward_paths;
        std::unordered_map<std::uint64_t, PathEntry> backward_paths;

        // BFS forward from input
        std::queue<std::tuple<NodeRef, double, std::vector<InferenceResult::Hop>>> fwd_queue;
        fwd_queue.emplace(input, 0.0, std::vector<InferenceResult::Hop>{});
        forward_paths[make_key(input)] = {0.0, {}};

        // BFS backward from target
        std::queue<std::tuple<NodeRef, double, std::vector<InferenceResult::Hop>>> bwd_queue;
        bwd_queue.emplace(target, 0.0, std::vector<InferenceResult::Hop>{});
        backward_paths[make_key(target)] = {0.0, {}};

        double best_weight = -std::numeric_limits<double>::infinity();
        std::vector<InferenceResult::Hop> best_path;

        for (std::size_t depth = 0; depth < max_hops / 2; ++depth) {
            // Expand forward
            expand_forward(fwd_queue, forward_paths, backward_paths,
                          model_context, best_weight, best_path);

            // Expand backward (using incoming edges)
            expand_backward(bwd_queue, forward_paths, backward_paths,
                           best_weight, best_path);
        }

        result.path = best_path;
        result.total_weight = best_weight;
        result.path_length = static_cast<double>(best_path.size());

        return result;
    }

private:
    using PathEntry = std::pair<double, std::vector<InferenceResult::Hop>>;

    void expand_forward(
        std::queue<std::tuple<NodeRef, double, std::vector<InferenceResult::Hop>>>& fwd_queue,
        std::unordered_map<std::uint64_t, PathEntry>& forward_paths,
        std::unordered_map<std::uint64_t, PathEntry>& backward_paths,
        NodeRef model_context,
        double& best_weight,
        std::vector<InferenceResult::Hop>& best_path)
    {
        std::size_t fwd_size = fwd_queue.size();
        for (std::size_t i = 0; i < fwd_size; ++i) {
            auto [current, weight, path] = fwd_queue.front();
            fwd_queue.pop();

            auto edges = (model_context.id_high != 0 || model_context.id_low != 0)
                ? store_.find_by_type(current, REL_DEFAULT, 50)
                : store_.find_from(current, 50);

            for (const auto& edge : edges) {
                std::uint64_t key = make_key(edge.to);
                double new_weight = weight + edge.weight;

                // Check if meets backward search
                auto bwd_it = backward_paths.find(key);
                if (bwd_it != backward_paths.end()) {
                    double total = new_weight + bwd_it->second.first;
                    if (total > best_weight) {
                        best_weight = total;
                        best_path = path;
                        best_path.push_back({current, edge.to, edge.weight,
                                            static_cast<RelType>(edge.rel_type)});
                        // Append backward path in reverse
                        for (auto it = bwd_it->second.second.rbegin();
                             it != bwd_it->second.second.rend(); ++it) {
                            best_path.push_back(*it);
                        }
                    }
                }

                // Add to forward paths if better
                auto fwd_it = forward_paths.find(key);
                if (fwd_it == forward_paths.end() || new_weight > fwd_it->second.first) {
                    std::vector<InferenceResult::Hop> new_path = path;
                    new_path.push_back({current, edge.to, edge.weight,
                                       static_cast<RelType>(edge.rel_type)});
                    forward_paths[key] = {new_weight, new_path};
                    fwd_queue.emplace(edge.to, new_weight, std::move(new_path));
                }
            }
        }
    }

    void expand_backward(
        std::queue<std::tuple<NodeRef, double, std::vector<InferenceResult::Hop>>>& bwd_queue,
        std::unordered_map<std::uint64_t, PathEntry>& forward_paths,
        std::unordered_map<std::uint64_t, PathEntry>& backward_paths,
        double& best_weight,
        std::vector<InferenceResult::Hop>& best_path)
    {
        std::size_t bwd_size = bwd_queue.size();
        for (std::size_t i = 0; i < bwd_size; ++i) {
            auto [current, weight, path] = bwd_queue.front();
            bwd_queue.pop();

            auto edges = store_.find_to(current, 50);

            for (const auto& edge : edges) {
                std::uint64_t key = make_key(edge.from);
                double new_weight = weight + edge.weight;

                // Check if meets forward search
                auto fwd_it = forward_paths.find(key);
                if (fwd_it != forward_paths.end()) {
                    double total = new_weight + fwd_it->second.first;
                    if (total > best_weight) {
                        best_weight = total;
                        best_path = fwd_it->second.second;
                        best_path.push_back({edge.from, current, edge.weight,
                                            static_cast<RelType>(edge.rel_type)});
                        for (const auto& hop : path) {
                            best_path.push_back(hop);
                        }
                    }
                }

                // Add to backward paths if better
                auto bwd_it = backward_paths.find(key);
                if (bwd_it == backward_paths.end() || new_weight > bwd_it->second.first) {
                    std::vector<InferenceResult::Hop> new_path = path;
                    new_path.insert(new_path.begin(),
                        InferenceResult::Hop{edge.from, current, edge.weight,
                                            static_cast<RelType>(edge.rel_type)});
                    backward_paths[key] = {new_weight, new_path};
                    bwd_queue.emplace(edge.from, new_weight, std::move(new_path));
                }
            }
        }
    }
};

} // namespace hartonomous::mlops
