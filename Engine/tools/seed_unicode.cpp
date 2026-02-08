/**
 * @file seed_unicode.cpp
 * @brief High-performance bulk seeder for Unicode Atom/Physicality (S³ projection) 
 * 
 * Vertically integrated C++ ingestion engine that parses UCD data,
 * assigns semantic sequencing, and generates deterministic S³ nodes.
 */

#include <unicode/ingestor/ucd_processor.hpp>
#include <database/postgres_connection.hpp>
#include <utils/time.hpp>
#include <iostream>

using namespace Hartonomous;
using namespace Hartonomous::unicode;

int main(int argc, char** argv) {
    try {
        std::string data_dir = "Engine/data/ucd";
        if (argc > 1) {
            data_dir = argv[1];
        }

        std::cout << "=== Hartonomous Unicode Seeding Tool ===\n";
        std::cout << "Data Directory: " << data_dir << "\n";

        Timer timer;

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
            std::cout << "✓ Atoms already seeded (" << atom_count << "). Skipping.\n";
            return 0;
        } else {
            processor.process_and_ingest();
        }

        std::cout << "\n✓ DONE. Unicode universe seeded in " << timer.elapsed_sec() << "s.\n";
        return 0;

    } catch (const std::exception& e) {
        std::cerr << "\nFATAL ERROR: " << e.what() << "\n";
        return 1;
    }
}
