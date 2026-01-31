#pragma once

#include <string>
#include <vector>
#include <filesystem>
#include <unordered_map>

namespace Hartonomous {

struct ModelFile {
    std::filesystem::path path;
    std::string type;
    size_t size;
};

struct ModelPackage {
    std::string name;
    std::string architecture;
    std::vector<ModelFile> safetensor_files;
    std::vector<ModelFile> config_files;
    std::vector<ModelFile> tokenizer_files;
    std::unordered_map<std::string, std::string> metadata;
};

class ModelPackageLoader {
public:
    static ModelPackage load_huggingface_model(const std::filesystem::path& model_dir);

private:
    static void parse_config_json(ModelPackage& pkg, const std::filesystem::path& path);
    static void parse_model_index(ModelPackage& pkg, const std::filesystem::path& path);
};

}
