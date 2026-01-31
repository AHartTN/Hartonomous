#include <ingestion/text_ingester.hpp>
#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <iostream>

using namespace Hartonomous;

int main(int argc, char** argv) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <text|file> [path]\n";
        std::cerr << "\nExamples:\n";
        std::cerr << "  " << argv[0] << " \"Hello World\"\n";
        std::cerr << "  " << argv[0] << " file /path/to/document.txt\n";
        return 1;
    }

    try {
        PostgresConnection db;
        if (!db.is_connected()) {
            std::cerr << "Failed to connect to database.\n";
            return 1;
        }

        // Configure ingestion with default tenant/user
        IngestionConfig config;
        config.tenant_id = BLAKE3Pipeline::hash("default-tenant");
        config.user_id = BLAKE3Pipeline::hash("default-user");
        config.min_ngram_size = 1;
        config.max_ngram_size = 8;
        config.min_frequency = 2;
        config.cooccurrence_window = 5;
        config.min_cooccurrence = 2;

        TextIngester ingester(db, config);

        IngestionStats stats;
        if (std::string(argv[1]) == "file" && argc >= 3) {
            std::cout << "Ingesting file: " << argv[2] << "\n";
            stats = ingester.ingest_file(argv[2]);
        } else {
            std::cout << "Ingesting text: " << argv[1] << "\n";
            stats = ingester.ingest(argv[1]);
        }

        std::cout << "\n=== Ingestion Complete ===\n"
                  << "Input: " << stats.original_bytes << " bytes\n"
                  << "\nAtoms (codepoints):\n"
                  << "  New: " << stats.atoms_new << " / Total unique: " << stats.atoms_total << "\n"
                  << "\nN-gram Analysis:\n"
                  << "  Extracted: " << stats.ngrams_extracted << "\n"
                  << "  Significant (freq >= " << config.min_frequency << "): " << stats.ngrams_significant << "\n"
                  << "\nCompositions:\n"
                  << "  New: " << stats.compositions_new << " / Total: " << stats.compositions_total << "\n"
                  << "\nCo-occurrence Analysis:\n"
                  << "  Found: " << stats.cooccurrences_found << "\n"
                  << "  Significant (count >= " << config.min_cooccurrence << "): " << stats.cooccurrences_significant << "\n"
                  << "\nRelations:\n"
                  << "  New: " << stats.relations_new << " / Total: " << stats.relations_total << "\n"
                  << "  Evidence records: " << stats.evidence_count << "\n";

        return 0;
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
}
