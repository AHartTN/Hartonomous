/**
 * @file seed_unicode.cpp
 * @brief Bulk seed ALL Unicode codepoints (0 to 1,114,111) with proper BLAKE3 and S³ projection
 */

#include <hashing/blake3_pipeline.hpp>
#include <unicode/codepoint_projection.hpp>
#include <database/postgres_connection.hpp>
#include <iostream>
#include <vector>
#include <chrono>
#include <sstream>
#include <iomanip>

using namespace Hartonomous;
using namespace hartonomous::unicode;

constexpr int BATCH_SIZE = 10000;
constexpr int TOTAL_CODEPOINTS = 1114112; // 0x000000 to 0x10FFFF

void bulk_insert_batch(PostgresConnection& db, const std::vector<CodepointProjection::ProjectionResult>& batch) {
    if (batch.empty()) return;

    std::ostringstream sql;
    sql << "INSERT INTO hartonomous.atoms (hash, codepoint, centroid_x, centroid_y, centroid_z, centroid_w, centroid, hilbert_index) VALUES ";

    std::vector<std::string> params;
    params.reserve(batch.size() * 7);

    for (size_t i = 0; i < batch.size(); ++i) {
        const auto& result = batch[i];

        if (i > 0) sql << ", ";

        size_t base = i * 7;
        sql << "($" << (base + 1) << ", $" << (base + 2) << ", $" << (base + 3)
            << ", $" << (base + 4) << ", $" << (base + 5) << ", $" << (base + 6)
            << ", ST_SetSRID(ST_MakePoint($" << (base + 3) << ", $" << (base + 4)
            << ", $" << (base + 5) << ", $" << (base + 6) << "), 0), $" << (base + 7) << ")";

        // Hash as bytea
        std::string hash_hex;
        for (uint8_t byte : result.hash) {
            char buf[3];
            snprintf(buf, sizeof(buf), "%02x", byte);
            hash_hex += buf;
        }
        params.push_back("\\x" + hash_hex);

        // Codepoint
        params.push_back(std::to_string(result.codepoint));

        // S³ position (individual coords + geometry)
        params.push_back(std::to_string(result.s3_position[0]));
        params.push_back(std::to_string(result.s3_position[1]));
        params.push_back(std::to_string(result.s3_position[2]));
        params.push_back(std::to_string(result.s3_position[3]));

        // Geometry point (handled by ST_MakePoint in SQL)

        // Hilbert index
        params.push_back(std::to_string(result.hilbert_index));
    }

    sql << " ON CONFLICT (codepoint) DO NOTHING";

    db.execute(sql.str(), params);
}

int main() {
    try {
        std::cout << "========================================\n";
        std::cout << "Seeding ALL Unicode Codepoints\n";
        std::cout << "========================================\n";
        std::cout << "Total codepoints: " << TOTAL_CODEPOINTS << "\n";
        std::cout << "Batch size: " << BATCH_SIZE << "\n";
        std::cout << "Using: BLAKE3 hashing + proper S³ projection\n";
        std::cout << "\n";

        // Connect to database
        PostgresConnection db;
        std::cout << "Connected to database\n\n";

        auto start_time = std::chrono::high_resolution_clock::now();

        std::vector<CodepointProjection::ProjectionResult> batch;
        batch.reserve(BATCH_SIZE);

        int total_inserted = 0;
        int batch_num = 0;

        for (char32_t cp = 0; cp < TOTAL_CODEPOINTS; ++cp) {
            // Project codepoint to S³ with BLAKE3 hash
            auto result = CodepointProjection::project(cp);
            batch.push_back(result);

            // Insert when batch is full
            if (batch.size() >= BATCH_SIZE) {
                bulk_insert_batch(db, batch);
                total_inserted += batch.size();
                batch_num++;

                // Progress every 10 batches
                if (batch_num % 10 == 0) {
                    auto elapsed = std::chrono::high_resolution_clock::now() - start_time;
                    auto seconds = std::chrono::duration_cast<std::chrono::seconds>(elapsed).count();
                    double percent = (double)cp / TOTAL_CODEPOINTS * 100.0;

                    std::cout << "Progress: " << batch_num << " batches"
                              << " (" << std::fixed << std::setprecision(1) << percent << "%)"
                              << " - " << total_inserted << " codepoints"
                              << " - " << seconds << "s elapsed\n";
                }

                batch.clear();
            }
        }

        // Insert remaining batch
        if (!batch.empty()) {
            bulk_insert_batch(db, batch);
            total_inserted += batch.size();
        }

        auto end_time = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::seconds>(end_time - start_time);

        std::cout << "\n";
        std::cout << "========================================\n";
        std::cout << "Seeding Complete!\n";
        std::cout << "========================================\n";
        std::cout << "Total codepoints processed: " << TOTAL_CODEPOINTS << "\n";
        std::cout << "Total time: " << duration.count() << " seconds\n";
        std::cout << "\n";

        // Verify
        auto count = db.query_single("SELECT COUNT(*) FROM hartonomous.atoms");
        if (count) {
            std::cout << "Database verification: " << *count << " atoms\n";
        }

        // Statistics
        std::cout << "\nRunning ANALYZE for query optimization...\n";
        db.execute("ANALYZE hartonomous.atoms");

        std::cout << "\nDone!\n";

        return 0;

    } catch (const std::exception& e) {
        std::cerr << "ERROR: " << e.what() << "\n";
        return 1;
    }
}
