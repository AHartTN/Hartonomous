#pragma once

/// WEIGHT COLLECTION - Sparse weight extraction from safetensors
///
/// Responsibilities:
/// - Dynamic threshold computation for sparsity
/// - 2D weight matrix collection (input→output relationships)
/// - 1D/sparse weight collection
/// - Embedding similarity extraction (token-to-token relationships)
///
/// All methods return tuples for batch storage - no I/O here.

#include "../atoms/node_ref.hpp"
#include "../atoms/merkle_hash.hpp"
#include "../threading/threading.hpp"
#include "safetensor.hpp"
#include <vector>
#include <tuple>
#include <cmath>
#include <algorithm>
#include <atomic>
#include <iostream>
#include <random>
#include <chrono>

namespace hartonomous::model {

/// Weight collection utilities - stateless functions for sparse extraction
class WeightCollector {
public:
    using WeightTuple = std::tuple<NodeRef, NodeRef, double>;

    /// Compute dynamic sparsity threshold based on tensor statistics.
    /// Keep top sparsity_percent% most significant weights (by magnitude).
    [[nodiscard]] static double compute_dynamic_threshold(
        const float* data,
        std::size_t count,
        double sparsity_percent)
    {
        if (count < 100) return 1e-9;  // Keep everything for tiny tensors

        // Sample to find magnitude distribution (avoid scanning entire tensor)
        std::size_t sample_size = std::min(count, std::size_t(10000));
        std::size_t stride = count / sample_size;

        std::vector<float> magnitudes;
        magnitudes.reserve(sample_size);
        for (std::size_t i = 0; i < count; i += stride) {
            magnitudes.push_back(std::abs(data[i]));
        }

        // Percentile = (100 - sparsity_percent) / 100
        // e.g., sparsity_percent=10 means keep top 10%, so threshold is 90th percentile
        double percentile = (100.0 - sparsity_percent) / 100.0;
        std::size_t threshold_idx = static_cast<std::size_t>(magnitudes.size() * percentile);
        threshold_idx = std::min(threshold_idx, magnitudes.size() - 1);

        std::nth_element(magnitudes.begin(),
                         magnitudes.begin() + static_cast<std::ptrdiff_t>(threshold_idx),
                         magnitudes.end());

        return static_cast<double>(magnitudes[threshold_idx]);
    }

    /// Collect 2D weight matrix as (tensor, edge, weight) tuples.
    /// Matrix layout: matrix[out_idx][in_idx] = weight from input to output
    [[nodiscard]] static std::size_t collect_weight_matrix(
        const SafetensorReader& reader,
        const TensorMeta& meta,
        NodeRef tensor_ref,
        double sparsity_percent,
        std::vector<WeightTuple>& output)
    {
        if (meta.shape.size() != 2) return 0;

        std::size_t out_dim = meta.shape[0];  // rows = output features
        std::size_t in_dim = meta.shape[1];   // cols = input features
        std::size_t count = out_dim * in_dim;
        const float* data = reader.get_f32_data(meta);

        // Dynamic threshold for this matrix
        double threshold = compute_dynamic_threshold(data, count, sparsity_percent);

        // Small matrices: sequential
        if (count < 10000) {
            return collect_matrix_sequential(data, out_dim, in_dim, tensor_ref, threshold, output);
        }

        // Large matrices: parallel by output row
        return collect_matrix_parallel(data, out_dim, in_dim, tensor_ref, threshold, output);
    }

    /// Collect 1D or other-dimensional tensor weights.
    [[nodiscard]] static std::size_t collect_sparse_weights(
        const SafetensorReader& reader,
        const TensorMeta& meta,
        NodeRef tensor_ref,
        double sparsity_percent,
        std::vector<WeightTuple>& output)
    {
        std::size_t count = SafetensorReader::element_count(meta);
        const float* data = reader.get_f32_data(meta);

        double threshold = compute_dynamic_threshold(data, count, sparsity_percent);

        // Small tensors: sequential fast path
        if (count < 10000) {
            std::size_t collected = 0;
            for (std::size_t i = 0; i < count; ++i) {
                float val = data[i];
                if (std::abs(val) < threshold) continue;
                output.emplace_back(tensor_ref, make_index_ref(i), static_cast<double>(val));
                ++collected;
            }
            return collected;
        }

        // Large tensors: parallel
        return collect_sparse_parallel(data, count, tensor_ref, threshold, output);
    }

    /// Extract token-to-token relationships from embedding matrix via cosine similarity.
    ///
    /// Embeddings are TEMPORARY - they exist only to compute which tokens relate.
    /// We compute cosine similarity between token pairs, then DISCARD the embeddings.
    template<typename TokenRefFn>
    [[nodiscard]] static std::size_t collect_embeddings(
        const float* data,
        std::size_t vocab_size,
        std::size_t hidden_dim,
        TokenRefFn get_token_ref,  // fn(size_t idx) -> NodeRef
        double similarity_threshold,
        std::size_t max_neighbors_per_token,
        std::vector<WeightTuple>& output)
    {
        std::cerr << "collect_embeddings: computing token-to-token similarity for "
                  << vocab_size << " tokens\n";

        // Precompute L2 norms for cosine similarity
        std::vector<float> norms(vocab_size);
        for (std::size_t i = 0; i < vocab_size; ++i) {
            const float* embed = data + i * hidden_dim;
            float sum = 0.0f;
            for (std::size_t d = 0; d < hidden_dim; ++d) {
                sum += embed[d] * embed[d];
            }
            norms[i] = std::sqrt(sum);
        }

        // Parallel: each thread handles a chunk of source tokens
        std::size_t num_threads = Threading::default_thread_count();
        std::size_t chunk_size = (vocab_size + num_threads - 1) / num_threads;
        std::vector<std::vector<WeightTuple>> thread_weights(num_threads);

        std::atomic<std::size_t> pairs_compared{0};

        Threading::parallel_for(num_threads, [&](std::size_t tid) {
            std::size_t start = tid * chunk_size;
            std::size_t end = std::min(start + chunk_size, vocab_size);
            if (start >= vocab_size) return;

            auto& local = thread_weights[tid];
            local.reserve((end - start) * max_neighbors_per_token);

            std::vector<std::pair<double, std::size_t>> similarities;
            similarities.reserve(vocab_size);

            for (std::size_t i = start; i < end; ++i) {
                NodeRef from_ref = get_token_ref(i);
                if (from_ref.id_high == 0 && from_ref.id_low == 0) continue;
                if (norms[i] < 1e-8f) continue;

                const float* embed_i = data + i * hidden_dim;
                similarities.clear();

                // Compare with all other tokens (j > i to avoid duplicates)
                for (std::size_t j = i + 1; j < vocab_size; ++j) {
                    if (norms[j] < 1e-8f) continue;

                    const float* embed_j = data + j * hidden_dim;

                    float dot = 0.0f;
                    for (std::size_t d = 0; d < hidden_dim; ++d) {
                        dot += embed_i[d] * embed_j[d];
                    }
                    double sim = static_cast<double>(dot) /
                                 (static_cast<double>(norms[i]) * static_cast<double>(norms[j]));

                    pairs_compared++;

                    if (sim > similarity_threshold) {
                        similarities.emplace_back(sim, j);
                    }
                }

                // Sort by similarity descending, take top-K
                if (similarities.size() > max_neighbors_per_token) {
                    std::partial_sort(similarities.begin(),
                                     similarities.begin() + max_neighbors_per_token,
                                     similarities.end(),
                                     [](const auto& a, const auto& b) {
                                         return a.first > b.first;
                                     });
                    similarities.resize(max_neighbors_per_token);
                }

                // Store as bidirectional relationships
                for (const auto& [sim, j] : similarities) {
                    NodeRef to_ref = get_token_ref(j);
                    if (to_ref.id_high == 0 && to_ref.id_low == 0) continue;

                    local.emplace_back(from_ref, to_ref, sim);
                    local.emplace_back(to_ref, from_ref, sim);
                }
            }
        });

        // Merge thread results
        std::size_t stored = 0;
        for (auto& tw : thread_weights) {
            stored += tw.size();
            output.insert(output.end(),
                std::make_move_iterator(tw.begin()),
                std::make_move_iterator(tw.end()));
        }

        std::cerr << "collect_embeddings: compared " << pairs_compared.load()
                  << " pairs, stored " << stored << " relationships\n";

        return stored;
    }

    /// FAST sampled embedding similarity - O(n*k) instead of O(n²)
    /// Each token compares against sample_size random tokens
    template<typename TokenRefFn>
    [[nodiscard]] static std::size_t collect_embeddings_sampled(
        const float* data,
        std::size_t vocab_size,
        std::size_t hidden_dim,
        TokenRefFn get_token_ref,
        double similarity_threshold,
        std::size_t max_neighbors_per_token,
        std::size_t sample_size,
        std::vector<WeightTuple>& output)
    {
        auto start = std::chrono::high_resolution_clock::now();
        
        // Precompute L2 norms
        std::vector<float> norms(vocab_size);
        for (std::size_t i = 0; i < vocab_size; ++i) {
            const float* embed = data + i * hidden_dim;
            float sum = 0.0f;
            for (std::size_t d = 0; d < hidden_dim; ++d) {
                sum += embed[d] * embed[d];
            }
            norms[i] = std::sqrt(sum);
        }

        // Parallel: each thread handles a chunk of source tokens
        std::size_t num_threads = Threading::default_thread_count();
        std::size_t chunk_size = (vocab_size + num_threads - 1) / num_threads;
        std::vector<std::vector<WeightTuple>> thread_weights(num_threads);

        Threading::parallel_for(num_threads, [&](std::size_t tid) {
            std::size_t start_idx = tid * chunk_size;
            std::size_t end_idx = std::min(start_idx + chunk_size, vocab_size);
            if (start_idx >= vocab_size) return;

            auto& local = thread_weights[tid];
            local.reserve((end_idx - start_idx) * max_neighbors_per_token * 2);

            // Thread-local RNG for sampling
            std::mt19937 rng(static_cast<unsigned>(tid * 12345 + 42));
            std::vector<std::size_t> sample_indices(vocab_size);
            for (std::size_t i = 0; i < vocab_size; ++i) sample_indices[i] = i;

            std::vector<std::pair<double, std::size_t>> similarities;
            similarities.reserve(sample_size);

            for (std::size_t i = start_idx; i < end_idx; ++i) {
                NodeRef from_ref = get_token_ref(i);
                if (from_ref.id_high == 0 && from_ref.id_low == 0) continue;
                if (norms[i] < 1e-8f) continue;

                const float* embed_i = data + i * hidden_dim;
                similarities.clear();

                // Shuffle and sample (Fisher-Yates partial shuffle)
                std::size_t effective_sample = std::min(sample_size, vocab_size - 1);
                for (std::size_t s = 0; s < effective_sample; ++s) {
                    std::size_t r = s + (rng() % (vocab_size - s));
                    std::swap(sample_indices[s], sample_indices[r]);
                }

                // Compare with sampled tokens
                for (std::size_t s = 0; s < effective_sample; ++s) {
                    std::size_t j = sample_indices[s];
                    if (j == i) continue;
                    if (norms[j] < 1e-8f) continue;

                    const float* embed_j = data + j * hidden_dim;

                    float dot = 0.0f;
                    for (std::size_t d = 0; d < hidden_dim; ++d) {
                        dot += embed_i[d] * embed_j[d];
                    }
                    double sim = static_cast<double>(dot) /
                                 (static_cast<double>(norms[i]) * static_cast<double>(norms[j]));

                    if (sim > similarity_threshold) {
                        similarities.emplace_back(sim, j);
                    }
                }

                // Sort by similarity descending, take top-K
                if (similarities.size() > max_neighbors_per_token) {
                    std::partial_sort(similarities.begin(),
                                     similarities.begin() + max_neighbors_per_token,
                                     similarities.end(),
                                     [](const auto& a, const auto& b) {
                                         return a.first > b.first;
                                     });
                    similarities.resize(max_neighbors_per_token);
                }

                // Store as bidirectional relationships
                for (const auto& [sim, j] : similarities) {
                    NodeRef to_ref = get_token_ref(j);
                    if (to_ref.id_high == 0 && to_ref.id_low == 0) continue;

                    local.emplace_back(from_ref, to_ref, sim);
                    local.emplace_back(to_ref, from_ref, sim);
                }
            }
        });

        // Merge thread results
        std::size_t stored = 0;
        for (auto& tw : thread_weights) {
            stored += tw.size();
            output.insert(output.end(),
                std::make_move_iterator(tw.begin()),
                std::make_move_iterator(tw.end()));
        }

        auto end = std::chrono::high_resolution_clock::now();
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();
        std::cerr << "collect_embeddings_sampled: " << vocab_size << " tokens, "
                  << sample_size << " samples each, " << stored << " relationships in " 
                  << ms << "ms\n";

        return stored;
    }

    /// Create deterministic NodeRef from index (for non-embedding tensor weights)
    [[nodiscard]] static NodeRef make_index_ref(std::size_t index) {
        std::uint8_t bytes[8];
        for (int i = 0; i < 8; ++i) {
            bytes[i] = static_cast<std::uint8_t>((index >> (i * 8)) & 0xFF);
        }

        NodeRef refs[2] = {
            NodeRef::comp(
                static_cast<std::int64_t>(bytes[0]) | (static_cast<std::int64_t>(bytes[1]) << 8),
                static_cast<std::int64_t>(bytes[2]) | (static_cast<std::int64_t>(bytes[3]) << 8)
            ),
            NodeRef::comp(
                static_cast<std::int64_t>(bytes[4]) | (static_cast<std::int64_t>(bytes[5]) << 8),
                static_cast<std::int64_t>(bytes[6]) | (static_cast<std::int64_t>(bytes[7]) << 8)
            )
        };

        auto [h, l] = MerkleHash::compute(refs, refs + 2);
        return NodeRef::comp(h, l);
    }

private:
    /// Sequential matrix collection for small matrices
    static std::size_t collect_matrix_sequential(
        const float* data,
        std::size_t out_dim,
        std::size_t in_dim,
        NodeRef tensor_ref,
        double threshold,
        std::vector<WeightTuple>& output)
    {
        std::size_t collected = 0;
        for (std::size_t out_idx = 0; out_idx < out_dim; ++out_idx) {
            NodeRef out_ref = make_index_ref(out_idx);
            for (std::size_t in_idx = 0; in_idx < in_dim; ++in_idx) {
                float val = data[out_idx * in_dim + in_idx];
                if (std::abs(val) < threshold) continue;

                NodeRef in_ref = make_index_ref(in_idx);
                NodeRef children[2] = {in_ref, out_ref};
                auto [h, l] = MerkleHash::compute(children, children + 2);
                NodeRef edge_ref = NodeRef::comp(h, l);

                output.emplace_back(tensor_ref, edge_ref, static_cast<double>(val));
                ++collected;
            }
        }
        return collected;
    }

    /// Parallel matrix collection for large matrices
    static std::size_t collect_matrix_parallel(
        const float* data,
        std::size_t out_dim,
        std::size_t in_dim,
        NodeRef tensor_ref,
        double threshold,
        std::vector<WeightTuple>& output)
    {
        std::size_t num_threads = Threading::default_thread_count();
        std::size_t rows_per_thread = (out_dim + num_threads - 1) / num_threads;

        std::vector<std::vector<WeightTuple>> thread_weights(num_threads);

        Threading::parallel_for(num_threads, [&](std::size_t tid) {
            std::size_t start_row = tid * rows_per_thread;
            std::size_t end_row = std::min(start_row + rows_per_thread, out_dim);

            std::vector<WeightTuple> local;
            local.reserve((end_row - start_row) * in_dim / 100);  // ~1% significant

            for (std::size_t out_idx = start_row; out_idx < end_row; ++out_idx) {
                NodeRef out_ref = make_index_ref(out_idx);
                for (std::size_t in_idx = 0; in_idx < in_dim; ++in_idx) {
                    float val = data[out_idx * in_dim + in_idx];
                    if (std::abs(val) < threshold) continue;

                    NodeRef in_ref = make_index_ref(in_idx);
                    NodeRef children[2] = {in_ref, out_ref};
                    auto [h, l] = MerkleHash::compute(children, children + 2);
                    NodeRef edge_ref = NodeRef::comp(h, l);

                    local.emplace_back(tensor_ref, edge_ref, static_cast<double>(val));
                }
            }

            thread_weights[tid] = std::move(local);
        });

        // Merge
        std::size_t collected = 0;
        for (auto& tw : thread_weights) {
            collected += tw.size();
            output.insert(output.end(),
                          std::make_move_iterator(tw.begin()),
                          std::make_move_iterator(tw.end()));
        }

        return collected;
    }

    /// Parallel sparse collection for large 1D tensors
    static std::size_t collect_sparse_parallel(
        const float* data,
        std::size_t count,
        NodeRef tensor_ref,
        double threshold,
        std::vector<WeightTuple>& output)
    {
        std::size_t num_threads = Threading::default_thread_count();
        std::size_t chunk_size = (count + num_threads - 1) / num_threads;

        std::vector<std::vector<WeightTuple>> thread_weights(num_threads);

        Threading::parallel_for(num_threads, [&](std::size_t tid) {
            std::size_t start = tid * chunk_size;
            std::size_t end = std::min(start + chunk_size, count);

            std::vector<WeightTuple> local;
            local.reserve((end - start) / 100);

            for (std::size_t i = start; i < end; ++i) {
                float val = data[i];
                if (std::abs(val) < threshold) continue;
                local.emplace_back(tensor_ref, make_index_ref(i), static_cast<double>(val));
            }

            thread_weights[tid] = std::move(local);
        });

        std::size_t collected = 0;
        for (auto& tw : thread_weights) {
            collected += tw.size();
            output.insert(output.end(),
                          std::make_move_iterator(tw.begin()),
                          std::make_move_iterator(tw.end()));
        }

        return collected;
    }
};

} // namespace hartonomous::model
