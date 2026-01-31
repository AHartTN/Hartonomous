#include "Config.hpp"
#include "PgConnection.hpp"
#include "UcdIngestor.hpp"
#include <iostream>
#include <fstream>
#include <sstream>
#include <memory>
#include <stdexcept>
#include <filesystem>

namespace fs = std::filesystem;

std::string read_file_content(const std::string& path) {
    std::ifstream file(path);
    if (!file.is_open()) {
        throw std::runtime_error("Could not open file: " + path);
    }
    std::stringstream buffer;
    buffer << file.rdbuf();
    return buffer.str();
}

int main(int argc, char* argv[]) {
    // Default to 'data/' if no arg provided, or use arg as directory
    std::string data_dir = "data/";
    if (argc >= 2) {
        data_dir = argv[1];
    }
    
    // Ensure data_dir ends with slash
    if (data_dir.back() != '/') data_dir += '/';

    std::string schema_path = "ucd_schema.sql";

    try {
        DbConfig db_config = DbConfig::loadFromEnv();
        std::unique_ptr<IDatabaseConnection> db_conn = std::make_unique<PgConnection>();

        UcdIngestor ingestor(db_config, std::move(db_conn));
        ingestor.connect();

        if (fs::exists(schema_path)) {
            std::cout << "Applying schema from: " << schema_path << std::endl;
            std::string schema_sql = read_file_content(schema_path);
            ingestor.execute_sql(schema_sql);
            std::cout << "Schema applied successfully." << std::endl;
        } else {
            std::cerr << "Warning: Schema file not found at " << schema_path << ". Assuming DB is set up." << std::endl;
        }

        // Run the Ingestion Pipeline
        std::cout << "Starting UCD Ingestion Pipeline on directory: " << data_dir << std::endl;
        ingestor.ingest_directory(data_dir);
        
        std::cout << "Ingestion Complete." << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "Fatal Error: " << e.what() << std::endl;
        return 1;
    }

    return 0;
}