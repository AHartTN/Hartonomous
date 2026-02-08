#include <ingestion/text_ingester.hpp>
#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <ingestion/async_flusher.hpp>
#include <ingestion/substrate_cache.hpp>
#include <utils/time.hpp>
#include <iostream>

using namespace Hartonomous;

int main(int argc, char** argv) {
    if (argc < 2) {
        std::cerr << "Usage: " << argv[0] << " <text|file> [path]\n";
        return 1;
    }

    try {
        PostgresConnection db;
        if (!db.is_connected()) return 1;

        SubstrateCache cache;
        cache.pre_populate(db);

        IngestionConfig config;
        config.tenant_id = BLAKE3Pipeline::hash("default-tenant");
        config.user_id = BLAKE3Pipeline::hash("default-user");

        TextIngester ingester(db, config);
        Timer timer;

        IngestionStats stats;
        if (std::string(argv[1]) == "file" && argc >= 3) {
            stats = ingester.ingest_file(argv[2]);
        } else {
            stats = ingester.ingest(argv[1]);
        }

        std::cout << "\n✓ Ingestion complete in " << timer.elapsed_sec() << "s\n";
        return 0;
    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << "\n";
        return 1;
    }
}