/**
 * @file model_ingester.hpp
 * @brief AI model package ingestion into substrate
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <ingestion/safetensor_loader.hpp>
#include <nlohmann/json.hpp>
#include <hnswlib/hnswlib.h>
#include <string>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include <filesystem>
#include <Eigen/Dense>
#include <cstring>

namespace Hartonomous {

/**
 * @brief Standard hasher for BLAKE3 128-bit hashes
 */
struct HashHasher {
    size_t operator()(const BLAKE3Pipeline::Hash& hash) const {
        size_t h;
        std::memcpy(&h, hash.data(), sizeof(size_t));
        return h;
    }
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

struct ModelIngestionConfig {
    BLAKE3Pipeline::Hash tenant_id;
    BLAKE3Pipeline::Hash user_id;
    double embedding_similarity_threshold = 0.3;
    size_t max_neighbors_per_token = 100;
};

class ModelIngester {
public:
    explicit ModelIngester(PostgresConnection& db, const ModelIngestionConfig& config = ModelIngestionConfig());

    ModelIngestionStats ingest_package(const std::filesystem::path& package_dir);

private:
    PostgresConnection& db_;
    ModelIngestionConfig config_;
    BLAKE3Pipeline::Hash model_id_;

    void create_vocab_compositions(const std::vector<std::string>& vocab, ModelIngestionStats& stats);
    void extract_embedding_relations(const std::vector<std::string>& vocab,
                                     const Eigen::MatrixXf& embeddings,
                                     ModelIngestionStats& stats);
    void ingest_tensor(const std::string& name, const TensorData& tensor, ModelIngestionStats& stats);

    BLAKE3Pipeline::Hash create_physicality_from_unicode(const std::string& text);
    
    std::string hash_to_uuid(const BLAKE3Pipeline::Hash& hash);
    
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_physicality_ids_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_atom_ids_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_composition_ids_;
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> seen_relation_ids_;
    void load_global_caches();
};

} // namespace Hartonomous
