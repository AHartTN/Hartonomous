#pragma once

/// MLOPS - AI/ML Operations on the Universal Substrate
///
/// This is NOT traditional AI. This is semantic graph traversal.
///
/// THE FUNDAMENTAL INSIGHT:
/// Neural networks encode RELATIONSHIPS between concepts, not positions in space.
/// A 7B parameter model is really a sparse graph of ~70M significant relationships.
/// The 99% near-zero weights encode ABSENCE of relationship, not information.
///
/// OPERATIONS:
/// - Attention: "Which concepts relate to this context?" = Spatial query + relationship walk
/// - Generation: "What follows from this?" = Weighted graph traversal
/// - Inference: "Input → Output" = Index lookup + multi-hop traversal
/// - Transformation: "Apply learned function" = Relationship composition
///
/// ALL operations reduce to:
/// 1. B-tree lookups (O(log n) - composition/relationship tables)
/// 2. R-tree range queries (O(log n) - spatial/GiST index on semantic positions)
/// 3. A* pathfinding (O(E log V) - graph traversal)
///
/// NO matrix multiplication. NO softmax. NO GPU.

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/merkle_hash.hpp"
#include "../atoms/semantic_decompose.hpp"
#include <vector>
#include <queue>
#include <unordered_set>
#include <unordered_map>
#include <algorithm>
#include <cmath>
#include <functional>
#include <limits>

namespace hartonomous::mlops {

using db::QueryStore;
using db::Relationship;
using db::RelType;
using db::Trajectory;
using db::TrajectoryPoint;
using db::SpatialMatch;

// ============================================================================
// ATTENTION - "Which concepts should I attend to?"
// ============================================================================

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
/// "king" traces a path. "monarch" traces a path. "ruler" traces a path.
/// Where these trajectories INTERSECT in 4D space = related concepts.
/// NOT clustering. NOT distance. INTERSECTION.
///
/// The concept of "king" is the VORONOI CELL - all points closer to
/// king's trajectory than to any other. Attention finds intersections.
class AttentionOp {
    QueryStore& store_;

public:
    explicit AttentionOp(QueryStore& store) : store_(store) {}

    /// Attend via TRAJECTORY INTERSECTION.
    /// Finds all compositions whose trajectories intersect with query's trajectory.
    /// THIS is how meaning is discovered - where paths cross in 4D semantic space.
    ///
    /// @param query The node to attend FROM
    /// @param intersection_threshold Maximum distance to consider an "intersection"
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
                total_score += 1.0 / (dist + 0.01);  // Avoid division by zero
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
            rels = store_.find_by_type(query, RelType::MODEL_WEIGHT, max_attend * 2);
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
    /// Uses spatial proximity as attention score (geometric similarity).
    ///
    /// @param query The query composition
    /// @param keys Candidate key compositions to attend to
    /// @param distance_scale Scaling factor for distance → attention conversion
    [[nodiscard]] AttentionResult cross_attend(
        NodeRef query,
        const std::vector<NodeRef>& keys,
        double distance_scale = 100.0)
    {
        AttentionResult result;
        result.query_ref = query;

        if (keys.empty()) return result;

        // Get spatial trajectory of query
        auto query_traj_opt = store_.get_trajectory(query, query);
        if (!query_traj_opt) {
            // No trajectory stored - fall back to relationship-based attention
            return attend(query, NodeRef{}, keys.size());
        }

        // Compute attention scores based on trajectory similarity
        // Use inverse distance as score: closer = higher attention
        std::vector<std::pair<NodeRef, double>> scores;
        scores.reserve(keys.size());

        for (const auto& key : keys) {
            auto key_traj_opt = store_.get_trajectory(key, key);
            if (!key_traj_opt) continue;

            // Compute simplified trajectory distance (Hausdorff-like)
            double dist = trajectory_distance(*query_traj_opt, *key_traj_opt);
            double score = distance_scale / (dist + 1.0);  // Inverse distance
            scores.emplace_back(key, score);
        }

        // Normalize scores
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

private:
    /// Compute simplified trajectory distance (point-wise Euclidean sum)
    [[nodiscard]] double trajectory_distance(
        const Trajectory& a, const Trajectory& b) const
    {
        if (a.points.empty() || b.points.empty()) {
            return std::numeric_limits<double>::max();
        }

        // Sum squared distances between corresponding points
        // Pad shorter trajectory with last point
        std::size_t len = std::max(a.points.size(), b.points.size());
        double sum_sq = 0.0;

        for (std::size_t i = 0; i < len; ++i) {
            const auto& pa = a.points[std::min(i, a.points.size() - 1)];
            const auto& pb = b.points[std::min(i, b.points.size() - 1)];

            double dx = static_cast<double>(pa.page - pb.page);
            double dy = static_cast<double>(pa.type - pb.type);
            double dz = static_cast<double>(pa.base - pb.base);
            double dm = static_cast<double>(pa.variant - pb.variant);

            sum_sq += dx*dx + dy*dy + dz*dz + dm*dm;
        }

        return std::sqrt(sum_sq);
    }
};

// ============================================================================
// GENERATION - "What comes next?"
// ============================================================================

/// Generation result: ranked candidates for next token/composition
struct GenerationResult {
    struct Candidate {
        NodeRef ref;
        double probability;   // Normalized probability
        double log_prob;      // Log probability
        std::string decoded;  // Decoded text (if decodable)
    };

    std::vector<Candidate> candidates;
    NodeRef context_ref;

    /// Sample from distribution (greedy = top-1)
    [[nodiscard]] NodeRef sample_greedy() const {
        return candidates.empty() ? NodeRef{} : candidates[0].ref;
    }

    /// Sample with temperature
    [[nodiscard]] NodeRef sample_temperature(double temperature, std::uint64_t seed) const {
        if (candidates.empty()) return NodeRef{};
        if (temperature <= 0.0) return sample_greedy();

        // Apply temperature to log probs and renormalize
        std::vector<double> adjusted_probs;
        adjusted_probs.reserve(candidates.size());
        double max_lp = candidates[0].log_prob;

        for (const auto& c : candidates) {
            double adj_lp = c.log_prob / temperature;
            adjusted_probs.push_back(std::exp(adj_lp - max_lp));  // Subtract max for numerical stability
        }

        double total = 0.0;
        for (double p : adjusted_probs) total += p;
        for (double& p : adjusted_probs) p /= total;

        // Sample from adjusted distribution
        std::uint64_t rand_state = seed;
        auto next_rand = [&]() -> double {
            rand_state = rand_state * 6364136223846793005ULL + 1442695040888963407ULL;
            return static_cast<double>(rand_state >> 33) / static_cast<double>(1ULL << 31);
        };

        double r = next_rand();
        double cumsum = 0.0;
        for (std::size_t i = 0; i < candidates.size(); ++i) {
            cumsum += adjusted_probs[i];
            if (r <= cumsum) {
                return candidates[i].ref;
            }
        }

        return candidates.back().ref;
    }
};

/// Generation operator - Trajectory endpoint intersection
///
/// Traditional generation:
///   hidden_state → linear projection → logits
///   probabilities = softmax(logits)
///   next_token = sample(probabilities)
///
/// Hartonomous generation:
///   context = NodeRef with trajectory through 4D space
///   The trajectory's ENDPOINT defines a region in semantic space
///   Find trajectories that PASS THROUGH that region
///   Those are the candidates for continuation
///
/// THE KEY INSIGHT: Generation is trajectory intersection at the endpoint.
/// "What comes next after 'The king sat on his ___'" =
/// "What trajectories intersect where 'his' ends?"
/// Answer: throne, chair, horse, bed... all pass through that region.
class GenerationOp {
    QueryStore& store_;

public:
    explicit GenerationOp(QueryStore& store) : store_(store) {}

    /// Generate candidates by finding trajectories through endpoint region.
    ///
    /// @param context Current context composition
    /// @param model_context Model to use for generation (unused - trajectories are universal)
    /// @param top_k Number of candidates to return
    [[nodiscard]] GenerationResult generate(
        NodeRef context,
        NodeRef model_context = NodeRef{},
        std::size_t top_k = 50)
    {
        GenerationResult result;
        result.context_ref = context;

        // PRIMARY: Get trajectory and find what intersects at endpoint
        auto traj_opt = store_.get_trajectory(context, context);
        if (traj_opt && !traj_opt->points.empty()) {
            // Get endpoint region
            const auto& endpoint = traj_opt->points.back();

            // Find trajectories passing through this region
            auto through = store_.query_trajectories_through_point(
                endpoint.page, endpoint.type, endpoint.base, endpoint.variant,
                2.0,  // radius
                top_k * 2);

            if (!through.empty()) {
                // Score by trajectory similarity (Frechet distance)
                result.candidates.reserve(through.size());
                for (const auto& ref : through) {
                    if (ref.id_high == context.id_high && ref.id_low == context.id_low) continue;

                    GenerationResult::Candidate cand;
                    cand.ref = ref;

                    // Get Frechet distance for scoring
                    auto other_traj = store_.get_trajectory(ref, ref);
                    if (other_traj) {
                        double dist = trajectory_distance(*traj_opt, *other_traj);
                        cand.log_prob = -dist;  // Negative distance as log prob
                        cand.probability = 0.0;  // Will normalize later
                    } else {
                        cand.log_prob = -10.0;  // Default low score
                    }

                    try {
                        cand.decoded = store_.decode_string(ref);
                    } catch (...) {
                        cand.decoded = "";
                    }

                    result.candidates.push_back(cand);
                }

                // Normalize probabilities via softmax
                if (!result.candidates.empty()) {
                    double max_lp = result.candidates[0].log_prob;
                    for (const auto& c : result.candidates) {
                        max_lp = std::max(max_lp, c.log_prob);
                    }

                    double sum_exp = 0.0;
                    for (auto& c : result.candidates) {
                        c.probability = std::exp(c.log_prob - max_lp);
                        sum_exp += c.probability;
                    }
                    for (auto& c : result.candidates) {
                        c.probability /= sum_exp;
                    }

                    // Sort by probability
                    std::sort(result.candidates.begin(), result.candidates.end(),
                        [](const auto& a, const auto& b) { return a.probability > b.probability; });

                    // Limit to top_k
                    if (result.candidates.size() > top_k) {
                        result.candidates.resize(top_k);
                    }

                    return result;
                }
            }
        }

        // FALLBACK: Use TEMPORAL_NEXT relationships
        auto rels = store_.find_by_type(context, RelType::TEMPORAL_NEXT, top_k * 2);
        if (model_context.id_high != 0 || model_context.id_low != 0) {
            rels.erase(
                std::remove_if(rels.begin(), rels.end(),
                    [&](const Relationship& r) {
                        return r.context.id_high != model_context.id_high ||
                               r.context.id_low != model_context.id_low;
                    }),
                rels.end());
        }

        if (rels.empty()) {
            return generate_by_similarity(context, top_k);
        }

        double total_weight = 0.0;
        for (const auto& rel : rels) {
            total_weight += std::exp(rel.weight);
        }

        result.candidates.reserve(std::min(top_k, rels.size()));
        for (std::size_t i = 0; i < std::min(top_k, rels.size()); ++i) {
            const auto& rel = rels[i];
            GenerationResult::Candidate cand;
            cand.ref = rel.to;
            cand.log_prob = rel.weight;
            cand.probability = std::exp(rel.weight) / total_weight;

            try {
                cand.decoded = store_.decode_string(rel.to);
            } catch (...) {
                cand.decoded = "";
            }

            result.candidates.push_back(cand);
        }

        return result;
    }

private:
    /// Compute simplified trajectory distance (point-wise Euclidean sum)
    [[nodiscard]] double trajectory_distance(
        const Trajectory& a, const Trajectory& b) const
    {
        if (a.points.empty() || b.points.empty()) {
            return std::numeric_limits<double>::max();
        }

        std::size_t len = std::max(a.points.size(), b.points.size());
        double sum_sq = 0.0;

        for (std::size_t i = 0; i < len; ++i) {
            const auto& pa = a.points[std::min(i, a.points.size() - 1)];
            const auto& pb = b.points[std::min(i, b.points.size() - 1)];

            double dx = static_cast<double>(pa.page - pb.page);
            double dy = static_cast<double>(pa.type - pb.type);
            double dz = static_cast<double>(pa.base - pb.base);
            double dm = static_cast<double>(pa.variant - pb.variant);

            sum_sq += dx*dx + dy*dy + dz*dz + dm*dm;
        }

        return std::sqrt(sum_sq);
    }

public:
    /// Generate by semantic similarity when no temporal relationships exist.
    /// Uses spatial proximity in semantic space.
    [[nodiscard]] GenerationResult generate_by_similarity(
        NodeRef context,
        std::size_t top_k = 50)
    {
        GenerationResult result;
        result.context_ref = context;

        // Get trajectory for context
        auto traj_opt = store_.get_trajectory(context, context);
        if (!traj_opt || traj_opt->points.empty()) {
            return result;  // No trajectory, can't generate by similarity
        }

        // Use last point in trajectory as query position
        const auto& last_pt = traj_opt->points.back();

        // Find atoms near this position using coordinate lookup
        SemanticCoord coord{
            static_cast<std::uint8_t>(last_pt.page),
            static_cast<std::uint8_t>(last_pt.type),
            last_pt.base,
            last_pt.variant
        };
        std::int32_t codepoint = SemanticDecompose::to_codepoint(coord);

        auto similar = store_.find_similar(codepoint, top_k * 2);

        // Convert spatial matches to candidates
        double total_inv_dist = 0.0;
        for (const auto& match : similar) {
            if (match.distance > 0) {
                total_inv_dist += 1.0 / match.distance;
            }
        }

        result.candidates.reserve(std::min(top_k, similar.size()));
        for (std::size_t i = 0; i < std::min(top_k, similar.size()); ++i) {
            const auto& match = similar[i];
            GenerationResult::Candidate cand;
            cand.ref = NodeRef::atom(match.hilbert_high, match.hilbert_low);

            if (match.distance > 0) {
                cand.probability = (1.0 / match.distance) / total_inv_dist;
                cand.log_prob = std::log(cand.probability);
            } else {
                cand.probability = 1.0;
                cand.log_prob = 0.0;
            }

            // Decode single character
            if (match.codepoint >= 0 && match.codepoint <= 127) {
                cand.decoded = std::string(1, static_cast<char>(match.codepoint));
            }

            result.candidates.push_back(cand);
        }

        return result;
    }
};

// ============================================================================
// INFERENCE - "Input → Output"
// ============================================================================

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
    double total_weight;  // Product of hop weights
    double path_length;   // Number of hops

    [[nodiscard]] bool success() const { return !path.empty(); }
};

/// Inference operator - replaces forward pass through network layers
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
class InferenceOp {
    QueryStore& store_;

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
        auto make_key = [](NodeRef r) -> std::uint64_t {
            return static_cast<std::uint64_t>(r.id_high) ^
                   (static_cast<std::uint64_t>(r.id_low) * 0x9e3779b97f4a7c15ULL);
        };

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
                edges = store_.find_by_type(current, RelType::MODEL_WEIGHT, 100);
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
    /// Finds best path from input to target.
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

        // Bidirectional A* from input and target
        // Forward from input
        std::unordered_map<std::uint64_t, std::pair<double, std::vector<InferenceResult::Hop>>> forward_paths;
        // Backward from target
        std::unordered_map<std::uint64_t, std::pair<double, std::vector<InferenceResult::Hop>>> backward_paths;

        auto make_key = [](NodeRef r) -> std::uint64_t {
            return static_cast<std::uint64_t>(r.id_high) ^
                   (static_cast<std::uint64_t>(r.id_low) * 0x9e3779b97f4a7c15ULL);
        };

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
            std::size_t fwd_size = fwd_queue.size();
            for (std::size_t i = 0; i < fwd_size; ++i) {
                auto [current, weight, path] = fwd_queue.front();
                fwd_queue.pop();

                auto edges = model_context.id_high != 0 || model_context.id_low != 0
                    ? store_.find_by_type(current, RelType::MODEL_WEIGHT, 50)
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

            // Expand backward (using incoming edges)
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
                            // Append current backward path
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

        result.path = best_path;
        result.total_weight = best_weight;
        result.path_length = static_cast<double>(best_path.size());

        return result;
    }
};

// ============================================================================
// TRANSFORMATION - "Apply learned function"
// ============================================================================

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
};

/// Transformation operator - replaces linear layer / MLP
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

        // Get transformation edges (MODEL_WEIGHT type)
        auto edges = store_.find_by_type(input, RelType::MODEL_WEIGHT, max_outputs * 2);

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

    /// Compute transformation similarity: how similar are two transformations?
    /// Uses trajectory Frechet distance via spatial query.
    [[nodiscard]] double transform_similarity(NodeRef a, NodeRef b) {
        // Get trajectories
        auto traj_a = store_.get_trajectory(a, a);
        auto traj_b = store_.get_trajectory(b, b);

        if (!traj_a || !traj_b) {
            return 0.0;  // No trajectories, can't compare
        }

        // Compute simplified Frechet-like distance
        // True Frechet requires ST_FrechetDistance in PostGIS
        double sum_sq = 0.0;
        std::size_t len = std::max(traj_a->points.size(), traj_b->points.size());

        for (std::size_t i = 0; i < len; ++i) {
            const auto& pa = traj_a->points[std::min(i, traj_a->points.size() - 1)];
            const auto& pb = traj_b->points[std::min(i, traj_b->points.size() - 1)];

            double dx = static_cast<double>(pa.page - pb.page);
            double dy = static_cast<double>(pa.type - pb.type);
            double dz = static_cast<double>(pa.base - pb.base);
            double dm = static_cast<double>(pa.variant - pb.variant);

            sum_sq += dx*dx + dy*dy + dz*dz + dm*dm;
        }

        double distance = std::sqrt(sum_sq);

        // Convert distance to similarity (inverse relationship)
        // similarity = 1 / (1 + distance) -> [0, 1]
        return 1.0 / (1.0 + distance);
    }
};

// ============================================================================
// UNIFIED API
// ============================================================================

/// MLOps interface - unified access to all operations
class MLOps {
    QueryStore& store_;
    AttentionOp attention_;
    GenerationOp generation_;
    InferenceOp inference_;
    TransformOp transform_;

public:
    explicit MLOps(QueryStore& store)
        : store_(store)
        , attention_(store)
        , generation_(store)
        , inference_(store)
        , transform_(store)
    {}

    // Attention operations
    [[nodiscard]] AttentionResult attend(NodeRef query, NodeRef context = NodeRef{}, std::size_t max = 64) {
        return attention_.attend(query, context, max);
    }

    [[nodiscard]] AttentionResult cross_attend(NodeRef query, const std::vector<NodeRef>& keys) {
        return attention_.cross_attend(query, keys);
    }

    // Generation operations
    [[nodiscard]] GenerationResult generate(NodeRef context, NodeRef model = NodeRef{}, std::size_t top_k = 50) {
        return generation_.generate(context, model, top_k);
    }

    [[nodiscard]] NodeRef generate_next(NodeRef context, double temperature = 1.0, std::uint64_t seed = 42) {
        auto result = generation_.generate(context);
        return result.sample_temperature(temperature, seed);
    }

    // Inference operations
    [[nodiscard]] InferenceResult infer(NodeRef input, std::size_t max_hops = 6, NodeRef model = NodeRef{}) {
        return inference_.infer(input, max_hops, model);
    }

    [[nodiscard]] InferenceResult infer_to(NodeRef input, NodeRef target, std::size_t max_hops = 10) {
        return inference_.infer_to(input, target, max_hops);
    }

    // Transformation operations
    [[nodiscard]] TransformResult transform(NodeRef input, NodeRef context = NodeRef{}) {
        return transform_.transform(input, context);
    }

    [[nodiscard]] TransformResult transform_chain(NodeRef input, const std::vector<NodeRef>& layers) {
        return transform_.transform_multi(input, layers);
    }

    [[nodiscard]] double similarity(NodeRef a, NodeRef b) {
        return transform_.transform_similarity(a, b);
    }

    // Direct store access for custom operations
    QueryStore& store() { return store_; }
};

} // namespace hartonomous::mlops
