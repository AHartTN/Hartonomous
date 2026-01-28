/**
 * @file safetensor_loader.hpp
 * @brief HuggingFace safetensor model loader
 *
 * Loads safetensor files and extracts:
 * - Model tensors (embeddings, weights)
 * - Config (model architecture, hyperparams)
 * - Tokenizer (vocab, special tokens)
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <string>
#include <vector>
#include <map>
#include <memory>
#include <Eigen/Dense>

namespace Hartonomous {

/**
 * @brief Safetensor metadata
 */
struct SafetensorMetadata {
    std::string model_name;
    std::string model_type;
    std::map<std::string, std::string> config;
    std::vector<std::string> vocab;
    std::map<std::string, int> special_tokens;
};

/**
 * @brief Tensor data
 */
struct TensorData {
    std::string name;
    std::vector<size_t> shape;
    std::string dtype;
    std::vector<float> data;  // Converted to float32

    size_t total_elements() const {
        size_t total = 1;
        for (auto dim : shape) total *= dim;
        return total;
    }
};

/**
 * @brief Safetensor loader
 *
 * Loads HuggingFace models from directory containing:
 * - model.safetensors or model.safetensors.index.json (sharded)
 * - config.json
 * - tokenizer.json / tokenizer_config.json
 * - vocab.txt / vocab.json
 */
class SafetensorLoader {
public:
    /**
     * @brief Load model from directory
     * @param model_dir Path to model directory
     */
    explicit SafetensorLoader(const std::string& model_dir);

    /**
     * @brief Get metadata
     */
    const SafetensorMetadata& metadata() const { return metadata_; }

    /**
     * @brief Get tensor by name
     */
    const TensorData* get_tensor(const std::string& name) const;

    /**
     * @brief Get all tensor names
     */
    std::vector<std::string> tensor_names() const;

    /**
     * @brief Get embedding matrix (if exists)
     * @return Matrix of shape (vocab_size, embedding_dim)
     */
    Eigen::MatrixXf get_embeddings() const;

    /**
     * @brief Ingest into database
     * @param db Database connection
     * @param store_all_tensors If true, store all tensors; if false, only embeddings
     */
    void ingest(PostgresConnection& db, bool store_all_tensors = false);

private:
    void load_metadata();
    void load_config(const std::string& path);
    void load_tokenizer(const std::string& path);
    void load_vocab(const std::string& path);
    void load_safetensors();
    void load_safetensor_file(const std::string& path);

    std::string model_dir_;
    SafetensorMetadata metadata_;
    std::map<std::string, TensorData> tensors_;
};

} // namespace Hartonomous
