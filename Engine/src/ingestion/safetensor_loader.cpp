/**
 * @file safetensor_loader.cpp
 * @brief Safetensor loader implementation
 */

#include <ingestion/safetensor_loader.hpp>
#include <ingestion/text_ingester.hpp>
#include <fstream>
#include <sstream>
#include <cstring>
#include <set>
#include <regex>
#include <algorithm>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

namespace Hartonomous {

using namespace std;

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

    // Handle sharded models (model.safetensors.index.json)
    std::string index_path = model_dir_ + "/model.safetensors.index.json";
    if (std::ifstream(index_path).good()) {
        load_sharded_model(index_path);
        return;
    }

    // Try alternate sharded index name
    index_path = model_dir_ + "/pytorch_model.safetensors.index.json";
    if (std::ifstream(index_path).good()) {
        load_sharded_model(index_path);
        return;
    }
}

void SafetensorLoader::load_sharded_model(const std::string& index_path) {
    std::ifstream file(index_path);
    if (!file) {
        throw std::runtime_error("Failed to open index file: " + index_path);
    }

    json index;
    file >> index;

    // Get unique shard files
    std::set<std::string> shard_files;
    if (index.contains("weight_map")) {
        for (auto& [tensor_name, shard_file] : index["weight_map"].items()) {
            shard_files.insert(shard_file);
        }
    }

    // Load each shard
    for (const auto& shard : shard_files) {
        std::string shard_path = model_dir_ + "/" + shard;
        if (std::ifstream(shard_path).good()) {
            load_safetensor_file(shard_path);
        }
    }
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

    // Helper for FP16 to FP32 conversion (IEEE 754)
    auto half_to_float = [](uint16_t h) -> float {
        uint32_t s = (h >> 15) & 0x0001;
        uint32_t e = (h >> 10) & 0x001F;
        uint32_t m = h & 0x03FF;

        if (e == 0) {
            if (m == 0) {
                // Zero
                uint32_t val = s << 31;
                float f;
                std::memcpy(&f, &val, 4);
                return f;
            } else {
                // Denormalized
                return (s ? -1.0f : 1.0f) * std::ldexp((float)m, -24);
            }
        } else if (e == 31) {
            // Inf or NaN
            uint32_t val = (s << 31) | 0x7F800000 | (m << 13);
            float f;
            std::memcpy(&f, &val, 4);
            return f;
        } else {
            // Normalized
            uint32_t val = (s << 31) | ((e + 112) << 23) | (m << 13);
            float f;
            std::memcpy(&f, &val, 4);
            return f;
        }
    };

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

        // Convert to float32
        size_t num_elements = tensor.total_elements();
        tensor.data.resize(num_elements);

        if (tensor.dtype == "F32") {
            std::memcpy(tensor.data.data(), raw_data.data(), data_size);
        } else if (tensor.dtype == "F16") {
            const uint16_t* half_data = (const uint16_t*)raw_data.data();
            for (size_t i = 0; i < num_elements; ++i) {
                tensor.data[i] = half_to_float(half_data[i]);
            }
        } else if (tensor.dtype == "BF16") {
            // BF16: same exponent range as F32, just truncated mantissa
            const uint16_t* bf16_data = (const uint16_t*)raw_data.data();
            for (size_t i = 0; i < num_elements; ++i) {
                uint32_t val = static_cast<uint32_t>(bf16_data[i]) << 16;
                float f;
                std::memcpy(&f, &val, 4);
                tensor.data[i] = f;
            }
        } else if (tensor.dtype == "F64") {
            const double* f64_data = (const double*)raw_data.data();
            for (size_t i = 0; i < num_elements; ++i) {
                tensor.data[i] = static_cast<float>(f64_data[i]);
            }
        } else if (tensor.dtype == "I32") {
            const int32_t* i32_data = (const int32_t*)raw_data.data();
            for (size_t i = 0; i < num_elements; ++i) {
                tensor.data[i] = static_cast<float>(i32_data[i]);
            }
        } else if (tensor.dtype == "I64") {
            const int64_t* i64_data = (const int64_t*)raw_data.data();
            for (size_t i = 0; i < num_elements; ++i) {
                tensor.data[i] = static_cast<float>(i64_data[i]);
            }
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
    // Common embedding tensor names across architectures
    const char* embedding_names[] = {
        // BERT/RoBERTa/DistilBERT
        "embeddings.word_embeddings.weight",
        "bert.embeddings.word_embeddings.weight",
        "roberta.embeddings.word_embeddings.weight",
        "distilbert.embeddings.word_embeddings.weight",
        // LLaMA/Mistral/Qwen
        "model.embed_tokens.weight",
        "model.embeddings.weight",
        // GPT-2/GPT-Neo/GPT-J
        "transformer.wte.weight",
        "wte.weight",
        // T5/FLAN
        "encoder.embed_tokens.weight",
        "shared.weight",
        // Falcon
        "transformer.word_embeddings.weight",
        // MPT
        "transformer.wpe.weight",
        // BLOOM
        "word_embeddings.weight",
        "word_embeddings_layernorm.weight",
        // Sentence Transformers / MiniLM
        "embeddings.word_embeddings.weight",
        "0.auto_model.embeddings.word_embeddings.weight",
        // Generic fallbacks
        "embed_tokens.weight",
        "token_embedding.weight",
        "embedding.weight"
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

    // Fallback: search for any 2D tensor with "embed" in name
    for (const auto& [name, tensor] : tensors_) {
        if (tensor.shape.size() == 2 &&
            (name.find("embed") != std::string::npos || name.find("Embed") != std::string::npos)) {
            size_t vocab_size = tensor.shape[0];
            size_t embed_dim = tensor.shape[1];

            if (vocab_size > 1000 && embed_dim >= 64) {  // Sanity check
                Eigen::MatrixXf embeddings(vocab_size, embed_dim);
                for (size_t i = 0; i < vocab_size; ++i) {
                    for (size_t j = 0; j < embed_dim; ++j) {
                        embeddings(i, j) = tensor.data[i * embed_dim + j];
                    }
                }
                return embeddings;
            }
        }
    }

    return Eigen::MatrixXf(0, 0);
}

std::vector<AttentionLayer> SafetensorLoader::get_attention_layers() const {
    std::vector<AttentionLayer> layers;

    // Pattern match for attention weight tensors across architectures
    std::regex layer_pattern(R"(.*\.(\d+)\..*(q_proj|k_proj|v_proj|o_proj|query|key|value|dense|attention\.self|attn\.(c_attn|c_proj)|self_attn).*\.weight)");

    std::map<int, AttentionLayer> layer_map;

    for (const auto& [name, tensor] : tensors_) {
        std::smatch match;
        if (std::regex_match(name, match, layer_pattern)) {
            int layer_idx = std::stoi(match[1].str());

            if (layer_map.find(layer_idx) == layer_map.end()) {
                layer_map[layer_idx].layer_index = layer_idx;
            }

            auto& layer = layer_map[layer_idx];

            // Identify which weight this is based on full tensor name
            std::string lower_name = name;
            std::transform(lower_name.begin(), lower_name.end(), lower_name.begin(), ::tolower);

            if (lower_name.find("q_proj") != std::string::npos ||
                lower_name.find("query") != std::string::npos ||
                lower_name.find(".q.") != std::string::npos ||
                lower_name.find("wq") != std::string::npos) {
                layer.q_weight = &tensor;
            } else if (lower_name.find("k_proj") != std::string::npos ||
                       lower_name.find("key") != std::string::npos ||
                       lower_name.find(".k.") != std::string::npos ||
                       lower_name.find("wk") != std::string::npos) {
                layer.k_weight = &tensor;
            } else if (lower_name.find("v_proj") != std::string::npos ||
                       lower_name.find("value") != std::string::npos ||
                       lower_name.find(".v.") != std::string::npos ||
                       lower_name.find("wv") != std::string::npos) {
                layer.v_weight = &tensor;
            } else if (lower_name.find("o_proj") != std::string::npos ||
                       lower_name.find("out_proj") != std::string::npos ||
                       lower_name.find("dense.weight") != std::string::npos ||
                       lower_name.find("wo") != std::string::npos ||
                       (lower_name.find("c_proj") != std::string::npos && lower_name.find("attn") != std::string::npos)) {
                layer.o_weight = &tensor;
            }
        }
    }

    for (auto& [idx, layer] : layer_map) {
        layers.push_back(std::move(layer));
    }

    std::sort(layers.begin(), layers.end(), [](const AttentionLayer& a, const AttentionLayer& b) {
        return a.layer_index < b.layer_index;
    });

    return layers;
}

std::vector<FFNLayer> SafetensorLoader::get_ffn_layers() const {
    std::vector<FFNLayer> layers;

    // Pattern match for FFN weight tensors
    std::regex layer_pattern(R"(.*\.(\d+)\..*(mlp|feed_forward|ffn|fc1|fc2|gate_proj|up_proj|down_proj|intermediate|output).*\.weight)");

    std::map<int, FFNLayer> layer_map;

    for (const auto& [name, tensor] : tensors_) {
        std::smatch match;
        if (std::regex_match(name, match, layer_pattern)) {
            int layer_idx = std::stoi(match[1].str());

            if (layer_map.find(layer_idx) == layer_map.end()) {
                layer_map[layer_idx].layer_index = layer_idx;
            }

            auto& layer = layer_map[layer_idx];

            std::string weight_type = match[2].str();
            if (weight_type.find("gate") != std::string::npos || weight_type == "fc1" || weight_type.find("intermediate") != std::string::npos) {
                layer.gate_weight = &tensor;
            } else if (weight_type.find("up") != std::string::npos) {
                layer.up_weight = &tensor;
            } else if (weight_type.find("down") != std::string::npos || weight_type == "fc2" || weight_type.find("output") != std::string::npos) {
                layer.down_weight = &tensor;
            }
        }
    }

    for (auto& [idx, layer] : layer_map) {
        layers.push_back(std::move(layer));
    }

    std::sort(layers.begin(), layers.end(), [](const FFNLayer& a, const FFNLayer& b) {
        return a.layer_index < b.layer_index;
    });

    return layers;
}

std::vector<std::string> SafetensorLoader::get_layer_names_matching(const std::string& pattern) const {
    std::vector<std::string> matches;
    std::regex re(pattern);

    for (const auto& [name, _] : tensors_) {
        if (std::regex_search(name, re)) {
            matches.push_back(name);
        }
    }

    return matches;
}

} // namespace Hartonomous
