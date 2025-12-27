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
    double sparsity_threshold_;

    // Model context - identifies this model in the substrate
    NodeRef model_context_;

    // Ingested vocabulary for this model
    std::vector<Token> vocab_;

public:
    explicit ModelIngester(db::QueryStore& store, double sparsity_threshold = 1e-6)
        : store_(store)
        , sparsity_threshold_(sparsity_threshold)
    {}

    /// Ingest entire model package (all files in directory)
    /// This is the CORRECT way - ingest ALL content, not just weights
    ModelResult ingest_package(const std::string& model_dir) {
        std::cerr << "ingest_package: start" << std::endl;
        auto start = std::chrono::high_resolution_clock::now();

        ModelResult result{};

        // Create model context from directory path
        std::cerr << "ingest_package: computing root..." << std::endl;
        model_context_ = store_.compute_root(model_dir);
        std::cerr << "ingest_package: encode_and_store..." << std::endl;
        store_.encode_and_store(model_dir);

        std::filesystem::path dir(model_dir);
        std::cerr << "ingest_package: Phase 1 - tokenizer..." << std::endl;

        // Phase 1: Ingest tokenizer vocabulary FIRST (semantic foundation)
        for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;

            std::string filename = entry.path().filename().string();

            // Tokenizer files - these define the semantic mapping
            if (filename == "tokenizer.json" || filename == "vocab.txt" ||
                filename == "vocab.json" || filename == "merges.txt") {
                std::cerr << "ingest_package: ingesting tokenizer: " << filename << std::endl;
                result.vocab = ingest_tokenizer(entry.path().string());
            }
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
            store_.store_model_weights(all_weights, model_context_, db::RelType::MODEL_WEIGHT);
        }
        
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

    /// Ingest text file as semantic content
    void ingest_text_file(const std::string& path) {
        std::ifstream file(path);
        if (!file) return;

        std::string content((std::istreambuf_iterator<char>(file)),
                             std::istreambuf_iterator<char>());

        if (!content.empty()) {
            store_.encode_and_store(content);
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

        for (const auto& meta : reader.tensors()) {
            std::cerr << "ingest_safetensor_semantic: tensor " << meta.name << " dtype=" << static_cast<int>(meta.dtype) << std::endl;
            if (meta.dtype != TensorDType::F32) continue;

            tensor_count++;

            // Ingest tensor name as content
            std::cerr << "ingest_safetensor_semantic: encoding tensor name" << std::endl;
            NodeRef tensor_ref = store_.encode_and_store(meta.name);

            std::size_t count = SafetensorReader::element_count(meta);
            std::cerr << "ingest_safetensor_semantic: " << count << " elements" << std::endl;
            total_weights += count;

            // Check if this is a WORD embedding layer (connects tokens to vectors)
            // Must be specifically word_embeddings, not position_embeddings or token_type_embeddings
            bool is_word_embedding = 
                (meta.name.find("word_embed") != std::string::npos) ||
                (meta.name.find("wte") != std::string::npos) ||
                (meta.name == "embeddings.weight");  // Simple embedding layer
            
            // Also check shape matches vocab: [vocab_size, hidden_dim] where vocab_size > 1000
            bool has_vocab_shape = meta.shape.size() >= 2 && 
                                   meta.shape[0] > 1000 &&  // Vocab is typically >30k
                                   meta.shape[0] <= vocab_.size() * 2;  // Sanity check
            
            std::cerr << "ingest_safetensor_semantic: is_word_embedding=" << is_word_embedding 
                      << " has_vocab_shape=" << has_vocab_shape 
                      << " shape[0]=" << (meta.shape.size() > 0 ? meta.shape[0] : 0) << std::endl;

            if (is_word_embedding && has_vocab_shape && !vocab_.empty()) {
                // Embedding matrix: [vocab_size, hidden_dim]
                // Create relationships from tokens to embedding positions
                std::cerr << "ingest_safetensor_semantic: calling ingest_embeddings" << std::endl;
                stored_weights += collect_embeddings(reader, meta, tensor_ref, all_weights);
                std::cerr << "ingest_safetensor_semantic: ingest_embeddings done" << std::endl;
            } else {
                // Other weights: sparse storage with tensor context
                std::cerr << "ingest_safetensor_semantic: calling ingest_sparse_weights" << std::endl;
                stored_weights += collect_sparse_weights(reader, meta, tensor_ref, all_weights);
                std::cerr << "ingest_safetensor_semantic: ingest_sparse_weights done" << std::endl;
            }
        }

        return {tensor_count, total_weights, stored_weights};
    }

    /// Ingest embedding matrix - store each embedding as a 384-point trajectory.
    ///
    /// THE VISION: Embeddings ARE positions in high-dimensional space.
    /// Store them as LineStringZM trajectories: point[d] = (d, embed[d], d/64, d%64)
    /// Query similarity with ST_FrechetDistance via GiST index - O(log n) not O(n²).
    ///
    /// NO pairwise computation at ingestion. Store once, query with spatial ops.
    std::size_t collect_embeddings(const SafetensorReader& reader,
                                   const TensorMeta& meta,
                                   [[maybe_unused]] NodeRef tensor_ref,
                                   [[maybe_unused]] std::vector<std::tuple<NodeRef, NodeRef, double>>& all_weights) {
        if (meta.shape.size() < 2) return 0;

        std::size_t vocab_size = meta.shape[0];
        std::size_t hidden_dim = meta.shape[1];
        const float* data = reader.get_f32_data(meta);

        std::size_t effective_size = std::min(vocab_size, vocab_.size());
        
        // Build vector of token refs
        std::vector<NodeRef> token_refs;
        token_refs.reserve(effective_size);
        for (std::size_t i = 0; i < effective_size; ++i) {
            token_refs.push_back(vocab_[i].ref);
        }

        std::cerr << "collect_embeddings: storing " << effective_size 
                  << " embeddings as " << hidden_dim << "-point trajectories\n";

        // Bulk store all embeddings as trajectories via COPY protocol
        store_.store_embedding_trajectories(
            data, 
            vocab_size, 
            hidden_dim, 
            token_refs, 
            model_context_,
            db::RelType::EMBEDDING_TRAJECTORY);

        std::cerr << "collect_embeddings: trajectory storage complete\n";

        // Return count of stored embeddings (not pairs - we don't compute pairs)
        return effective_size;
    }

    /// Create NodeRef from semantic position coordinates
    [[nodiscard]] NodeRef make_semantic_position_ref(double x, double y, double z, double m) {
        // Quantize coordinates to integers for deterministic hashing
        std::int32_t ix = static_cast<std::int32_t>(x * 10000);
        std::int32_t iy = static_cast<std::int32_t>(y * 10000);
        std::int32_t iz = static_cast<std::int32_t>(z * 10000);
        std::int32_t im = static_cast<std::int32_t>(m * 10000);

        NodeRef refs[2] = {
            NodeRef::comp(
                (static_cast<std::int64_t>(ix) << 32) | static_cast<std::uint32_t>(iy),
                (static_cast<std::int64_t>(iz) << 32) | static_cast<std::uint32_t>(im)
            ),
            NodeRef::comp(ix ^ iy, iz ^ im)
        };

        auto [h, l] = MerkleHash::compute(refs, refs + 2);
        return NodeRef::comp(h, l);
    }

    /// Collect non-embedding weights with sparse filtering (parallelized)
    std::size_t collect_sparse_weights(const SafetensorReader& reader,
                                       const TensorMeta& meta,
                                       NodeRef tensor_ref,
                                       std::vector<std::tuple<NodeRef, NodeRef, double>>& all_weights) {
        std::size_t count = SafetensorReader::element_count(meta);
        const float* data = reader.get_f32_data(meta);
        
        // Small tensors: sequential fast path
        if (count < 10000) {
            std::size_t collected = 0;
            for (std::size_t i = 0; i < count; ++i) {
                float val = data[i];
                if (std::abs(val) < sparsity_threshold_) continue;
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
            local.reserve((end - start) / 10);
            
            for (std::size_t i = start; i < end; ++i) {
                float val = data[i];
                if (std::abs(val) < sparsity_threshold_) continue;
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

    /// Create deterministic NodeRef for dimension
    [[nodiscard]] NodeRef make_dimension_ref(NodeRef tensor_ref, std::size_t dim) {
        // Combine tensor ref with dimension to create unique ref
        std::uint8_t bytes[16];
        std::memcpy(bytes, &tensor_ref.id_high, 8);
        std::memcpy(bytes + 8, &dim, 8);

        NodeRef refs[2] = {
            NodeRef::comp(
                *reinterpret_cast<std::int64_t*>(bytes),
                *reinterpret_cast<std::int64_t*>(bytes + 8)
            ),
            tensor_ref
        };

        auto [h, l] = MerkleHash::compute(refs, refs + 2);
        return NodeRef::comp(h, l);
    }

    /// Create deterministic NodeRef from index
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
