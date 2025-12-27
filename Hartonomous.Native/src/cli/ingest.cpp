/// Hartonomous Ingestion Pipeline
/// 
/// Production-grade content ingestion to universal semantic substrate.
/// Uses Cascading Pair Encoding with frequency-based vocabulary learning.

#include "atoms/pair_encoding.hpp"
#include <chrono>
#include <fstream>
#include <iostream>
#include <filesystem>
#include <vector>
#include <iomanip>
#include <thread>

namespace fs = std::filesystem;
using namespace hartonomous;

struct IngestStats {
    std::size_t files_processed = 0;
    std::size_t bytes_processed = 0;
    std::size_t vocabulary_size = 0;
    double build_time_ms = 0;
    double throughput_mbps = 0;
};

/// File ingestion pipeline using the pair encoding engine.
class IngestionPipeline {
    PairEncodingEngine engine_;
    std::vector<std::pair<std::string, NodeRef>> file_roots_;
    
public:
    explicit IngestionPipeline(PairEncodingEngine::Config config = {}) : engine_(std::move(config)) {}
    
    /// Ingest a single file, returns root NodeRef
    NodeRef ingest_file(const fs::path& path) {
        std::ifstream file(path, std::ios::binary | std::ios::ate);
        if (!file) {
            std::cerr << "[WARN] Cannot open: " << path << "\n";
            return NodeRef{};
        }
        
        std::streamsize size = file.tellg();
        if (size <= 0) {
            std::cerr << "[WARN] Empty file: " << path << "\n";
            return NodeRef{};
        }
        
        file.seekg(0, std::ios::beg);
        
        std::vector<std::uint8_t> content(static_cast<std::size_t>(size));
        if (!file.read(reinterpret_cast<char*>(content.data()), size)) {
            std::cerr << "[WARN] Read failed: " << path << "\n";
            return NodeRef{};
        }
        
        NodeRef root = engine_.ingest(content.data(), content.size());
        file_roots_.emplace_back(path.string(), root);
        
        return root;
    }
    
    /// Ingest all files in directory recursively
    void ingest_directory(const fs::path& dir) {
        for (const auto& entry : fs::recursive_directory_iterator(dir)) {
            if (entry.is_regular_file()) {
                ingest_file(entry.path());
            }
        }
    }
    
    /// Get ingestion statistics
    IngestStats stats() const {
        IngestStats s;
        s.files_processed = file_roots_.size();
        s.bytes_processed = engine_.bytes_processed();
        s.vocabulary_size = engine_.vocabulary_size();
        return s;
    }
    
    /// Access the underlying engine
    PairEncodingEngine& engine() { return engine_; }
    const PairEncodingEngine& engine() const { return engine_; }
    
    /// Get all ingested file roots
    const std::vector<std::pair<std::string, NodeRef>>& file_roots() const { 
        return file_roots_; 
    }
};

void print_usage() {
    std::cout << "Hartonomous Ingestion Pipeline\n";
    std::cout << "==============================\n\n";
    std::cout << "Usage: hartonomous-ingest [options] <path> [path...]\n\n";
    std::cout << "Options:\n";
    std::cout << "  --chunk-size <bytes>     Chunk size for parallel processing (default: 1MB)\n";
    std::cout << "  --min-frequency <n>      Minimum pair frequency to merge (default: 2)\n";
    std::cout << "  --max-vocab <n>          Maximum vocabulary size (default: 1M)\n";
    std::cout << "  --threads <n>            Thread count (default: auto)\n";
    std::cout << "  --quiet                  Suppress progress output\n";
    std::cout << "  --help                   Show this help\n\n";
    std::cout << "Paths can be files or directories. Directories are processed recursively.\n";
}

int main(int argc, char* argv[]) {
    if (argc < 2) {
        print_usage();
        return 1;
    }
    
    // Parse arguments
    PairEncodingEngine::Config config;
    std::vector<fs::path> paths;
    bool quiet = false;
    
    for (int i = 1; i < argc; ++i) {
        std::string arg = argv[i];
        
        if (arg == "--help" || arg == "-h") {
            print_usage();
            return 0;
        } else if (arg == "--quiet" || arg == "-q") {
            quiet = true;
        } else if (arg == "--chunk-size" && i + 1 < argc) {
            config.chunk_size = std::stoull(argv[++i]);
        } else if (arg == "--min-frequency" && i + 1 < argc) {
            config.min_pair_frequency = std::stoull(argv[++i]);
        } else if (arg == "--max-vocab" && i + 1 < argc) {
            config.max_vocabulary_size = std::stoull(argv[++i]);
        } else if (arg == "--threads" && i + 1 < argc) {
            config.thread_count = std::stoull(argv[++i]);
        } else if (arg[0] != '-') {
            paths.emplace_back(arg);
        } else {
            std::cerr << "[ERROR] Unknown option: " << arg << "\n";
            return 1;
        }
    }
    
    if (paths.empty()) {
        std::cerr << "[ERROR] No input paths specified\n";
        return 1;
    }
    
    // Initialize pipeline
    IngestionPipeline pipeline(config);
    
    if (!quiet) {
        std::cout << "Hartonomous Ingestion Pipeline\n";
        std::cout << "==============================\n";
        std::cout << "Threads: " << (config.thread_count == 0 ? std::thread::hardware_concurrency() : config.thread_count) << "\n";
        std::cout << "Chunk size: " << (config.chunk_size / 1024) << " KB\n";
        std::cout << "Min frequency: " << config.min_pair_frequency << "\n";
        std::cout << "Max vocabulary: " << config.max_vocabulary_size << "\n";
        std::cout << "\n";
    }
    
    // Process all paths
    auto start = std::chrono::high_resolution_clock::now();
    
    for (const auto& path : paths) {
        if (!fs::exists(path)) {
            std::cerr << "[WARN] Path not found: " << path << "\n";
            continue;
        }
        
        if (!quiet) {
            std::cout << "Processing: " << path << "\n";
        }
        
        if (fs::is_directory(path)) {
            pipeline.ingest_directory(path);
        } else if (fs::is_regular_file(path)) {
            pipeline.ingest_file(path);
        }
    }
    
    auto end = std::chrono::high_resolution_clock::now();
    double elapsed_ms = std::chrono::duration<double, std::milli>(end - start).count();
    
    // Report results
    auto stats = pipeline.stats();
    double elapsed_sec = elapsed_ms / 1000.0;
    double throughput_mbps = (stats.bytes_processed / 1048576.0) / elapsed_sec;
    
    std::cout << "\n=== INGESTION COMPLETE ===\n";
    std::cout << std::fixed << std::setprecision(2);
    std::cout << "Files processed:  " << stats.files_processed << "\n";
    std::cout << "Bytes processed:  " << stats.bytes_processed << " (" 
              << (stats.bytes_processed / 1048576.0) << " MB)\n";
    std::cout << "Vocabulary size:  " << stats.vocabulary_size << "\n";
    std::cout << "Elapsed time:     " << elapsed_sec << " s\n";
    std::cout << "Throughput:       " << throughput_mbps << " MB/s\n";
    
    // Show top files by size
    if (!quiet && !pipeline.file_roots().empty()) {
        std::cout << "\nIngested files:\n";
        for (const auto& [path, root] : pipeline.file_roots()) {
            std::cout << "  " << path << " -> ";
            if (root.is_atom) {
                std::cout << "[atom]\n";
            } else {
                std::cout << std::hex << root.id_high << ":" << root.id_low << std::dec << "\n";
            }
        }
    }
    
    return 0;
}
