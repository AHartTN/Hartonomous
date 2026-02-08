/**
 * @file model_ingester.hpp
 * @brief AI model ingestion: Extract semantic edges from model embeddings
 *
 * The embedding matrix IS the value from AI models:
 * - Each row = a token's learned position in the model's semantic space
 * - KNN on rows = token-to-token relationships (the model's OPINIONS)
 * - Weight matrices (Q/K/V/FFN) are internal plumbing — dimensions don't map to tokens
 *
 * Model ingestion pipeline:
 * 1. Vocab tokens → Compositions (same pipeline as text ingestion)
 * 2. Embedding KNN → Relations with ELO (model opinions, lower than observed text)
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <storage/physicality_store.hpp>
#include <storage/relation_store.hpp>
#include <storage/relation_evidence_store.hpp>
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

// Thread-local accumulators for parallel edge extraction
struct ThreadLocalRecords {
    std::vector<PhysicalityRecord> phys;
    std::vector<RelationRecord> rel;
    std::vector<RelationSequenceRecord> rel_seq;
    std::vector<RelationRatingRecord> rating;
    std::vector<RelationEvidenceRecord> ev;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> phys_seen;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> rel_seen;
    size_t relations_created = 0;
};

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

struct HnswParams {
    size_t M = 16;
    size_t ef_construction = 200;
    size_t ef_search = 64;
};

struct ModelIngestionConfig {
    BLAKE3Pipeline::Hash tenant_id;
    BLAKE3Pipeline::Hash user_id;
    double embedding_similarity_threshold = 0.40;  // Similarity threshold for edge inclusion
    size_t max_neighbors_per_token = 64;           // Max neighbors to extract per token
    size_t db_batch_size = 100000;                 // Records per DB batch

    // HNSW parameter presets per search type
    HnswParams hnsw_embedding{16, 200, 128};   // High quality baseline (k=64, threshold=0.4)
    HnswParams hnsw_self_sim{12, 100, 64};     // Symmetric search (V, O, gate, up, down)
    HnswParams hnsw_asymmetric{16, 150, 80};   // Asymmetric search (Q*K attention)
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
        const Eigen::MatrixXf& norm_embeddings,
        const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
        ModelIngestionStats& stats
    );

    void extract_procedural_knn(
        const std::vector<std::string>& vocab,
        const Eigen::MatrixXf& Q,
        const Eigen::MatrixXf& K,
        const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
        ModelIngestionStats& stats,
        double base_elo,
        const std::string& type_tag,
        int layer_index,
        int total_layers,
        const HnswParams& params,
        std::vector<ThreadLocalRecords>* out_records = nullptr
    );

    void extract_procedural_knn_streaming(
        const std::vector<std::string>& vocab,
        const Eigen::MatrixXf& norm_embeddings,
        const Eigen::MatrixXf& W,
        const std::unordered_map<std::string, BLAKE3Pipeline::Hash>& token_to_comp,
        ModelIngestionStats& stats,
        double base_elo,
        const std::string& type_tag,
        int layer_index,
        int total_layers,
        const HnswParams& params,
        bool apply_sigmoid,
        std::vector<ThreadLocalRecords>* out_records = nullptr
    );

    static double weight_similarity(const TensorData* a, const TensorData* b);

    std::unordered_map<BLAKE3Pipeline::Hash, Eigen::Vector4d, HashHasher> comp_centroids_;
    Eigen::MatrixXf proj_workspace_a_;  // Reused for K or self-sim projections
    Eigen::MatrixXf proj_workspace_b_;  // Reused for Q in asymmetric case
};

} // namespace Hartonomous
