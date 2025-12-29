#pragma once

/// MODEL INGESTION - The CORRECT way to ingest AI models
///
/// The key insight: weights are MEANINGLESS without semantics.
/// A model is not a bag of floats - it's a mapping from TOKENS to MEANING.
///
/// Ingestion flow:
/// 1. Tokenizer vocabulary → TEXT ingestion (every token is semantic content)
/// 2. Embeddings → relationships FROM ingested tokens TO semantic positions
/// 3. Attention/FFN weights → relationships between token positions
///
/// This creates a BRIDGE between the model's internal space and our universal substrate.
/// Queries become: "ingest prompt → walk semantic graph → find model paths → generate"
///
/// ARCHITECTURE (Single Responsibility):
/// - vocab_parser.hpp     -> Token extraction from txt/json formats
/// - weight_collector.hpp -> Sparse weight extraction, embedding similarity
/// - model_ingest.hpp     -> Orchestration and file iteration

#include "../db/query_store.hpp"
#include "../db/cpe_encoder.hpp"
#include "../atoms/node_ref.hpp"
#include "../atoms/codepoint_atom_table.hpp"
#include "vocab_parser.hpp"
#include "weight_collector.hpp"
#include "safetensor.hpp"
#include <string>
#include <vector>
#include <fstream>
#include <filesystem>
#include <algorithm>
#include <iostream>
#include <chrono>

namespace hartonomous::model {

/// Ingested token with semantic reference
struct IngestedToken {
    std::uint32_t id;
    std::string text;
    NodeRef ref;  // Semantic reference from CPE ingestion
};

// Backwards compatibility alias
using Token = IngestedToken;

/// Vocabulary ingestion result
struct VocabResult {
    std::size_t token_count;
    std::size_t ingested_count;
    std::chrono::milliseconds duration;
};

/// Model ingestion result
struct ModelResult {
    VocabResult vocab;
    std::size_t tensor_count;
    std::size_t total_weights;
    std::size_t stored_weights;  // After sparse filtering
    std::chrono::milliseconds total_duration;
    double sparsity_ratio;
};

/// Model ingester - ingests EVERYTHING semantically
class ModelIngester {
    db::QueryStore& store_;
    double sparsity_percent_;  // Keep top N% of weights by magnitude

    // Model context - identifies this model in the substrate
    NodeRef model_context_;

    // Ingested vocabulary for this model
    std::vector<IngestedToken> vocab_;

public:
    /// @param sparsity_percent Keep top N% of weights by magnitude (default: 50%)
    explicit ModelIngester(db::QueryStore& store, double sparsity_percent = 50.0)
        : store_(store)
        , sparsity_percent_(std::clamp(sparsity_percent, 0.1, 100.0))
    {}

    /// Ingest entire model package (all files in directory)
    ModelResult ingest_package(const std::string& model_dir) {
        std::cerr << "ingest_package: start" << std::endl;
        auto start = std::chrono::high_resolution_clock::now();

        ModelResult result{};

        // Create model context from directory path
        std::cerr << "ingest_package: computing root..." << std::endl;
        model_context_ = store_.compute_root(model_dir);
        store_.build_and_collect(
            reinterpret_cast<const std::uint8_t*>(model_dir.data()),
            model_dir.size());

        std::filesystem::path dir(model_dir);

        // Phase 1: Ingest tokenizer vocabulary FIRST (semantic foundation)
        std::cerr << "ingest_package: Phase 1 - tokenizer..." << std::endl;
        result.vocab = ingest_tokenizer_phase(dir);

        // Phase 2: Ingest config files
        std::cerr << "ingest_package: Phase 2 - config files..." << std::endl;
        ingest_config_files(dir);

        // Phase 3: Collect safetensor weights
        std::cerr << "ingest_package: Phase 3 - safetensors..." << std::endl;
        std::vector<WeightCollector::WeightTuple> all_weights;
        all_weights.reserve(15000000);
        
        auto [tensors, total, stored] = ingest_safetensors_phase(dir, all_weights);
        result.tensor_count = tensors;
        result.total_weights = total;
        result.stored_weights = stored;

        // Bulk store all weights
        std::cerr << "ingest_package: bulk storing " << all_weights.size() << " weights..." << std::endl;
        if (!all_weights.empty()) {
            store_.store_model_weights(all_weights, model_context_);
        }

        // Flush all compositions
        std::cerr << "ingest_package: flushing " << store_.pending_compositions_.size() << " compositions..." << std::endl;
        store_.flush_pending();

        auto end = std::chrono::high_resolution_clock::now();
        result.total_duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

        if (result.total_weights > 0) {
            result.sparsity_ratio = 1.0 - static_cast<double>(result.stored_weights) /
                                          static_cast<double>(result.total_weights);
        }

        return result;
    }

    /// Get model context for queries
    [[nodiscard]] NodeRef model_context() const { return model_context_; }

    /// Get ingested vocabulary
    [[nodiscard]] const std::vector<IngestedToken>& vocabulary() const { return vocab_; }

    /// Public API for ingesting a single safetensor file
    /// Returns: (tensor_count, total_weights, stored_weights)
    std::tuple<std::size_t, std::size_t, std::size_t>
    ingest_safetensor_semantic(
        const std::string& path,
        std::vector<WeightCollector::WeightTuple>& all_weights)
    {
        // Ensure model_context_ is set
        if (model_context_.id_high == 0 && model_context_.id_low == 0) {
            model_context_ = store_.compute_root(path);
        }
        return process_safetensor(path, all_weights);
    }

private:
    /// Phase 1: Find and ingest tokenizer vocabulary
    VocabResult ingest_tokenizer_phase(const std::filesystem::path& dir) {
        auto start = std::chrono::high_resolution_clock::now();
        VocabResult result{};

        // Find tokenizer file (prefer vocab.txt over tokenizer.json)
        std::string tokenizer_path = find_tokenizer_file(dir);
        if (tokenizer_path.empty()) {
            return result;
        }

        std::cerr << "ingest_package: ingesting tokenizer: "
                  << std::filesystem::path(tokenizer_path).filename().string() << std::endl;

        // Read file content
        std::ifstream file(tokenizer_path);
        if (!file) return result;

        std::string content((std::istreambuf_iterator<char>(file)),
                             std::istreambuf_iterator<char>());

        // Parse based on format
        std::string ext = std::filesystem::path(tokenizer_path).extension().string();
        ParsedVocab parsed = (ext == ".txt")
            ? VocabParser::parse_vocab_txt(content)
            : VocabParser::parse_vocab_json(content);

        // Ingest tokens using CPE - same algorithm as query encoding
        // Also stores compositions so they can be decoded later
        // IMPORTANT: Preserve original indices for embedding matrix lookup
        db::CpeEncoder cpe;
        std::vector<std::tuple<NodeRef, NodeRef, NodeRef>> all_compositions;
        all_compositions.reserve(parsed.size() * 10);
        
        vocab_.resize(parsed.size());  // Preserve indices!
        for (const auto& token : parsed.tokens) {
            if (token.text.empty()) {
                vocab_[token.id] = IngestedToken{token.id, "", NodeRef{}};
                continue;
            }

            IngestedToken ingested;
            ingested.id = token.id;
            ingested.text = token.text;
            
            // Use CPE to match query-time encoding
            auto codepoints = UTF8Decoder::decode(
                reinterpret_cast<const std::uint8_t*>(token.text.data()), 
                token.text.size());
            if (codepoints.size() == 1) {
                ingested.ref = CodepointAtomTable::ref(codepoints[0]);
            } else if (!codepoints.empty()) {
                // Build CPE and collect compositions for storage
                ingested.ref = cpe.build_cpe_and_collect(codepoints, 0, codepoints.size(), all_compositions);
            } else {
                vocab_[token.id] = IngestedToken{token.id, token.text, NodeRef{}};
                continue;
            }

            vocab_[token.id] = std::move(ingested);
            result.ingested_count++;
        }

        // Store all vocab compositions
        if (!all_compositions.empty()) {
            store_.bulk_store_compositions(all_compositions);
        }

        // Single flush for all tokens
        store_.flush_pending();

        result.token_count = vocab_.size();
        auto end = std::chrono::high_resolution_clock::now();
        result.duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

        return result;
    }

    /// Find tokenizer file (prioritize vocab.txt)
    [[nodiscard]] std::string find_tokenizer_file(const std::filesystem::path& dir) const {
        std::string result;
        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;

            std::string filename = entry.path().filename().string();
            if (filename == "vocab.txt") {
                return entry.path().string();  // Preferred format
            }
            if (result.empty() && filename == "tokenizer.json") {
                result = entry.path().string();  // Fallback
            }
        }
        return result;
    }

    /// Phase 2: Ingest config/text files
    void ingest_config_files(const std::filesystem::path& dir) {
        static const std::unordered_set<std::string> skip_files = {
            "tokenizer.json", "vocab.txt", "vocab.json", "merges.txt"
        };
        static const std::unordered_set<std::string> text_extensions = {
            ".json", ".txt", ".md", ".yaml", ".yml"
        };

        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;

            std::string filename = entry.path().filename().string();
            std::string ext = entry.path().extension().string();

            if (skip_files.count(filename) > 0) continue;
            if (text_extensions.count(ext) == 0) continue;

            std::cerr << "Phase 2: ingesting text file: " << filename << std::endl;
            ingest_text_file(entry.path().string());
        }
    }

    /// Ingest text file as semantic content
    void ingest_text_file(const std::string& path) {
        std::ifstream file(path);
        if (!file) return;

        std::string content((std::istreambuf_iterator<char>(file)),
                             std::istreambuf_iterator<char>());

        if (!content.empty()) {
            store_.build_and_collect(
                reinterpret_cast<const std::uint8_t*>(content.data()),
                content.size());
        }
    }

    /// Phase 3: Process safetensor files
    std::tuple<std::size_t, std::size_t, std::size_t>
    ingest_safetensors_phase(
        const std::filesystem::path& dir,
        std::vector<WeightCollector::WeightTuple>& all_weights)
    {
        std::size_t tensor_count = 0;
        std::size_t total_weights = 0;
        std::size_t stored_weights = 0;

        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;
            if (entry.path().extension() != ".safetensors") continue;

            std::cerr << "Phase 3: processing safetensor: "
                      << entry.path().filename().string() << std::endl;

            auto [tensors, total, stored] = process_safetensor(entry.path().string(), all_weights);

            std::cerr << "Phase 3: done - tensors=" << tensors
                      << " total=" << total << " stored=" << stored << std::endl;

            tensor_count += tensors;
            total_weights += total;
            stored_weights += stored;
        }

        return {tensor_count, total_weights, stored_weights};
    }

    /// Process single safetensor file
    std::tuple<std::size_t, std::size_t, std::size_t>
    process_safetensor(
        const std::string& path,
        std::vector<WeightCollector::WeightTuple>& all_weights)
    {
        SafetensorReader reader(path);

        std::size_t tensor_count = 0;
        std::size_t total_weights = 0;
        std::size_t stored_weights = 0;

        // Collect F32 tensors
        std::vector<TensorMeta> f32_tensors;
        for (const auto& meta : reader.tensors()) {
            if (meta.dtype == TensorDType::F32) {
                f32_tensors.push_back(meta);
            }
        }
        tensor_count = f32_tensors.size();

        // Find embedding tensor - we PROCESS it for token similarity, not skip
        std::size_t embedding_idx = find_embedding_tensor_index(f32_tensors);

        // Process each tensor (parallel via WeightCollector)
        for (std::size_t i = 0; i < f32_tensors.size(); ++i) {
            const auto& meta = f32_tensors[i];
            total_weights += SafetensorReader::element_count(meta);

            if (i == embedding_idx) {
                // PROCESS embedding to extract token-to-token similarity
                // The embeddings are TEMPORARY - we compute similarity then discard coordinates
                if (meta.shape.size() == 2 && !vocab_.empty()) {
                    std::size_t vocab_size = meta.shape[0];
                    std::size_t hidden_dim = meta.shape[1];
                    const float* data = reader.get_f32_data(meta);
                    
                    std::cerr << "Processing embedding tensor for token-to-token similarity: "
                              << vocab_size << "x" << hidden_dim << std::endl;
                    
                    // Build token ref lookup from vocab
                    auto get_token_ref = [this](std::size_t idx) -> NodeRef {
                        if (idx < vocab_.size()) {
                            return vocab_[idx].ref;
                        }
                        return NodeRef{};
                    };
                    
                    // Extract top-k similar tokens for each token
                    // similarity_threshold=0.5, max_neighbors=50
                    stored_weights += WeightCollector::collect_embeddings(
                        data, vocab_size, hidden_dim,
                        get_token_ref, 0.5, 50, all_weights);
                }
                continue;
            }

            NodeRef tensor_ref = store_.compute_root(meta.name);

            if (meta.shape.size() == 2) {
                stored_weights += WeightCollector::collect_weight_matrix(
                    reader, meta, tensor_ref, sparsity_percent_, all_weights);
            } else {
                stored_weights += WeightCollector::collect_sparse_weights(
                    reader, meta, tensor_ref, sparsity_percent_, all_weights);
            }
        }

        return {tensor_count, total_weights, stored_weights};
    }

    /// Find embedding tensor index (to process for token similarity)
    [[nodiscard]] std::size_t find_embedding_tensor_index(
        const std::vector<TensorMeta>& tensors) const
    {
        for (std::size_t i = 0; i < tensors.size(); ++i) {
            const auto& meta = tensors[i];
            bool is_word_embedding =
                (meta.name.find("word_embed") != std::string::npos) ||
                (meta.name.find("wte") != std::string::npos) ||
                (meta.name == "embeddings.weight");

            if (is_word_embedding) {
                std::cerr << "Found embedding tensor: " << meta.name
                          << " (will extract token-to-token similarity)\n";
                return i;
            }
        }
        return tensors.size();  // Invalid index = no embedding found
    }
};

} // namespace hartonomous::model
