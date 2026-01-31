/**
 * @file seed_unicode.cpp
 * @brief High-performance bulk seeder for Unicode Atom/Physicality (S³ projection)
 */

#include <hashing/blake3_pipeline.hpp>
#include <unicode/codepoint_projection.hpp>
#include <database/postgres_connection.hpp>
#include <geometry/super_fibonacci.hpp>
#include <iostream>
#include <vector>
#include <thread>
#include <mutex>
#include <atomic>
#include <iomanip>
#include <sstream>
#include <endian.h>

using namespace Hartonomous;
using namespace hartonomous::unicode;

// Configuration
constexpr int NUM_THREADS = 16;
constexpr uint32_t TOTAL_CODEPOINTS = 1114112; // U+0000 to U+10FFFF
constexpr int BATCH_SIZE = 10000;

// Shared Stats
std::atomic<uint32_t> g_processed_count{0};

// Helper for UUID formatting
template<typename HashType>
std::string hash_to_uuid(const HashType& hash) {
    static const char hex_chars[] = "0123456789abcdef";
    std::string uuid;
    uuid.reserve(36);
    for (int i = 0; i < 16; ++i) {
        if (i == 4 || i == 6 || i == 8 || i == 10) uuid += '-';
        uuid += hex_chars[(hash[i] >> 4) & 0xF];
        uuid += hex_chars[hash[i] & 0xF];
    }
    return uuid;
}

// Worker Function
void worker_thread(uint32_t start_cp, uint32_t end_cp, PostgresConnection* db) {
    // Local buffers to reduce lock contention
    std::stringstream phys_sql;
    std::stringstream atom_sql;
    
    // Pre-allocate decent size
    phys_sql.str().reserve(BATCH_SIZE * 200);
    atom_sql.str().reserve(BATCH_SIZE * 200);

    phys_sql << "INSERT INTO Physicality (Id, Hilbert, Centroid) VALUES ";
    atom_sql << "INSERT INTO Atom (Id, Codepoint, PhysicalityId) VALUES ";

    bool first = true;
    int count = 0;

    for (uint32_t cp = start_cp; cp < end_cp; ++cp) {
        // 1. Math (Heavy Lift)
        auto result = CodepointProjection::project(static_cast<char32_t>(cp));

        // 2. IDs
        std::vector<uint8_t> phys_hash_bytes(4 * sizeof(double));
        std::memcpy(phys_hash_bytes.data(), result.s3_position.data(), 4 * sizeof(double));
        auto phys_hash = BLAKE3Pipeline::hash(phys_hash_bytes);
        std::string phys_uuid = hash_to_uuid(phys_hash);

        std::string atom_uuid = hash_to_uuid(result.hash);

        // 3. Buffer SQL
        if (!first) {
            phys_sql << ",";
            atom_sql << ",";
        }
        first = false;

        uint64_t hilbert_hi = htobe64(result.hilbert_index.hi);
        uint64_t hilbert_lo = htobe64(result.hilbert_index.lo);
        uint32_t codepoint_be = htobe32(cp);

        phys_sql << "('" << phys_uuid << "',E'\\\\x";
        phys_sql << std::hex << std::setfill('0');
        phys_sql << std::setw(16) << hilbert_hi << std::setw(16) << hilbert_lo;
        phys_sql << std::dec << "',ST_GeomFromText('POINT ZM("
                 << result.s3_position[0] << " " << result.s3_position[1] << " "
                 << result.s3_position[2] << " " << result.s3_position[3] << ")',0))";

        atom_sql << "('" << atom_uuid << "',E'\\\\x" << std::hex << std::setfill('0')
                 << std::setw(8) << codepoint_be << std::dec << "','" << phys_uuid << "')";

        count++;
        g_processed_count++;

        // Flush Batch
        if (count >= BATCH_SIZE) {
            phys_sql << " ON CONFLICT (Id) DO NOTHING";
            atom_sql << " ON CONFLICT (Id) DO NOTHING";
            
            // Execute (Locking DB access if sharing connection, but here we use per-thread logic if possible. 
            // PostgresConnection is not thread-safe usually. 
            // In this main(), we create one DB connection per thread ideally, OR lock.)
            // Assuming `db` is thread-safe OR we lock. 
            // Since libpq connection is NOT thread-safe, we MUST have 1 connection per thread.
            
            try {
                // Ensure Physicality exists before Atom
                db->execute(phys_sql.str());
                db->execute(atom_sql.str());
            } catch (const std::exception& e) {
                std::cerr << "Batch Error: " << e.what() << "\n";
            }

            // Reset
            phys_sql.str(""); 
            atom_sql.str("");
            phys_sql << "INSERT INTO Physicality (Id, Hilbert, Centroid) VALUES ";
            atom_sql << "INSERT INTO Atom (Id, Codepoint, PhysicalityId) VALUES ";
            first = true;
            count = 0;
        }
    }

    // Flush remaining
    if (count > 0) {
        phys_sql << " ON CONFLICT (Id) DO NOTHING";
        atom_sql << " ON CONFLICT (Id) DO NOTHING";
        db->execute(phys_sql.str());
        db->execute(atom_sql.str());
    }
}

int main() {
    try {
        std::cout << "Seeding " << TOTAL_CODEPOINTS << " Unicode codepoints on " << NUM_THREADS << " threads.\n";

        // Create threads
        std::vector<std::thread> threads;
        std::vector<std::unique_ptr<PostgresConnection>> connections;

        uint32_t chunk_size = TOTAL_CODEPOINTS / NUM_THREADS;
        
        auto start_time = std::chrono::high_resolution_clock::now();

        for (int i = 0; i < NUM_THREADS; ++i) {
            // Create dedicated connection for this thread
            connections.push_back(std::make_unique<PostgresConnection>());
            
            uint32_t start = i * chunk_size;
            uint32_t end = (i == NUM_THREADS - 1) ? TOTAL_CODEPOINTS : (start + chunk_size);

            threads.emplace_back(worker_thread, start, end, connections.back().get());
        }

        // Wait for threads
        for (auto& t : threads) {
            t.join();
        }

        auto end_time = std::chrono::high_resolution_clock::now();
        auto duration = std::chrono::duration_cast<std::chrono::seconds>(end_time - start_time).count();

        std::cout << "\n✓ DONE. Seeded " << TOTAL_CODEPOINTS << " atoms in " << duration << "s.\n";
        return 0;

    } catch (const std::exception& e) {
        std::cerr << "FATAL: " << e.what() << "\n";
        return 1;
    }
}