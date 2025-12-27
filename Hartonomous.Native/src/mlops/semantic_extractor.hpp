#pragma once

/// SEMANTIC EXTRACTION - Extract EXPLICIT relationships from model structure
///
/// WHAT THIS IS:
/// - Attention patterns: token A attends to token B (explicit edge)
/// - Layer connections: input position → output position (sparse weights)
///
/// WHAT THIS IS NOT:
/// - Similarity computation (that's a QUERY, not stored data)
/// - Pairwise comparison (trajectory intersection at query time)
///
/// For embeddings, use model_ingest.hpp which stores them as trajectories.
/// Similarity discovery happens at query time via ST_FrechetDistance.

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include "../threading/threading.hpp"
#include "safetensor.hpp"
#include <vector>
#include <cmath>
#include <algorithm>
#include <iostream>

namespace hartonomous::mlops {

using db::QueryStore;
using db::RelType;

/// Extracted relationship from model structure
struct ExtractedRelationship {
    NodeRef from;
    NodeRef to;
    double weight;
    RelType type;
};

/// Extraction statistics
struct ExtractionStats {
    std::size_t items_processed;
    std::size_t relationships_extracted;
    std::chrono::milliseconds duration;
};

/// Semantic Extractor - extracts EXPLICIT relationships from model architecture
class SemanticExtractor {
    QueryStore& store_;
    std::size_t top_k_per_query_;

public:
    explicit SemanticExtractor(
        QueryStore& store,
        std::size_t top_k_per_query = 10)
        : store_(store)
        , top_k_per_query_(top_k_per_query)
    {}

    // ========================================================================
    // ATTENTION EXTRACTION - Token-attends-to-Token (EXPLICIT relationship)
    // ========================================================================

    /// Extract attention patterns from attention weight tensor.
    /// Attention weights ARE explicit relationships - which tokens attend to which.
    /// This is NOT similarity - it's structural information from the model.
    [[nodiscard]] ExtractionStats extract_attention(
        const float* attention_weights,  // [num_heads × seq_len × seq_len]
        std::size_t num_heads,
        std::size_t seq_len,
        const std::vector<NodeRef>& position_refs,
        NodeRef layer_context)
    {
        auto start = std::chrono::high_resolution_clock::now();
        ExtractionStats stats{};
        stats.items_processed = num_heads * seq_len;

        std::vector<ExtractedRelationship> all_rels;
        all_rels.reserve(num_heads * seq_len * top_k_per_query_);

        for (std::size_t h = 0; h < num_heads; ++h) {
            const float* head_weights = attention_weights + h * seq_len * seq_len;

            for (std::size_t i = 0; i < seq_len; ++i) {
                if (position_refs[i].id_high == 0 && position_refs[i].id_low == 0) continue;

                const float* row = head_weights + i * seq_len;

                // Find top-K attended positions
                std::vector<std::pair<float, std::size_t>> scores;
                scores.reserve(seq_len);
                for (std::size_t j = 0; j < seq_len; ++j) {
                    if (position_refs[j].id_high == 0 && position_refs[j].id_low == 0) continue;
                    scores.emplace_back(row[j], j);
                }

                std::size_t k = std::min(top_k_per_query_, scores.size());
                std::partial_sort(scores.begin(), scores.begin() + k, scores.end(),
                    [](const auto& a, const auto& b) { return a.first > b.first; });

                for (std::size_t idx = 0; idx < k; ++idx) {
                    auto [weight, j] = scores[idx];
                    if (weight < 0.01) continue;

                    all_rels.push_back({
                        position_refs[i],
                        position_refs[j],
                        static_cast<double>(weight),
                        RelType::SEMANTIC_LINK
                    });
                }
            }
        }

        store_relationships(all_rels, layer_context);

        auto end = std::chrono::high_resolution_clock::now();
        stats.relationships_extracted = all_rels.size();
        stats.duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

        return stats;
    }

    // ========================================================================
    // LAYER WEIGHT EXTRACTION - Sparse transformation connections
    // ========================================================================

    /// Extract sparse connections from layer weight matrix.
    /// These are EXPLICIT structural relationships in the model.
    [[nodiscard]] ExtractionStats extract_layer_weights(
        const float* weights,
        std::size_t out_features,
        std::size_t in_features,
        NodeRef input_context,
        NodeRef output_context,
        NodeRef layer_context,
        double sparsity_threshold = 0.01)
    {
        auto start = std::chrono::high_resolution_clock::now();
        ExtractionStats stats{};
        stats.items_processed = out_features * in_features;

        std::vector<ExtractedRelationship> all_rels;
        all_rels.reserve(stats.items_processed / 100);

        auto make_position_ref = [](NodeRef context, std::size_t pos) -> NodeRef {
            std::int64_t high = context.id_high ^ static_cast<std::int64_t>(pos >> 32);
            std::int64_t low = context.id_low ^ static_cast<std::int64_t>(pos & 0xFFFFFFFF);
            return NodeRef::comp(high, low);
        };

        for (std::size_t i = 0; i < out_features; ++i) {
            NodeRef output_ref = make_position_ref(output_context, i);
            const float* row = weights + i * in_features;

            for (std::size_t j = 0; j < in_features; ++j) {
                float w = row[j];
                if (std::abs(w) < sparsity_threshold) continue;

                NodeRef input_ref = make_position_ref(input_context, j);
                all_rels.push_back({
                    input_ref,
                    output_ref,
                    static_cast<double>(w),
                    RelType::MODEL_WEIGHT
                });
            }
        }

        store_relationships(all_rels, layer_context);

        auto end = std::chrono::high_resolution_clock::now();
        stats.relationships_extracted = all_rels.size();
        stats.duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

        return stats;
    }

private:
    void store_relationships(
        const std::vector<ExtractedRelationship>& rels,
        NodeRef context)
    {
        if (rels.empty()) return;

        std::vector<std::tuple<NodeRef, NodeRef, double>> weights;
        weights.reserve(rels.size());

        for (const auto& rel : rels) {
            weights.emplace_back(rel.from, rel.to, rel.weight);
        }

        RelType primary_type = rels.empty() ? RelType::SEMANTIC_LINK : rels[0].type;
        store_.store_model_weights(weights, context, primary_type);
    }
};

} // namespace hartonomous::mlops
