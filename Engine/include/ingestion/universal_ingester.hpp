#pragma once

#include <ingestion/text_ingester.hpp>
#include <ingestion/model_ingester.hpp>
#include <filesystem>

namespace Hartonomous {

/**
 * @brief Universal Ingester that dispatches to specific ingesters based on content type
 */
class UniversalIngester {
public:
    explicit UniversalIngester(PostgresConnection& db) : db_(db), text_ingester_(db) {}

    IngestionStats ingest_text(const std::string& text) {
        return text_ingester_.ingest(text);
    }

    IngestionStats ingest_path(const std::string& path) {
        namespace fs = std::filesystem;
        fs::path p(path);

        if (fs::is_directory(p)) {
            // Check for model files (config.json + .safetensors)
            if (fs::exists(p / "config.json") && 
               (fs::exists(p / "model.safetensors") || fs::exists(p / "model.safetensors.index.json") || fs::exists(p / "pytorch_model.safetensors"))) {
                
                ModelIngester model_ingester(db_);
                auto mstats = model_ingester.ingest_package(p);
                
                // Map ModelIngestionStats to IngestionStats
                IngestionStats stats;
                stats.compositions_new = mstats.compositions_created;
                stats.relations_new = mstats.relations_created;
                stats.atoms_new = mstats.atoms_created;
                stats.compositions_total = mstats.compositions_created; // Approximate
                stats.relations_total = mstats.relations_created;
                stats.atoms_total = mstats.atoms_created;
                return stats;
            }
        } else if (p.extension() == ".safetensors") {
            // Single safetensor file
            ModelIngester model_ingester(db_);
            // For now, treat directory of the file as the package if no config.json
            auto mstats = model_ingester.ingest_package(p.parent_path());
            
            IngestionStats stats;
            stats.compositions_new = mstats.compositions_created;
            stats.relations_new = mstats.relations_created;
            return stats;
        }

        // Default to text ingestion
        return text_ingester_.ingest_file(path);
    }

private:
    PostgresConnection& db_;
    TextIngester text_ingester_;
};

} // namespace Hartonomous
