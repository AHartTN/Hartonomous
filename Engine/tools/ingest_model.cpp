/**
 * @file ingest_model.cpp
 * @brief CLI tool to ingest AI model packages into Hartonomous substrate
 *
 * Usage: ingest_model <model_directory>
 */

#include <ingestion/model_ingester.hpp>
#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <iostream>
#include <filesystem>

using namespace Hartonomous;
namespace fs = std::filesystem;

int main(int argc, char** argv) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <model_directory>\n";
        return 1;
    }

    fs::path model_dir(argv[1]);
    if (!fs::exists(model_dir) || !fs::is_directory(model_dir)) {
        std::cerr << "Error: '" << model_dir << "' is not a valid directory\n";
        return 1;
    }

    try {
        PostgresConnection db;
        if (!db.is_connected()) {
            std::cerr << "Failed to connect to database. Check PG environment variables.\n";
            return 1;
        }

        ModelIngestionConfig config;
        config.tenant_id = BLAKE3Pipeline::hash("default-tenant");
        config.user_id = BLAKE3Pipeline::hash("default-user");
        config.embedding_similarity_threshold = 0.3;
        config.max_neighbors_per_token = 20;

        ModelIngester ingester(db, config);
        auto stats = ingester.ingest_package(model_dir);

        std::cout << "\n✓ Model ingestion complete!\n";
        std::cout << "  Tensors processed: " << stats.tensors_processed << "\n";
        std::cout << "  Atoms created:     " << stats.atoms_created << "\n";
        std::cout << "  Compositions:      " << stats.compositions_created << "\n";
        std::cout << "  Relations:         " << stats.relations_created << "\n";
        
        return 0;

    } catch (const std::exception& e) {
        std::cerr << "\nError: " << e.what() << "\n";
        return 1;
    }
}