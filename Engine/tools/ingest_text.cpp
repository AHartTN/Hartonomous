#include <ingestion/text_ingester.hpp>
#include <database/postgres_connection.hpp>
#include <iostream>

using namespace Hartonomous;

int main(int argc, char** argv) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <text|file> [path]\n";
        return 1;
    }

    try {
        PostgresConnection db;
        TextIngester ingester(db);

        IngestionStats stats;
        if (std::string(argv[1]) == "file" && argc >= 3) {
            stats = ingester.ingest_file(argv[2]);
        } else {
            stats = ingester.ingest(argv[1]);
        }

        std::cout << "Ingestion complete:\n"
                  << "  Atoms: " << stats.atoms_new << " new / " << stats.atoms_total << " total\n"
                  << "  Compositions: " << stats.compositions_new << " new / " << stats.compositions_total << " total\n"
                  << "  Relations: " << stats.relations_total << "\n"
                  << "  Compression: " << (stats.compression_ratio() * 100) << "%\n";
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        PostgresConnection db2;
        std::cerr << "DB Error: " << db2.last_error() << "\n";
        return 1;
    }
}
