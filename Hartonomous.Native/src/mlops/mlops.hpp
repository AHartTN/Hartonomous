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
///
/// ARCHITECTURE (Single Responsibility):
/// - attention_op.hpp    -> AttentionOp, AttentionResult
/// - generation_op.hpp   -> GenerationOp, GenerationResult  
/// - inference_op.hpp    -> InferenceOp, InferenceResult
/// - transform_op.hpp    -> TransformOp, TransformResult
/// - trajectory_utils.hpp -> trajectory_distance, distance_to_*
/// - mlops.hpp (this)    -> MLOps unified facade

#include "attention_op.hpp"
#include "generation_op.hpp"
#include "inference_op.hpp"
#include "transform_op.hpp"
#include "trajectory_utils.hpp"

namespace hartonomous::mlops {

// Re-export result types for convenience
using AttentionResult = AttentionResult;
using GenerationResult = GenerationResult;
using InferenceResult = InferenceResult;
using TransformResult = TransformResult;

/// MLOps interface - unified access to all operations
///
/// Provides a single entry point for all ML operations without
/// requiring callers to manage individual operator instances.
///
/// Usage:
///   MLOps ops(store);
///   auto attended = ops.attend(query, context);
///   auto next = ops.generate_next(context, temperature);
///   auto path = ops.infer(input, max_hops);
///   auto transformed = ops.transform(input, layer_context);
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

    // ========================================================================
    // Attention operations
    // ========================================================================

    /// Find related concepts through spatial proximity and relationships.
    [[nodiscard]] AttentionResult attend(
        NodeRef query, 
        NodeRef context = NodeRef{}, 
        std::size_t max = 64) 
    {
        return attention_.attend(query, context, max);
    }

    /// Cross-attention: compare query against explicit key set.
    [[nodiscard]] AttentionResult cross_attend(
        NodeRef query, 
        const std::vector<NodeRef>& keys) 
    {
        return attention_.cross_attend(query, keys);
    }

    // ========================================================================
    // Generation operations
    // ========================================================================

    /// Generate candidates for next token/composition.
    [[nodiscard]] GenerationResult generate(
        NodeRef context, 
        NodeRef model = NodeRef{}, 
        std::size_t top_k = 50) 
    {
        return generation_.generate(context, model, top_k);
    }

    /// Generate and sample next token with temperature.
    [[nodiscard]] NodeRef generate_next(
        NodeRef context, 
        double temperature = 1.0, 
        std::uint64_t seed = 42) 
    {
        auto result = generation_.generate(context);
        return result.sample_temperature(temperature, seed);
    }

    // ========================================================================
    // Inference operations
    // ========================================================================

    /// Run inference from input to find optimal output path.
    [[nodiscard]] InferenceResult infer(
        NodeRef input, 
        std::size_t max_hops = 6, 
        NodeRef model = NodeRef{}) 
    {
        return inference_.infer(input, max_hops, model);
    }

    /// Run targeted inference to find path to specific output.
    [[nodiscard]] InferenceResult infer_to(
        NodeRef input, 
        NodeRef target, 
        std::size_t max_hops = 10) 
    {
        return inference_.infer_to(input, target, max_hops);
    }

    // ========================================================================
    // Transformation operations
    // ========================================================================

    /// Apply single-layer transformation.
    [[nodiscard]] TransformResult transform(
        NodeRef input, 
        NodeRef context = NodeRef{}) 
    {
        return transform_.transform(input, context);
    }

    /// Apply multi-layer transformation chain.
    [[nodiscard]] TransformResult transform_chain(
        NodeRef input, 
        const std::vector<NodeRef>& layers) 
    {
        return transform_.transform_multi(input, layers);
    }

    /// Compute similarity between two nodes via trajectory comparison.
    [[nodiscard]] double similarity(NodeRef a, NodeRef b) {
        return transform_.transform_similarity(a, b);
    }

    // ========================================================================
    // Direct access
    // ========================================================================

    /// Direct store access for custom operations.
    QueryStore& store() { return store_; }

    /// Access individual operators for advanced use cases.
    AttentionOp& attention_op() { return attention_; }
    GenerationOp& generation_op() { return generation_; }
    InferenceOp& inference_op() { return inference_; }
    TransformOp& transform_op() { return transform_; }
};

} // namespace hartonomous::mlops
