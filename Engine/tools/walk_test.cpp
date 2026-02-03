/**
 * @file walk_test.cpp
 * @brief Simple CLI to demonstrate generative walking
 */

#include <cognitive/walk_engine.hpp>
#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <ingestion/text_ingester.hpp> // For stats struct if needed
#include <iostream>
#include <iomanip>

using namespace Hartonomous;

int main(int argc, char** argv) {
    try {
        std::cout << "Initializing Generative Walk..." << std::endl;
        PostgresConnection db;
        WalkEngine engine(db);

        // 1. Find a starting seed
        std::string seed_word = (argc > 1) ? argv[1] : "whale";
        std::cout << "Searching for seed: '" << seed_word << "'" << std::endl;

        BLAKE3Pipeline::Hash start_id;
        std::string start_hex;
        bool found = false;
        
        if (argc == 1) {
            // Default to 'whale' ID found via psql
            start_hex = "09498610-15ea-d38a-4389-5683e43df07a";
            start_id = BLAKE3Pipeline::from_hex(start_hex);
            found = true;
        } else {
            // ... (rest of search logic)
        }

        if (!found) {
            // Grab a high-rating composition to start
            std::string sql = R"(
                SELECT c.id 
                FROM hartonomous.composition c
                JOIN hartonomous.relationsequence rs ON c.id = rs.compositionid
                JOIN hartonomous.relationrating rr ON rs.relationid = rr.relationid
                ORDER BY rr.ratingvalue DESC 
                LIMIT 1
            )";

            db.query(sql, {}, [&](const std::vector<std::string>& row) {
                start_id = BLAKE3Pipeline::from_hex(row[0]);
                start_hex = row[0];
                found = true;
            });
        }

        if (!found) {
            // Fallback to any composition
            db.query("SELECT id FROM hartonomous.composition LIMIT 1", {}, 
                [&](const std::vector<std::string>& row) {
                    start_id = BLAKE3Pipeline::from_hex(row[0]);
                    start_hex = row[0];
                    found = true;
                });
        }

        if (!found) {
            std::cerr << "No compositions found in database!" << std::endl;
            return 1;
        }

        std::cout << "Starting walk from composition: " << start_hex << "..." << std::endl;

        // 2. Initialize Walk
        auto state = engine.init_walk(start_id, 1.0);
        
        WalkParameters params;
        params.base_temp = 0.4;
        params.energy_alpha = 0.6;
        params.energy_decay = 0.05; // 20 steps max
        
        // 3. Step
        std::cout << "\n--- Trajectory ---" << std::endl;
        for (int i = 0; i < 20; ++i) {
            auto result = engine.step(state, params);
            
            std::string hex_id = BLAKE3Pipeline::to_hex(result.next_composition);
            
            // Look up text for this composition
            // Compositions are sequences of atoms (codepoints)
            std::u32string text;
            std::string sql = R"(
                SELECT a.codepoint
                FROM hartonomous.atom a
                JOIN hartonomous.compositionsequence cs ON a.id = cs.atomid
                WHERE cs.compositionid = $1
                ORDER BY cs.ordinal ASC
            )";
            
            db.query(sql, {hex_id}, [&](const std::vector<std::string>& row) {
                text.push_back(static_cast<char32_t>(std::stoul(row[0])));
            });

            // Convert UTF32 to UTF8 for printing
            std::string utf8;
            for (char32_t cp : text) {
                if (cp < 0x80) utf8.push_back(static_cast<char>(cp));
                else if (cp < 0x800) { utf8.push_back(0xC0 | (cp >> 6)); utf8.push_back(0x80 | (cp & 0x3F)); }
                else if (cp < 0x10000) { utf8.push_back(0xE0 | (cp >> 12)); utf8.push_back(0x80 | ((cp >> 6) & 0x3F)); utf8.push_back(0x80 | (cp & 0x3F)); }
                else { utf8.push_back(0xF0 | (cp >> 18)); utf8.push_back(0x80 | ((cp >> 12) & 0x3F)); utf8.push_back(0x80 | ((cp >> 6) & 0x3F)); utf8.push_back(0x80 | (cp & 0x3F)); }
            }

            std::cout << "Step " << std::setw(2) << (i+1) << ": " 
                      << std::setw(15) << std::left << utf8 
                      << " (E=" << std::fixed << std::setprecision(2) << result.energy_remaining << ")" 
                      << std::endl;

            if (result.terminated) {
                std::cout << "Terminated: " << result.reason << std::endl;
                break;
            }

            // Optional: Print some candidate info for debugging
            // (Would need to expose get_candidates or similar)
        }
        std::cout << "------------------" << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
    return 0;
}
