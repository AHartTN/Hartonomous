#include <ingestion/model_package_loader.hpp>
#include <nlohmann/json.hpp>
#include <fstream>

namespace Hartonomous {

namespace fs = std::filesystem;

ModelPackage ModelPackageLoader::load_huggingface_model(const fs::path& model_dir) {
    ModelPackage pkg;
    pkg.name = model_dir.filename().string();

    for (const auto& entry : fs::recursive_directory_iterator(model_dir)) {
        if (!entry.is_regular_file()) continue;

        ModelFile file;
        file.path = entry.path();
        file.size = fs::file_size(entry.path());
        auto ext = entry.path().extension().string();

        if (ext == ".safetensors") {
            file.type = "safetensor";
            pkg.safetensor_files.push_back(file);
        } else if (entry.path().filename() == "config.json") {
            file.type = "config";
            pkg.config_files.push_back(file);
            parse_config_json(pkg, entry.path());
        } else if (entry.path().filename().string().find("tokenizer") != std::string::npos) {
            file.type = "tokenizer";
            pkg.tokenizer_files.push_back(file);
        } else if (entry.path().filename() == "model.safetensors.index.json") {
            parse_model_index(pkg, entry.path());
        }
    }

    return pkg;
}

void ModelPackageLoader::parse_config_json(ModelPackage& pkg, const fs::path& path) {
    std::ifstream file(path);
    auto json = nlohmann::json::parse(file);

    if (json.contains("architectures") && json["architectures"].is_array()) {
        pkg.architecture = json["architectures"][0].get<std::string>();
    }

    if (json.contains("model_type")) {
        pkg.metadata["model_type"] = json["model_type"].get<std::string>();
    }
}

void ModelPackageLoader::parse_model_index(ModelPackage& pkg, const fs::path& path) {
    std::ifstream file(path);
    auto json = nlohmann::json::parse(file);

    if (json.contains("metadata")) {
        for (auto& [key, value] : json["metadata"].items()) {
            pkg.metadata[key] = value.dump();
        }
    }
}

}
