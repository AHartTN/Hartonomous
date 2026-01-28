/**
 * @file safetensor_loader.cpp
 * @brief Safetensor loader implementation
 */

#include <ingestion/safetensor_loader.hpp>
#include <ingestion/text_ingester.hpp>
#include <fstream>
#include <sstream>
#include <cstring>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

namespace Hartonomous {

SafetensorLoader::SafetensorLoader(const std::string& model_dir) : model_dir_(model_dir) {
    load_metadata();
    load_safetensors();
}

void SafetensorLoader::load_metadata() {
    // Load config.json
    std::string config_path = model_dir_ + "/config.json";
    if (std::ifstream(config_path).good()) {
        load_config(config_path);
    }

    // Load tokenizer
    std::string tokenizer_path = model_dir_ + "/tokenizer.json";
    if (std::ifstream(tokenizer_path).good()) {
        load_tokenizer(tokenizer_path);
    }

    // Load vocab
    std::string vocab_path = model_dir_ + "/vocab.txt";
    if (std::ifstream(vocab_path).good()) {
        load_vocab(vocab_path);
    } else {
        vocab_path = model_dir_ + "/vocab.json";
        if (std::ifstream(vocab_path).good()) {
            load_vocab(vocab_path);
        }
    }
}

void SafetensorLoader::load_config(const std::string& path) {
    std::ifstream file(path);
    if (!file) return;

    json config;
    file >> config;

    // Extract common fields
    if (config.contains("model_type")) {
        metadata_.model_type = config["model_type"];
    }

    if (config.contains("_name_or_path")) {
        metadata_.model_name = config["_name_or_path"];
    }

    // Store all config as strings
    for (auto& [key, value] : config.items()) {
        if (value.is_string()) {
            metadata_.config[key] = value;
        } else if (value.is_number()) {
            metadata_.config[key] = std::to_string((double)value);
        } else if (value.is_boolean()) {
            metadata_.config[key] = value ? "true" : "false";
        }
    }
}

void SafetensorLoader::load_tokenizer(const std::string& path) {
    std::ifstream file(path);
    if (!file) return;

    json tokenizer;
    file >> tokenizer;

    // Extract vocab from tokenizer
    if (tokenizer.contains("model") && tokenizer["model"].contains("vocab")) {
        auto& vocab = tokenizer["model"]["vocab"];
        for (auto& [token, id] : vocab.items()) {
            if (id.is_number_integer()) {
                int idx = id;
                if (idx >= (int)metadata_.vocab.size()) {
                    metadata_.vocab.resize(idx + 1);
                }
                metadata_.vocab[idx] = token;
            }
        }
    }

    // Extract special tokens
    if (tokenizer.contains("added_tokens")) {
        for (auto& token_obj : tokenizer["added_tokens"]) {
            if (token_obj.contains("content") && token_obj.contains("id")) {
                std::string token = token_obj["content"];
                int id = token_obj["id"];
                metadata_.special_tokens[token] = id;
            }
        }
    }
}

void SafetensorLoader::load_vocab(const std::string& path) {
    std::ifstream file(path);
    if (!file) return;

    // Check if JSON or text
    std::string first_line;
    std::getline(file, first_line);
    file.seekg(0);

    if (first_line[0] == '{') {
        // JSON format
        json vocab;
        file >> vocab;

        for (auto& [token, id] : vocab.items()) {
            if (id.is_number_integer()) {
                int idx = id;
                if (idx >= (int)metadata_.vocab.size()) {
                    metadata_.vocab.resize(idx + 1);
                }
                metadata_.vocab[idx] = token;
            }
        }
    } else {
        // Text format (one token per line)
        std::string line;
        while (std::getline(file, line)) {
            metadata_.vocab.push_back(line);
        }
    }
}

void SafetensorLoader::load_safetensors() {
    // Try single file first
    std::string model_path = model_dir_ + "/model.safetensors";
    if (std::ifstream(model_path).good()) {
        load_safetensor_file(model_path);
        return;
    }

    // Try pytorch_model.bin equivalent
    model_path = model_dir_ + "/pytorch_model.safetensors";
    if (std::ifstream(model_path).good()) {
        load_safetensor_file(model_path);
        return;
    }

    // TODO: Handle sharded models (model.safetensors.index.json)
}

void SafetensorLoader::load_safetensor_file(const std::string& path) {
    std::ifstream file(path, std::ios::binary);
    if (!file) {
        throw std::runtime_error("Failed to open safetensor file: " + path);
    }

    // Read header size (first 8 bytes, little-endian uint64)
    uint64_t header_size = 0;
    file.read((char*)&header_size, 8);

    if (header_size > 100 * 1024 * 1024) {  // Sanity check: 100MB max header
        throw std::runtime_error("Invalid safetensor header size");
    }

    // Read header (JSON)
    std::vector<char> header_data(header_size);
    file.read(header_data.data(), header_size);

    json header = json::parse(header_data.begin(), header_data.end());

    // Parse tensors
    for (auto& [name, tensor_info] : header.items()) {
        if (name == "__metadata__") continue;  // Skip metadata

        TensorData tensor;
        tensor.name = name;

        // Get dtype
        if (tensor_info.contains("dtype")) {
            tensor.dtype = tensor_info["dtype"];
        }

        // Get shape
        if (tensor_info.contains("shape")) {
            for (auto& dim : tensor_info["shape"]) {
                tensor.shape.push_back(dim);
            }
        }

        // Get data offset
        size_t data_begin = tensor_info["data_offsets"][0];
        size_t data_end = tensor_info["data_offsets"][1];
        size_t data_size = data_end - data_begin;

        // Read tensor data
        std::vector<uint8_t> raw_data(data_size);
        file.seekg(8 + header_size + data_begin);
        file.read((char*)raw_data.data(), data_size);

        // Convert to float32 (simplified - assumes F32 or F16)
        size_t num_elements = tensor.total_elements();
        tensor.data.resize(num_elements);

        if (tensor.dtype == "F32") {
            std::memcpy(tensor.data.data(), raw_data.data(), data_size);
        } else if (tensor.dtype == "F16") {
            // TODO: Proper FP16 conversion
            // For now, just zero
            std::fill(tensor.data.begin(), tensor.data.end(), 0.0f);
        } else {
            // Unsupported dtype, fill with zeros
            std::fill(tensor.data.begin(), tensor.data.end(), 0.0f);
        }

        tensors_[name] = std::move(tensor);
    }
}

const TensorData* SafetensorLoader::get_tensor(const std::string& name) const {
    auto it = tensors_.find(name);
    return it != tensors_.end() ? &it->second : nullptr;
}

std::vector<std::string> SafetensorLoader::tensor_names() const {
    std::vector<std::string> names;
    for (auto& [name, _] : tensors_) {
        names.push_back(name);
    }
    return names;
}

Eigen::MatrixXf SafetensorLoader::get_embeddings() const {
    // Try common embedding tensor names
    const char* embedding_names[] = {
        "embeddings.word_embeddings.weight",
        "bert.embeddings.word_embeddings.weight",
        "model.embed_tokens.weight",
        "transformer.wte.weight",
        "word_embeddings.weight"
    };

    for (const char* name : embedding_names) {
        auto* tensor = get_tensor(name);
        if (tensor && tensor->shape.size() == 2) {
            size_t vocab_size = tensor->shape[0];
            size_t embed_dim = tensor->shape[1];

            Eigen::MatrixXf embeddings(vocab_size, embed_dim);

            for (size_t i = 0; i < vocab_size; ++i) {
                for (size_t j = 0; j < embed_dim; ++j) {
                    embeddings(i, j) = tensor->data[i * embed_dim + j];
                }
            }

            return embeddings;
        }
    }

    // Not found
    return Eigen::MatrixXf(0, 0);
}

void SafetensorLoader::ingest(PostgresConnection& db, bool store_all_tensors) {
    // Ingest metadata as text (config, model info)
    TextIngester text_ingester(db);

    // Ingest config as text
    for (auto& [key, value] : metadata_.config) {
        std::string config_text = key + ": " + value;
        text_ingester.ingest(config_text);
    }

    // Ingest vocab as compositions
    for (const auto& token : metadata_.vocab) {
        text_ingester.ingest(token);
    }

    // Get embeddings and store
    auto embeddings = get_embeddings();

    if (embeddings.rows() > 0) {
        // For each token, store its embedding as a composition
        // The embedding vector becomes metadata

        for (int i = 0; i < embeddings.rows() && i < (int)metadata_.vocab.size(); ++i) {
            const std::string& token = metadata_.vocab[i];

            // Hash the token text
            auto token_hash = BLAKE3Pipeline::hash(token);
            std::string hash_hex = BLAKE3Pipeline::to_hex(token_hash);

            // Store embedding metadata
            std::ostringstream embedding_json;
            embedding_json << "{\"embedding\": [";
            for (int j = 0; j < embeddings.cols(); ++j) {
                if (j > 0) embedding_json << ",";
                embedding_json << embeddings(i, j);
                if (j >= 10) {  // Limit for DB storage
                    embedding_json << "...";
                    break;
                }
            }
            embedding_json << "]}";

            // Update composition with embedding metadata
            db.execute(
                "UPDATE compositions SET metadata = $1 WHERE hash = $2",
                {embedding_json.str(), hash_hex}
            );
        }
    }

    // TODO: Store tensors as semantic edges (attention weights, etc.)
    if (store_all_tensors) {
        // Extract attention weights and store as semantic_edges
        // This is where we'd extract model relationships
    }
}

} // namespace Hartonomous
