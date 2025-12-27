/// hartonomous-model-ingest: Ingest AI models into the universal substrate
///
/// Usage:
///   hartonomous-model-ingest <model-path> [options]
///
/// Options:
///   --sparsity <threshold>  Sparsity threshold (default: 1e-6)
///   --quiet                 Suppress output
///   --verbose               Show detailed progress
///
/// The model path can be:
///   - A directory containing model files (tokenizer, config, safetensors)
///   - A single .safetensors file
///   - A HuggingFace cache directory (models--org--name/...)
///
/// All content is ingested semantically:
///   1. Tokenizer vocabulary → every token ingested as semantic content
///   2. Config files → ingested as structured content
///   3. Weights → sparse relationships anchored to tokens

#include "../model/model_ingest.hpp"
#include "../db/seeder.hpp"
#include <iostream>
#include <iomanip>
#include <chrono>
#include <cstring>

using namespace hartonomous;
using namespace hartonomous::model;
using namespace hartonomous::db;

void print_usage(const char* prog) {
    std::cerr << "Usage: " << prog << " <model-path> [options]\n"
              << "\n"
              << "Options:\n"
              << "  --sparsity <threshold>  Sparsity threshold (default: 1e-6)\n"
              << "  --quiet                 Suppress output\n"
              << "  --verbose               Show detailed progress\n"
              << "\n"
              << "Examples:\n"
              << "  " << prog << " ./models/gpt2\n"
              << "  " << prog << " ~/.cache/huggingface/hub/models--openai--gpt2\n"
              << "  " << prog << " model.safetensors\n";
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        print_usage(argv[0]);
        return 1;
    }

    std::string model_path;
    double sparsity = 1e-6;
    bool quiet = false;
    bool verbose = false;

    // Parse arguments
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--sparsity") == 0 && i + 1 < argc) {
            sparsity = std::stod(argv[++i]);
        } else if (std::strcmp(argv[i], "--quiet") == 0) {
            quiet = true;
        } else if (std::strcmp(argv[i], "--verbose") == 0) {
            verbose = true;
        } else if (std::strcmp(argv[i], "--help") == 0 || std::strcmp(argv[i], "-h") == 0) {
            print_usage(argv[0]);
            return 0;
        } else if (argv[i][0] != '-') {
            model_path = argv[i];
        } else {
            std::cerr << "Unknown option: " << argv[i] << "\n";
            return 1;
        }
    }

    if (model_path.empty()) {
        std::cerr << "Error: No model path specified\n";
        print_usage(argv[0]);
        return 1;
    }

    // Check path exists
    if (!std::filesystem::exists(model_path)) {
        std::cerr << "Error: Path does not exist: " << model_path << "\n";
        return 1;
    }

    try {
        // Ensure database schema
        if (!quiet) {
            std::cout << "Ensuring database schema..." << std::flush;
        }
        Seeder seeder(quiet);
        seeder.ensure_schema();
        if (!quiet) {
            std::cout << " done\n";
        }

        // Create ingester
        QueryStore store;
        ModelIngester ingester(store, sparsity);

        if (!quiet) {
            std::cout << "Ingesting model: " << model_path << "\n";
            std::cout << "Sparsity threshold: " << sparsity << "\n";
            std::cout << std::string(50, '-') << "\n";
        }

        auto start = std::chrono::high_resolution_clock::now();

        // Ingest
        ModelResult result = ingester.ingest_package(model_path);

        auto end = std::chrono::high_resolution_clock::now();
        auto total_ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();

        if (!quiet) {
            std::cout << std::string(50, '-') << "\n";
            std::cout << "INGESTION COMPLETE\n";
            std::cout << std::string(50, '-') << "\n";
            std::cout << std::left << std::setw(20) << "Vocabulary:" << result.vocab.token_count << " tokens\n";
            std::cout << std::left << std::setw(20) << "Tokens ingested:" << result.vocab.ingested_count << "\n";
            std::cout << std::left << std::setw(20) << "Vocab time:" << result.vocab.duration.count() << " ms\n";
            std::cout << std::left << std::setw(20) << "Tensors:" << result.tensor_count << "\n";
            std::cout << std::left << std::setw(20) << "Total weights:" << result.total_weights << "\n";
            std::cout << std::left << std::setw(20) << "Stored weights:" << result.stored_weights << "\n";
            std::cout << std::left << std::setw(20) << "Sparsity:" << std::fixed << std::setprecision(1)
                      << (result.sparsity_ratio * 100) << "%\n";
            std::cout << std::string(50, '-') << "\n";
            std::cout << std::left << std::setw(20) << "TOTAL TIME:" << total_ms << " ms\n";
            std::cout << std::string(50, '-') << "\n";
        }

        // Output model context for scripting
        auto ctx = ingester.model_context();
        if (verbose || quiet) {
            // Machine-readable output
            std::cout << "MODEL_CONTEXT=" << ctx.id_high << ":" << ctx.id_low << "\n";
            std::cout << "TOKENS=" << result.vocab.ingested_count << "\n";
            std::cout << "WEIGHTS=" << result.stored_weights << "\n";
            std::cout << "TIME_MS=" << total_ms << "\n";
        }

        return 0;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
}
