/**
 * @file walk_test.cpp
 * @brief CLI for generative walking — prompt the substrate like an LLM
 */

#include <cognitive/walk_engine.hpp>
#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <iostream>
#include <iomanip>

using namespace Hartonomous;

int main(int argc, char** argv) {
    try {
        PostgresConnection db;
        WalkEngine engine(db);

        std::string prompt = (argc > 1) ? argv[1] : "whale";
        size_t max_steps = (argc > 2) ? std::stoul(argv[2]) : 50;

        std::cout << "Prompt: \"" << prompt << "\"" << std::endl;
        std::cout << "Max steps: " << max_steps << std::endl;
        std::cout << std::endl;

        // Method 1: Full generation (prompt → text)
        WalkParameters params;
        std::string response = engine.generate(prompt, params, max_steps);
        std::cout << "=== Generated Response ===" << std::endl;
        std::cout << response << std::endl;
        std::cout << "==========================" << std::endl;
        std::cout << std::endl;

        // Method 2: Step-by-step walk with scoring visibility
        std::cout << "=== Step-by-Step Walk ===" << std::endl;
        auto state = engine.init_walk_from_prompt(prompt, 1.0);
        
        std::string seed_text = engine.lookup_text(state.current_composition);
        std::cout << "Seed: " << seed_text << " [" << BLAKE3Pipeline::to_hex(state.current_composition).substr(0, 8) << "]" << std::endl;

        for (size_t i = 0; i < max_steps; ++i) {
            auto result = engine.step(state, params);
            if (result.terminated) {
                std::cout << "  [" << result.reason << "]" << std::endl;
                break;
            }

            std::string text = engine.lookup_text(result.next_composition);
            std::cout << "  " << std::setw(2) << (i+1) << ": " 
                      << std::setw(20) << std::left << text
                      << " p=" << std::fixed << std::setprecision(3) << result.probability
                      << " E=" << std::setprecision(2) << result.energy_remaining
                      << std::endl;
        }
        std::cout << "=========================" << std::endl;

    } catch (const std::exception& e) {
        std::cerr << "Error: " << e.what() << std::endl;
        return 1;
    }
    return 0;
}
