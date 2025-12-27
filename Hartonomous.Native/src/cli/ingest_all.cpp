/// hartonomous-ingest-all: Universal content ingestion into the substrate
///
/// Usage:
///   hartonomous-ingest-all <path> [options]
///
/// Ingests EVERYTHING:
///   - Text files (txt, md, json, yaml, xml, html, css, js, py, cpp, etc.)
///   - AI models (safetensors, tokenizer, config)
///   - Images (png, jpg, gif, bmp, webp)
///   - Audio (wav, mp3, flac, ogg)
///   - Binary (any file as raw bytes)
///   - Directories (recursive)
///
/// Options:
///   --sparsity <threshold>  For AI weights (default: 1e-6)
///   --quiet                 Suppress output
///   --parallel <n>          Thread count (default: CPU cores)

#include "../model/model_ingest.hpp"
#include "../db/seeder.hpp"
#include "../db/query_store.hpp"
#include "../db/bulk_store.hpp"
#include <iostream>
#include <iomanip>
#include <chrono>
#include <cstring>
#include <fstream>
#include <thread>
#include <atomic>
#include <mutex>
#include <queue>
#include <condition_variable>

using namespace hartonomous;
using namespace hartonomous::model;
using namespace hartonomous::db;

struct IngestStats {
    std::atomic<std::size_t> files_total{0};
    std::atomic<std::size_t> files_done{0};
    std::atomic<std::size_t> bytes_total{0};
    std::atomic<std::size_t> bytes_done{0};
    std::atomic<std::size_t> compositions{0};
    std::atomic<std::size_t> errors{0};
};

class UniversalIngester {
    QueryStore& store_;
    double sparsity_;
    bool quiet_;
    IngestStats& stats_;
    std::mutex output_mutex_;

    // Text extensions
    static constexpr const char* TEXT_EXTS[] = {
        ".txt", ".md", ".json", ".yaml", ".yml", ".xml", ".html", ".htm",
        ".css", ".js", ".ts", ".jsx", ".tsx", ".py", ".cpp", ".hpp",
        ".c", ".h", ".rs", ".go", ".java", ".kt", ".swift", ".rb",
        ".php", ".sh", ".bash", ".ps1", ".sql", ".csv", ".toml", ".ini",
        ".cfg", ".conf", ".log", ".rst", ".tex", ".bib", ".gitignore",
        ".dockerfile", ".makefile", ".cmake", nullptr
    };

    // Model extensions
    static constexpr const char* MODEL_EXTS[] = {
        ".safetensors", ".bin", ".pt", ".pth", ".onnx", ".gguf", nullptr
    };

    // Image extensions (ingest as raw bytes)
    static constexpr const char* IMAGE_EXTS[] = {
        ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp", ".tiff", ".ico", nullptr
    };

    // Audio extensions (ingest as raw bytes)
    static constexpr const char* AUDIO_EXTS[] = {
        ".wav", ".mp3", ".flac", ".ogg", ".m4a", ".aac", nullptr
    };

public:
    UniversalIngester(QueryStore& store, double sparsity, bool quiet, IngestStats& stats)
        : store_(store), sparsity_(sparsity), quiet_(quiet), stats_(stats) {}

    void ingest_path(const std::filesystem::path& path) {
        if (std::filesystem::is_directory(path)) {
            ingest_directory(path);
        } else if (std::filesystem::is_regular_file(path)) {
            ingest_file(path);
        }
    }

private:
    void ingest_directory(const std::filesystem::path& dir) {
        // Check if this is a model directory (has tokenizer + safetensors)
        bool has_tokenizer = false;
        bool has_safetensor = false;

        for (const auto& entry : std::filesystem::directory_iterator(dir)) {
            if (!entry.is_regular_file()) continue;
            std::string name = entry.path().filename().string();
            std::string ext = entry.path().extension().string();

            if (name == "tokenizer.json" || name == "vocab.txt" || name == "vocab.json") {
                has_tokenizer = true;
            }
            if (ext == ".safetensors") {
                has_safetensor = true;
            }
        }

        if (has_tokenizer && has_safetensor) {
            // This is a model package - use ModelIngester
            ingest_model_package(dir);
        } else {
            // Regular directory - recurse
            for (const auto& entry : std::filesystem::recursive_directory_iterator(dir)) {
                if (entry.is_regular_file()) {
                    ingest_file(entry.path());
                }
            }
        }
    }

    void ingest_model_package(const std::filesystem::path& dir) {
        if (!quiet_) {
            std::lock_guard<std::mutex> lock(output_mutex_);
            std::cout << "[MODEL] " << dir.string() << "\n";
        }

        try {
            ModelIngester ingester(store_, sparsity_);
            auto result = ingester.ingest_package(dir.string());

            stats_.compositions += result.stored_weights;
            stats_.files_done += result.tensor_count + 1;  // +1 for vocab

            if (!quiet_) {
                std::lock_guard<std::mutex> lock(output_mutex_);
                std::cout << "  → " << result.vocab.ingested_count << " tokens, "
                          << result.stored_weights << " weights (" << result.tensor_count << " tensors)\n";
            }
        } catch (const std::exception& e) {
            stats_.errors++;
            if (!quiet_) {
                std::lock_guard<std::mutex> lock(output_mutex_);
                std::cerr << "  [ERROR] " << e.what() << "\n";
            }
        }
    }

    void ingest_file(const std::filesystem::path& file) {
        stats_.files_total++;

        std::string ext = file.extension().string();
        std::transform(ext.begin(), ext.end(), ext.begin(), ::tolower);

        std::size_t size = std::filesystem::file_size(file);
        stats_.bytes_total += size;

        try {
            if (is_text_ext(ext)) {
                ingest_text_file(file, size);
            } else if (is_model_ext(ext)) {
                ingest_model_file(file, size);
            } else {
                // Everything else: ingest as raw bytes
                ingest_binary_file(file, size);
            }

            stats_.files_done++;
            stats_.bytes_done += size;

        } catch (const std::exception& e) {
            stats_.errors++;
            if (!quiet_) {
                std::lock_guard<std::mutex> lock(output_mutex_);
                std::cerr << "[ERROR] " << file.string() << ": " << e.what() << "\n";
            }
        }
    }

    void ingest_text_file(const std::filesystem::path& file, std::size_t size) {
        std::ifstream f(file, std::ios::binary);
        if (!f) return;

        std::string content((std::istreambuf_iterator<char>(f)),
                             std::istreambuf_iterator<char>());

        if (!content.empty()) {
            store_.encode_and_store(content);
            stats_.compositions++;

            if (!quiet_) {
                std::lock_guard<std::mutex> lock(output_mutex_);
                std::cout << "[TEXT] " << file.filename().string()
                          << " (" << size << " bytes)\n";
            }
        }
    }

    void ingest_model_file(const std::filesystem::path& file, std::size_t size) {
        if (!quiet_) {
            std::lock_guard<std::mutex> lock(output_mutex_);
            std::cout << "[MODEL] " << file.filename().string()
                      << " (" << size << " bytes)\n";
        }

        // Single safetensor file without tokenizer context
        // Just store the raw content + sparse weights
        SafetensorImporter importer(store_, sparsity_);
        auto [total, stored] = importer.import_model(file.string());
        stats_.compositions += stored;

        if (!quiet_) {
            std::lock_guard<std::mutex> lock(output_mutex_);
            std::cout << "  → " << stored << " / " << total << " weights stored\n";
        }
    }

    void ingest_binary_file(const std::filesystem::path& file, std::size_t size) {
        std::ifstream f(file, std::ios::binary);
        if (!f) return;

        std::vector<std::uint8_t> content(size);
        f.read(reinterpret_cast<char*>(content.data()), static_cast<std::streamsize>(size));

        if (!content.empty()) {
            store_.encode_and_store(content.data(), content.size());
            stats_.compositions++;

            if (!quiet_) {
                std::lock_guard<std::mutex> lock(output_mutex_);
                std::cout << "[BIN]  " << file.filename().string()
                          << " (" << size << " bytes)\n";
            }
        }
    }

    static bool is_text_ext(const std::string& ext) {
        for (int i = 0; TEXT_EXTS[i]; ++i) {
            if (ext == TEXT_EXTS[i]) return true;
        }
        return false;
    }

    static bool is_model_ext(const std::string& ext) {
        for (int i = 0; MODEL_EXTS[i]; ++i) {
            if (ext == MODEL_EXTS[i]) return true;
        }
        return false;
    }
};

void print_usage(const char* prog) {
    std::cerr << "Usage: " << prog << " <path> [options]\n"
              << "\n"
              << "Ingests EVERYTHING into the universal substrate.\n"
              << "\n"
              << "Options:\n"
              << "  --sparsity <threshold>  For AI weights (default: 1e-6)\n"
              << "  --quiet                 Suppress output\n"
              << "\n"
              << "Examples:\n"
              << "  " << prog << " ./my-document.txt\n"
              << "  " << prog << " ./my-project/\n"
              << "  " << prog << " D:\\Models\n"
              << "  " << prog << " ./image.png\n";
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        print_usage(argv[0]);
        return 1;
    }

    std::string path;
    double sparsity = 1e-6;
    bool quiet = false;

    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--sparsity") == 0 && i + 1 < argc) {
            sparsity = std::stod(argv[++i]);
        } else if (std::strcmp(argv[i], "--quiet") == 0) {
            quiet = true;
        } else if (std::strcmp(argv[i], "--help") == 0 || std::strcmp(argv[i], "-h") == 0) {
            print_usage(argv[0]);
            return 0;
        } else if (argv[i][0] != '-') {
            path = argv[i];
        }
    }

    if (path.empty()) {
        std::cerr << "Error: No path specified\n";
        return 1;
    }

    if (!std::filesystem::exists(path)) {
        std::cerr << "Error: Path does not exist: " << path << "\n";
        return 1;
    }

    try {
        // Ensure schema
        if (!quiet) std::cout << "Initializing database...\n";
        Seeder seeder(quiet);
        seeder.ensure_schema();

        // Create store and ingester
        QueryStore store;
        IngestStats stats;
        UniversalIngester ingester(store, sparsity, quiet, stats);

        if (!quiet) {
            std::cout << "Ingesting: " << path << "\n";
            std::cout << std::string(60, '=') << "\n";
        }

        auto start = std::chrono::high_resolution_clock::now();

        ingester.ingest_path(path);

        auto end = std::chrono::high_resolution_clock::now();
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();

        if (!quiet) {
            std::cout << std::string(60, '=') << "\n";
            std::cout << "INGESTION COMPLETE\n";
            std::cout << std::string(60, '-') << "\n";
            std::cout << std::left << std::setw(20) << "Files:" << stats.files_done << " / " << stats.files_total << "\n";
            std::cout << std::left << std::setw(20) << "Bytes:" << stats.bytes_done << " / " << stats.bytes_total << "\n";
            std::cout << std::left << std::setw(20) << "Compositions:" << stats.compositions << "\n";
            std::cout << std::left << std::setw(20) << "Errors:" << stats.errors << "\n";
            std::cout << std::left << std::setw(20) << "Time:" << ms << " ms\n";
            std::cout << std::string(60, '=') << "\n";
        }

        // Machine-readable output
        std::cout << "FILES=" << stats.files_done << "\n";
        std::cout << "BYTES=" << stats.bytes_done << "\n";
        std::cout << "COMPOSITIONS=" << stats.compositions << "\n";
        std::cout << "TIME_MS=" << ms << "\n";

        return stats.errors > 0 ? 1 : 0;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
}
