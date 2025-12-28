#pragma once

/// GENERATION OPERATOR - Trajectory endpoint intersection
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

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/semantic_decompose.hpp"
#include "../atoms/semantic_coord.hpp"
#include "trajectory_utils.hpp"
#include <vector>
#include <algorithm>
#include <cmath>
#include <string>

namespace hartonomous::mlops {

using db::QueryStore;
using db::Relationship;
using db::RelType;
using db::REL_DEFAULT;
using db::Trajectory;
using db::SpatialMatch;
using hartonomous::SemanticCoord;

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

        std::vector<double> adjusted_probs;
        adjusted_probs.reserve(candidates.size());
        double max_lp = candidates[0].log_prob;

        for (const auto& c : candidates) {
            double adj_lp = c.log_prob / temperature;
            adjusted_probs.push_back(std::exp(adj_lp - max_lp));
        }

        double total = 0.0;
        for (double p : adjusted_probs) total += p;
        for (double& p : adjusted_probs) p /= total;

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
class GenerationOp {
    QueryStore& store_;

public:
    explicit GenerationOp(QueryStore& store) : store_(store) {}

    /// Generate candidates by finding trajectories through endpoint region.
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
            const auto& endpoint = traj_opt->points.back();

            auto through = store_.query_trajectories_through_point(
                endpoint.page, endpoint.type, endpoint.base, endpoint.variant,
                2.0, top_k * 2);

            if (!through.empty()) {
                result.candidates.reserve(through.size());
                for (const auto& ref : through) {
                    if (ref.id_high == context.id_high && ref.id_low == context.id_low) continue;

                    GenerationResult::Candidate cand;
                    cand.ref = ref;

                    auto other_traj = store_.get_trajectory(ref, ref);
                    if (other_traj) {
                        double dist = trajectory_distance(*traj_opt, *other_traj);
                        cand.log_prob = -dist;
                        cand.probability = 0.0;
                    } else {
                        cand.log_prob = -10.0;
                    }

                    try {
                        cand.decoded = store_.decode_string(ref);
                    } catch (...) {
                        cand.decoded = "";
                    }

                    result.candidates.push_back(cand);
                }

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

                    std::sort(result.candidates.begin(), result.candidates.end(),
                        [](const auto& a, const auto& b) { return a.probability > b.probability; });

                    if (result.candidates.size() > top_k) {
                        result.candidates.resize(top_k);
                    }

                    return result;
                }
            }
        }

        // FALLBACK: Use TEMPORAL_NEXT relationships
        auto rels = store_.find_by_type(context, REL_DEFAULT, top_k * 2);
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

    /// Generate by semantic similarity when no temporal relationships exist.
    [[nodiscard]] GenerationResult generate_by_similarity(
        NodeRef context,
        std::size_t top_k = 50)
    {
        GenerationResult result;
        result.context_ref = context;

        auto traj_opt = store_.get_trajectory(context, context);
        if (!traj_opt || traj_opt->points.empty()) {
            return result;
        }

        const auto& last_pt = traj_opt->points.back();

        SemanticCoord coord{
            static_cast<std::uint8_t>(last_pt.page),
            static_cast<std::uint8_t>(last_pt.type),
            last_pt.base,
            last_pt.variant
        };
        std::int32_t codepoint = SemanticDecompose::to_codepoint(coord);

        auto similar = store_.find_similar(codepoint, top_k * 2);

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
            cand.ref = NodeRef::atom(AtomId{match.hilbert_high, match.hilbert_low});

            if (match.distance > 0) {
                cand.probability = (1.0 / match.distance) / total_inv_dist;
                cand.log_prob = std::log(cand.probability);
            } else {
                cand.probability = 1.0;
                cand.log_prob = 0.0;
            }

            if (match.codepoint >= 0 && match.codepoint <= 127) {
                cand.decoded = std::string(1, static_cast<char>(match.codepoint));
            }

            result.candidates.push_back(cand);
        }

        return result;
    }
};

} // namespace hartonomous::mlops
