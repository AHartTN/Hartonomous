/**
 * @file model_ingester.hpp
 * @brief AI model ingestion: Extract semantic edges from models
 *
 * Key insight:
 * - "King" in substrate = composition of atoms [K,i,n,g] = just the word
 * - "King" in AI model = entire CONCEPT with all learned relationships
 *
 * Model ingestion extracts the concept by mining semantic edges:
 * - Vocab tokens → Compositions (same pipeline as text)
 * - Embedding KNN → Relations (semantic neighbors)
 * - Attention weights → Relations (A attends to B with weight W)
 * - All edges become Relations with Evidence from the model
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <ingestion/safetensor_loader.hpp>
#include <nlohmann/json.hpp>
#include <string>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <filesystem>
#include <Eigen/Dense>
#include <cstring>

namespace Hartonomous {

struct ModelIngestionStats {
    size_t total_files = 0;
    size_t vocab_tokens = 0;
    size_t compositions_created = 0;
    size_t physicality_records = 0;
    size_t relations_created = 0;
    size_t tensors_processed = 0;
    size_t embedding_relations = 0;
    size_t atoms_created = 0;
};

struct ModelIngestionConfig {
    BLAKE3Pipeline::Hash tenant_id;
    BLAKE3Pipeline::Hash user_id;
    double embedding_similarity_threshold = 0.40;  // Similarity threshold for edge inclusion
    size_t max_neighbors_per_token = 64;           // Max neighbors to extract per token
    size_t db_batch_size = 100000;                 // Records per DB batch
};

class ModelIngester {
public:
    explicit ModelIngester(PostgresConnection& db, const ModelIngestionConfig& config = ModelIngestionConfig());

    ModelIngestionStats ingest_package(const std::filesystem::path& package_dir);

private:
    PostgresConnection& db_;
    ModelIngestionConfig config_;
    BLAKE3Pipeline::Hash model_id_;

    std::unordered_map<std::string, BLAKE3Pipeline::Hash> ingest_vocab_as_text(
        const std::vector<std::string>& vocab,
        ModelIngestionStats& stats
    );

    void extract_embedding_edges(
        const std::vector<std::string>& vocab,
        const Eigen::MatrixXf& embeddings,
        const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
        ModelIngestionStats& stats
    );

    void extract_attention_edges(
        const std::vector<Eigen::MatrixXd>& attention_weights,
        const std::vector<std::string>& tokens,
        const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
        ModelIngestionStats& stats
    );

    void extract_attention_layer_edges(
        const std::vector<AttentionLayer>& layers,
        const std::vector<std::string>& vocab,
        const Eigen::MatrixXf& embeddings,
        const Eigen::MatrixXf& norm_embeddings,
        const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
        ModelIngestionStats& stats
    );

    void extract_ffn_layer_edges(
        const std::vector<FFNLayer>& layers,
        const std::vector<std::string>& vocab,
        const Eigen::MatrixXf& embeddings,
        const Eigen::MatrixXf& norm_embeddings,
        const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
        std::unordered_set<BLAKE3Pipeline::Hash, HashHasher>& session_rel_seen,
        ModelIngestionStats& stats
    );

    void ingest_tensor(const std::string& name, const TensorData& tensor, ModelIngestionStats& stats);

    // Deprecated - physicality comes from content, not embeddings
    BLAKE3Pipeline::Hash create_physicality_from_unicode(const std::string& text);

    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);

    std::unordered_map<BLAKE3Pipeline::Hash, Eigen::Vector4d, HashHasher> comp_centroids_;
};

} // namespace Hartonomous
