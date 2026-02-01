/**
 * @file seed_unicode.cpp
 * @brief High-performance bulk seeder for Unicode Atom/Physicality (S³ projection) 
 * 
 * Vertically integrated C++ ingestion engine that parses UCD data,
 * assigns semantic sequencing, and generates deterministic S³ nodes.
 */

#include <unicode/ingestor/ucd_processor.hpp>
#include <database/postgres_connection.hpp>
#include <iostream>
#include <chrono>

using namespace Hartonomous;
using namespace Hartonomous::unicode;

int main(int argc, char** argv) {
    try {
        std::string data_dir = "UCDIngestor/data";
        if (argc > 1) {
            data_dir = argv[1];
        }

        std::cout << "=== Hartonomous Unicode Seeding Tool ===\n";
        std::cout << "Data Directory: " << data_dir << "\n";

        auto start_time = std::chrono::high_resolution_clock::now();

        // 1. Connect to DB
        PostgresConnection db;
        if (!db.is_connected()) {
            throw std::runtime_error("Failed to connect to database. Check PG environment variables.");
        }

        // 2. Initialize Processor
        UCDProcessor processor(data_dir, db);

        // 3. Run Pipeline
        std::cout << "Checking existing atoms...\n";
        auto count_str = db.query_single("SELECT count(*) FROM hartonomous.atom");
        size_t atom_count = count_str ? std::stoul(*count_str) : 0;

        if (atom_count >= 1114112) {
            std::cout << "✓ Atoms already seeded (" << atom_count << "). Loading metadata from Gene Pool...\n";
            processor.load_from_database();
            processor.ingest_ucd_metadata();
        } else {
            processor.process_and_ingest();
        }

        auto end_time = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::seconds>(end_time - start_time).count();

        std::cout << "\n✓ DONE. Unicode universe seeded in " << duration << "s.\n";
        return 0;

    } catch (const std::exception& e) {
        std::cerr << "\nFATAL ERROR: " << e.what() << "\n";
        return 1;
    }
}
