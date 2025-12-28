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

#include "../db/query_store.hpp"
#include "../atoms/node_ref.hpp"
#include "../threading/threading.hpp"
#include "safetensor.hpp"
#include <string>
#include <vector>
#include <fstream>
#include <filesystem>
#include <unordered_map>
#include <cmath>
#include <cstring>
#include <algorithm>
#include <mutex>
#include <atomic>
#include <iostream>

namespace hartonomous::model {

/// Token from vocabulary - the semantic anchor for embeddings
struct Token {
    std::uint32_t id;
    std::string text;
    NodeRef ref;  // Ingested semantic reference
};

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
    double sparsity_percent_;  // Keep top N% of weights by magnitude (e.g., 10.0 = top 10%)

    // Model context - identifies this model in the substrate
    NodeRef model_context_;

    // Ingested vocabulary for this model
    std::vector<Token> vocab_;

public:
    /// @param sparsity_percent Keep top N% of weights by magnitude (default: 50% = top 50%)
    explicit ModelIngester(db::QueryStore& store, double sparsity_percent = 50.0)
        : store_(store)
        , sparsity_percent_(std::clamp(sparsity_percent, 0.1, 100.0))
    {}

    /// Ingest entire model package (all files in directory)
    /// This is the CORRECT way - ingest ALL content, not just weights
    ModelResult ingest_package(const std::string& model_dir) {
        std::cerr << "ingest_package: start" << std::endl;
        auto start = std::chrono::high_resolution_clock::now();

        ModelResult result{};

        // Create model context from directory path (no flush - batch everything)
        std::cerr << "ingest_package: computing root..." << std::endl;
        model_context_ = store_.compute_root(model_dir);
        std::cerr << "ingest_package: build_and_collect..." << std::endl;
        store_.build_and_collect(
            reinterpret_cast<const std::uint8_t*>(model_dir.data()),
            model_dir.size());

        std::filesystem::path dir(model_dir);
        std::cerr << "ingest_package: Phase 1 - tokenizer..." << std::endl;

        // Phase 1: Ingest tokenizer vocabulary FIRST (semantic foundation)
        // Only ingest ONE tokenizer file - they contain the same vocab in different formats
        // Priority: vocab.txt (simple) > tokenizer.json (complex)
        std::string tokenizer_path;
        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;

            std::string filename = entry.path().filename().string();

            // Prefer vocab.txt (simpler format)
            if (filename == "vocab.txt") {
                tokenizer_path = entry.path().string();
                break;  // Found preferred format
            }
            // Fallback to tokenizer.json
            if (tokenizer_path.empty() && filename == "tokenizer.json") {
                tokenizer_path = entry.path().string();
            }
        }
        
        if (!tokenizer_path.empty()) {
            std::cerr << "ingest_package: ingesting tokenizer: " << std::filesystem::path(tokenizer_path).filename().string() << std::endl;
            result.vocab = ingest_tokenizer(tokenizer_path);
        }
        
        std::cerr << "ingest_package: Phase 2 - config files..." << std::endl;

        // Phase 2: Ingest all other content (configs, etc.)
        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;

            std::string ext = entry.path().extension().string();
            std::string filename = entry.path().filename().string();
            
            std::cerr << "Phase 2: checking file: " << filename << std::endl;

            // Skip already-processed tokenizer files
            if (filename == "tokenizer.json" || filename == "vocab.txt" ||
                filename == "vocab.json" || filename == "merges.txt") {
                continue;
            }

            // Text-based config files - full semantic ingestion
            if (ext == ".json" || ext == ".txt" || ext == ".md" ||
                ext == ".yaml" || ext == ".yml") {
                std::cerr << "Phase 2: ingesting text file: " << filename << std::endl;
                ingest_text_file(entry.path().string());
            }
        }
        
        std::cerr << "ingest_package: Phase 3 - safetensors..." << std::endl;

        // Collect ALL weights across ALL tensors, then bulk insert once
        std::vector<std::tuple<NodeRef, NodeRef, double>> all_weights;
        all_weights.reserve(15000000);  // ~11M weights expected

        // Phase 3: Collect safetensor weights WITH semantic anchors
        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;

            std::string ext = entry.path().extension().string();
            if (ext == ".safetensors") {
                std::cerr << "Phase 3: collecting safetensor: " << entry.path().filename().string() << std::endl;
                auto [tensors, total, stored] = ingest_safetensor_semantic(entry.path().string(), all_weights);
                std::cerr << "Phase 3: done - tensors=" << tensors << " total=" << total << " stored=" << stored << std::endl;
                result.tensor_count += tensors;
                result.total_weights += total;
                result.stored_weights += stored;
            }
        }

        // Single bulk insert for all weights
        std::cerr << "ingest_package: bulk storing " << all_weights.size() << " weights..." << std::endl;
        if (!all_weights.empty()) {
            store_.store_model_weights(all_weights, model_context_);
        }

        // SINGLE flush for ALL compositions (batched from entire package)
        std::cerr << "ingest_package: flushing " << store_.pending_compositions_.size() << " compositions..." << std::endl;
        store_.flush_pending();

        std::cerr << "ingest_package: Phase 3 complete" << std::endl;

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
    [[nodiscard]] const std::vector<Token>& vocabulary() const { return vocab_; }

private:
    /// Ingest tokenizer - this is THE semantic foundation
    /// BATCHED: collect all compositions, flush once at the end
    VocabResult ingest_tokenizer(const std::string& path) {
        auto start = std::chrono::high_resolution_clock::now();

        VocabResult result{};

        std::ifstream file(path);
        if (!file) return result;

        std::string content((std::istreambuf_iterator<char>(file)),
                             std::istreambuf_iterator<char>());

        // Parse vocabulary based on format
        std::string ext = std::filesystem::path(path).extension().string();

        if (ext == ".txt") {
            parse_vocab_txt(content);
        } else if (ext == ".json") {
            parse_vocab_json(content);
        }

        // BATCH: Compute all token refs WITHOUT flushing
        for (auto& token : vocab_) {
            if (!token.text.empty()) {
                // Just compute root - adds to pending but doesn't flush
                token.ref = store_.compute_root(token.text);
                // Collect compositions without flushing
                store_.build_and_collect(
                    reinterpret_cast<const std::uint8_t*>(token.text.data()),
                    token.text.size());
                result.ingested_count++;
            }
        }

        // SINGLE flush for all tokens
        store_.flush_pending();

        result.token_count = vocab_.size();

        auto end = std::chrono::high_resolution_clock::now();
        result.duration = std::chrono::duration_cast<std::chrono::milliseconds>(end - start);

        return result;
    }

    /// Parse simple vocab.txt format
    void parse_vocab_txt(const std::string& content) {
        std::uint32_t id = 0;
        std::size_t pos = 0;

        while (pos < content.size()) {
            std::size_t end = content.find('\n', pos);
            if (end == std::string::npos) end = content.size();

            std::string line = content.substr(pos, end - pos);

            // Remove carriage return if present
            if (!line.empty() && line.back() == '\r') {
                line.pop_back();
            }

            if (!line.empty()) {
                vocab_.push_back({id++, line, NodeRef{}});
            }

            pos = end + 1;
        }
    }

    /// Parse JSON vocabulary (simplified parser for tokenizer.json/vocab.json)
    void parse_vocab_json(const std::string& content) {
        // Look for "vocab" or "model" -> "vocab" section
        std::size_t vocab_pos = content.find("\"vocab\"");
        if (vocab_pos == std::string::npos) {
            // Try direct key-value pairs at root
            vocab_pos = content.find('{');
        } else {
            vocab_pos = content.find('{', vocab_pos);
        }

        if (vocab_pos == std::string::npos) return;

        // Simple JSON key-value extraction: "token": id
        std::size_t pos = vocab_pos;
        while (pos < content.size()) {
            // Find next string key
            std::size_t key_start = content.find('"', pos);
            if (key_start == std::string::npos) break;
            key_start++;

            std::size_t key_end = content.find('"', key_start);
            if (key_end == std::string::npos) break;

            std::string key = content.substr(key_start, key_end - key_start);

            // Skip non-vocabulary keys
            if (key == "vocab" || key == "model" || key == "version" ||
                key == "truncation" || key == "padding" || key == "added_tokens" ||
                key == "normalizer" || key == "pre_tokenizer" || key == "post_processor" ||
                key == "decoder" || key == "unk_token" || key == "bos_token" ||
                key == "eos_token" || key == "pad_token") {
                pos = key_end + 1;
                continue;
            }

            // Find colon and value
            std::size_t colon = content.find(':', key_end);
            if (colon == std::string::npos) break;

            // Skip whitespace
            std::size_t val_start = colon + 1;
            while (val_start < content.size() &&
                   (content[val_start] == ' ' || content[val_start] == '\t' ||
                    content[val_start] == '\n' || content[val_start] == '\r')) {
                val_start++;
            }

            // Check if value is a number (token ID)
            if (val_start < content.size() &&
                (content[val_start] >= '0' && content[val_start] <= '9')) {
                std::size_t val_end = val_start;
                while (val_end < content.size() &&
                       content[val_end] >= '0' && content[val_end] <= '9') {
                    val_end++;
                }

                std::uint32_t id = static_cast<std::uint32_t>(
                    std::stoul(content.substr(val_start, val_end - val_start)));

                // Unescape token text
                std::string token_text = unescape_json_string(key);

                // Ensure vocab is large enough
                if (id >= vocab_.size()) {
                    vocab_.resize(id + 1);
                }
                vocab_[id] = {id, token_text, NodeRef{}};

                pos = val_end;
            } else {
                pos = val_start + 1;
            }
        }

        // Remove empty slots
        vocab_.erase(
            std::remove_if(vocab_.begin(), vocab_.end(),
                [](const Token& t) { return t.text.empty(); }),
            vocab_.end());
    }

    /// Unescape JSON string
    static std::string unescape_json_string(const std::string& s) {
        std::string result;
        result.reserve(s.size());

        for (std::size_t i = 0; i < s.size(); ++i) {
            if (s[i] == '\\' && i + 1 < s.size()) {
                switch (s[i + 1]) {
                    case 'n': result += '\n'; ++i; break;
                    case 'r': result += '\r'; ++i; break;
                    case 't': result += '\t'; ++i; break;
                    case '"': result += '"'; ++i; break;
                    case '\\': result += '\\'; ++i; break;
                    case 'u':
                        if (i + 5 < s.size()) {
                            // Unicode escape: \uXXXX
                            std::string hex = s.substr(i + 2, 4);
                            try {
                                int cp = std::stoi(hex, nullptr, 16);
                                if (cp < 0x80) {
                                    result += static_cast<char>(cp);
                                } else if (cp < 0x800) {
                                    result += static_cast<char>(0xC0 | (cp >> 6));
                                    result += static_cast<char>(0x80 | (cp & 0x3F));
                                } else {
                                    result += static_cast<char>(0xE0 | (cp >> 12));
                                    result += static_cast<char>(0x80 | ((cp >> 6) & 0x3F));
                                    result += static_cast<char>(0x80 | (cp & 0x3F));
                                }
                                i += 5;
                            } catch (...) {
                                result += s[i];
                            }
                        } else {
                            result += s[i];
                        }
                        break;
                    default:
                        result += s[i];
                        break;
                }
            } else {
                result += s[i];
            }
        }

        return result;
    }

    /// Ingest text file as semantic content (no flush - batch with everything else)
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

public:
    /// Ingest safetensor with semantic anchors to vocabulary
    /// Returns: (tensor_count, total_weights, stored_weights)
    /// Collects weights into provided vector for batch storage
    std::tuple<std::size_t, std::size_t, std::size_t>
    ingest_safetensor_semantic(const std::string& path,
                                std::vector<std::tuple<NodeRef, NodeRef, double>>& all_weights) {
        std::cerr << "ingest_safetensor_semantic: opening " << path << std::endl;
        SafetensorReader reader(path);
        std::cerr << "ingest_safetensor_semantic: reader created" << std::endl;

        std::size_t tensor_count = 0;
        std::size_t total_weights = 0;
        std::size_t stored_weights = 0;

        // Collect all F32 tensors first
        std::vector<TensorMeta> f32_tensors;
        for (const auto& meta : reader.tensors()) {
            if (meta.dtype == TensorDType::F32) {
                f32_tensors.push_back(meta);
            }
        }
        tensor_count = f32_tensors.size();

        // PARALLEL: Process tensors in parallel, each with thread-local weight collection
        std::vector<std::vector<std::tuple<NodeRef, NodeRef, double>>> thread_weights(f32_tensors.size());
        std::vector<std::size_t> thread_totals(f32_tensors.size());
        std::vector<std::size_t> thread_stored(f32_tensors.size());

        // Find embedding tensor index - SKIP IT ENTIRELY
        // Embeddings are worthless - just coordinates in the model's arbitrary space
        // We have our OWN deterministic 4D semantic space from Unicode decomposition
        // The vocabulary ingestion (tokens → CPE → NodeRefs) is the ONLY value we extract
        std::size_t embedding_idx = f32_tensors.size(); // Invalid index = skip
        for (std::size_t i = 0; i < f32_tensors.size(); ++i) {
            const auto& meta = f32_tensors[i];
            bool is_word_embedding = 
                (meta.name.find("word_embed") != std::string::npos) ||
                (meta.name.find("wte") != std::string::npos) ||
                (meta.name == "embeddings.weight");
            if (is_word_embedding) {
                embedding_idx = i; // Mark to SKIP
                std::cerr << "SKIPPING embedding tensor: " << meta.name 
                          << " (worthless coordinates in model's arbitrary space)\n";
                break;
            }
        }

        // Process non-embedding tensors only
        // Weight matrices are 2D: [out_features × in_features]
        // Each weight at [i,j] = relationship from input_j to output_i
        // Store as trajectory LineStringZM from input position to output position

        // PARALLEL: Process all other tensors
        Threading::parallel_for(f32_tensors.size(), [&](std::size_t i) {
            if (i == embedding_idx) return; // Skip embeddings

            const auto& meta = f32_tensors[i];
            NodeRef tensor_ref = store_.compute_root(meta.name);
            thread_totals[i] = SafetensorReader::element_count(meta);
            
            // 2D weight matrices: store as input→output relationships
            if (meta.shape.size() == 2) {
                thread_stored[i] = collect_weight_matrix(reader, meta, tensor_ref, thread_weights[i]);
            } else {
                // 1D or other: store raw sparse weights  
                thread_stored[i] = collect_sparse_weights(reader, meta, tensor_ref, thread_weights[i]);
            }
        });

        // Merge results
        for (std::size_t i = 0; i < f32_tensors.size(); ++i) {
            total_weights += thread_totals[i];
            stored_weights += thread_stored[i];
            all_weights.insert(all_weights.end(),
                std::make_move_iterator(thread_weights[i].begin()),
                std::make_move_iterator(thread_weights[i].end()));
        }

        return {tensor_count, total_weights, stored_weights};
    }

    /// Extract token-to-token relationships from embedding matrix via cosine similarity.
    ///
    /// THE CORRECT APPROACH (per VISION.md):
    /// Embeddings are TEMPORARY - they exist only to compute which tokens relate.
    /// We compute cosine similarity between token pairs, then DISCARD the embeddings.
    /// The embedding dimensions disappear - they served their purpose.
    ///
    /// For each token pair where similarity > threshold:
    ///   - Emit relationship (our NodeRefs, not model indices)
    ///
    /// This is O(n² × d) but we can optimize with:
    ///   - Only compare tokens with high-magnitude embeddings
    ///   - Use approximate nearest neighbors (future)
    ///   - Sample pairs rather than exhaustive comparison
    std::size_t collect_embeddings(const SafetensorReader& reader,
                                   const TensorMeta& meta,
                                   [[maybe_unused]] NodeRef tensor_ref,
                                   std::vector<std::tuple<NodeRef, NodeRef, double>>& all_weights) {
        if (meta.shape.size() < 2) return 0;

        std::size_t vocab_size = meta.shape[0];
        std::size_t hidden_dim = meta.shape[1];
        const float* data = reader.get_f32_data(meta);

        std::size_t effective_size = std::min(vocab_size, vocab_.size());

        std::cerr << "collect_embeddings: computing token-to-token similarity for " 
                  << effective_size << " tokens\n";

        // Precompute L2 norms for cosine similarity
        std::vector<float> norms(effective_size);
        for (std::size_t i = 0; i < effective_size; ++i) {
            const float* embed = data + i * hidden_dim;
            float sum = 0.0f;
            for (std::size_t d = 0; d < hidden_dim; ++d) {
                sum += embed[d] * embed[d];
            }
            norms[i] = std::sqrt(sum);
        }

        // Similarity threshold - only store relationships above this
        constexpr double similarity_threshold = 0.5;  // cosine similarity > 0.5
        
        // For large vocabularies, we sample to avoid O(n²) explosion
        // Top-K per token: find the K most similar tokens for each token
        constexpr std::size_t max_neighbors_per_token = 50;

        std::cerr << "collect_embeddings: similarity threshold = " << similarity_threshold 
                  << ", max neighbors = " << max_neighbors_per_token << "\n";

        // Parallel: each thread handles a chunk of source tokens
        std::size_t num_threads = Threading::default_thread_count();
        std::size_t chunk_size = (effective_size + num_threads - 1) / num_threads;
        std::vector<std::vector<std::tuple<NodeRef, NodeRef, double>>> thread_weights(num_threads);

        std::atomic<std::size_t> pairs_compared{0};
        std::atomic<std::size_t> pairs_stored{0};

        Threading::parallel_for(num_threads, [&](std::size_t tid) {
            std::size_t start = tid * chunk_size;
            std::size_t end = std::min(start + chunk_size, effective_size);
            if (start >= effective_size) return;

            auto& local = thread_weights[tid];
            local.reserve((end - start) * max_neighbors_per_token);

            // For top-K selection per token
            std::vector<std::pair<double, std::size_t>> similarities;
            similarities.reserve(effective_size);

            for (std::size_t i = start; i < end; ++i) {
                NodeRef from_ref = vocab_[i].ref;
                if (from_ref.id_high == 0 && from_ref.id_low == 0) continue;
                if (norms[i] < 1e-8f) continue;  // Skip zero vectors

                const float* embed_i = data + i * hidden_dim;

                similarities.clear();

                // Compare with all other tokens (j > i to avoid duplicates)
                for (std::size_t j = i + 1; j < effective_size; ++j) {
                    if (norms[j] < 1e-8f) continue;

                    const float* embed_j = data + j * hidden_dim;

                    // Cosine similarity = dot(a,b) / (|a| × |b|)
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
                    NodeRef to_ref = vocab_[j].ref;
                    if (to_ref.id_high == 0 && to_ref.id_low == 0) continue;

                    // Bidirectional: A→B and B→A with same weight
                    local.emplace_back(from_ref, to_ref, sim);
                    local.emplace_back(to_ref, from_ref, sim);
                    pairs_stored += 2;
                }
            }
        });

        // Merge thread results
        std::size_t stored = 0;
        for (auto& tw : thread_weights) {
            stored += tw.size();
            all_weights.insert(all_weights.end(),
                std::make_move_iterator(tw.begin()),
                std::make_move_iterator(tw.end()));
        }

        std::cerr << "collect_embeddings: compared " << pairs_compared.load() 
                  << " pairs, stored " << stored << " relationships\n";

        return stored;
    }

    /// Compute dynamic sparsity threshold based on tensor statistics.
    /// Keep top sparsity_percent_% most significant weights (by magnitude).
    [[nodiscard]] double compute_dynamic_threshold(const float* data, std::size_t count) {
        if (count < 100) return 1e-9; // Keep everything for tiny tensors
        
        // Sample to find magnitude distribution (avoid scanning entire tensor)
        std::size_t sample_size = std::min(count, std::size_t(10000));
        std::size_t stride = count / sample_size;
        
        std::vector<float> magnitudes;
        magnitudes.reserve(sample_size);
        for (std::size_t i = 0; i < count; i += stride) {
            magnitudes.push_back(std::abs(data[i]));
        }
        
        // Percentile = (100 - sparsity_percent_) / 100
        // e.g., sparsity_percent_=10 means keep top 10%, so threshold is 90th percentile
        double percentile = (100.0 - sparsity_percent_) / 100.0;
        std::size_t threshold_idx = static_cast<std::size_t>(magnitudes.size() * percentile);
        threshold_idx = std::min(threshold_idx, magnitudes.size() - 1);
        
        std::nth_element(magnitudes.begin(), magnitudes.begin() + static_cast<std::ptrdiff_t>(threshold_idx), magnitudes.end());
        
        return static_cast<double>(magnitudes[threshold_idx]);
    }

    /// Collect 2D weight matrix as (input, output, weight) points.
    /// Matrix layout: matrix[out_idx][in_idx] = weight from input to output
    /// Each significant weight becomes a relationship point.
    /// Each row (fixed output) = trajectory of how inputs feed into that output.
    std::size_t collect_weight_matrix(const SafetensorReader& reader,
                                      const TensorMeta& meta,
                                      NodeRef tensor_ref,
                                      std::vector<std::tuple<NodeRef, NodeRef, double>>& all_weights) {
        if (meta.shape.size() != 2) return 0;
        
        std::size_t out_dim = meta.shape[0];  // rows = output features
        std::size_t in_dim = meta.shape[1];   // cols = input features
        std::size_t count = out_dim * in_dim;
        const float* data = reader.get_f32_data(meta);
        
        // Dynamic threshold for this matrix
        double threshold = compute_dynamic_threshold(data, count);
        
        // Small matrices: sequential
        if (count < 10000) {
            std::size_t collected = 0;
            for (std::size_t out_idx = 0; out_idx < out_dim; ++out_idx) {
                NodeRef out_ref = make_index_ref(out_idx);
                for (std::size_t in_idx = 0; in_idx < in_dim; ++in_idx) {
                    float val = data[out_idx * in_dim + in_idx];
                    if (std::abs(val) < threshold) continue;
                    NodeRef in_ref = make_index_ref(in_idx);
                    // Store as (tensor, composition(in→out), weight)
                    NodeRef children[2] = {in_ref, out_ref};
                    auto [h, l] = MerkleHash::compute(children, children + 2);
                    NodeRef edge_ref = NodeRef::comp(h, l);
                    all_weights.emplace_back(tensor_ref, edge_ref, static_cast<double>(val));
                    ++collected;
                }
            }
            return collected;
        }
        
        // Large matrices: parallel by output row (each row = one output's input relationships)
        std::size_t num_threads = Threading::default_thread_count();
        std::size_t rows_per_thread = (out_dim + num_threads - 1) / num_threads;
        
        std::vector<std::vector<std::tuple<NodeRef, NodeRef, double>>> thread_weights(num_threads);
        
        Threading::parallel_for(num_threads, [&](std::size_t tid) {
            std::size_t start_row = tid * rows_per_thread;
            std::size_t end_row = std::min(start_row + rows_per_thread, out_dim);
            
            std::vector<std::tuple<NodeRef, NodeRef, double>> local;
            local.reserve((end_row - start_row) * in_dim / 100); // ~1% significant
            
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
            all_weights.insert(all_weights.end(),
                              std::make_move_iterator(tw.begin()),
                              std::make_move_iterator(tw.end()));
        }
        
        return collected;
    }

    std::size_t collect_sparse_weights(const SafetensorReader& reader,
                                       const TensorMeta& meta,
                                       NodeRef tensor_ref,
                                       std::vector<std::tuple<NodeRef, NodeRef, double>>& all_weights) {
        std::size_t count = SafetensorReader::element_count(meta);
        const float* data = reader.get_f32_data(meta);
        
        // Dynamic threshold: keep only significant weights
        double threshold = compute_dynamic_threshold(data, count);
        
        // Small tensors: sequential fast path
        if (count < 10000) {
            std::size_t collected = 0;
            for (std::size_t i = 0; i < count; ++i) {
                float val = data[i];
                if (std::abs(val) < threshold) continue;
                all_weights.emplace_back(tensor_ref, make_index_ref(i), static_cast<double>(val));
                ++collected;
            }
            return collected;
        }

        // Large tensors: chunk-based parallel with no contention
        std::size_t num_threads = Threading::default_thread_count();
        std::size_t chunk_size = (count + num_threads - 1) / num_threads;
        
        std::vector<std::vector<std::tuple<NodeRef, NodeRef, double>>> thread_weights(num_threads);
        
        Threading::parallel_for(num_threads, [&](std::size_t tid) {
            std::size_t start = tid * chunk_size;
            std::size_t end = std::min(start + chunk_size, count);
            
            std::vector<std::tuple<NodeRef, NodeRef, double>> local;
            local.reserve((end - start) / 100); // ~1% after dynamic threshold
            
            for (std::size_t i = start; i < end; ++i) {
                float val = data[i];
                if (std::abs(val) < threshold) continue;
                local.emplace_back(tensor_ref, make_index_ref(i), static_cast<double>(val));
            }
            
            thread_weights[tid] = std::move(local);
        });

        // Merge thread-local results into all_weights
        std::size_t collected = 0;
        for (auto& tw : thread_weights) {
            collected += tw.size();
            all_weights.insert(all_weights.end(),
                              std::make_move_iterator(tw.begin()),
                              std::make_move_iterator(tw.end()));
        }

        return collected;
    }

    /// Create deterministic NodeRef from index (for non-embedding tensor weights)
    [[nodiscard]] NodeRef make_index_ref(std::size_t index) {
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
};

} // namespace hartonomous::model
