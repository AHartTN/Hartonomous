/**
 * @file seed_unicode.cpp
 * @brief Bulk seed ALL Unicode codepoints into atoms table
 */

#include <hashing/blake3_pipeline.hpp>
#include <unicode/codepoint_projection.hpp>
#include <database/postgres_connection.hpp>
#include <iostream>
#include <vector>
#include <chrono>

using namespace Hartonomous;
using namespace hartonomous::unicode;

constexpr int BATCH_SIZE = 1000;  // Safe for PostgreSQL parameter limit
constexpr int TOTAL_CODEPOINTS = 1114112;

int main() {
    try {
        std::cout << "Seeding " << TOTAL_CODEPOINTS << " Unicode codepoints (batch size: " << BATCH_SIZE << ")\n";

        PostgresConnection db;
        std::cout << "Connected to database\n";

        auto start = std::chrono::high_resolution_clock::now();
        int total = 0;

        for (int batch_start = 0; batch_start < TOTAL_CODEPOINTS; batch_start += BATCH_SIZE) {
            int batch_end = std::min(batch_start + BATCH_SIZE, TOTAL_CODEPOINTS);

            std::ostringstream sql;
            sql << "INSERT INTO atoms (hash, codepoint, s3_x, s3_y, s3_z, s3_w, s2_x, s2_y, s2_z, "
                << "hypercube_x, hypercube_y, hypercube_z, hypercube_w, hilbert_index, category) VALUES ";

            for (int i = batch_start; i < batch_end; i++) {
                auto result = CodepointProjection::project(static_cast<char32_t>(i));

                if (i > batch_start) sql << ",";

                sql << "(decode('";
                for (uint8_t byte : result.hash) {
                    char buf[3];
                    snprintf(buf, 3, "%02x", byte);
                    sql << buf;
                }
                sql << "','hex')," << result.codepoint << ","
                    << result.s3_position[0] << "," << result.s3_position[1] << ","
                    << result.s3_position[2] << "," << result.s3_position[3] << ","
                    << "0.0,0.0,0.0,0.5,0.5,0.5,0.5," << result.hilbert_index << ",'unicode')";
            }

            sql << " ON CONFLICT (hash) DO NOTHING";

            db.execute(sql.str());
            total += (batch_end - batch_start);

            if (total % 10000 == 0) {
                std::cout << "Seeded " << total << "/" << TOTAL_CODEPOINTS << " codepoints\n";
            }
        }

        auto end = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::seconds>(end - start).count();

        std::cout << "\n✓ Seeded " << total << " codepoints in " << duration << "s\n";
        return 0;

    } catch (const std::exception& e) {
        std::cerr << "ERROR: " << e.what() << "\n";
        return 1;
    }
}
