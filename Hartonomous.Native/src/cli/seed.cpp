/// Hartonomous Database Seeder - IDEMPOTENT
/// Ensures schema exists + seeds only missing Unicode atoms
/// Requires: HARTONOMOUS_DB_URL environment variable
/// Usage: hartonomous-seed [--quiet]

#include "db/seeder.hpp"
#include <iostream>
#include <chrono>
#include <cstring>

int main(int argc, char** argv) {
    bool quiet = false;
    for (int i = 1; i < argc; ++i) {
        if (std::strcmp(argv[i], "--quiet") == 0 || std::strcmp(argv[i], "-q") == 0) {
            quiet = true;
        } else if (std::strcmp(argv[i], "--help") == 0 || std::strcmp(argv[i], "-h") == 0) {
            std::cout << "Usage: " << argv[0] << " [--quiet]\n"
                      << "IDEMPOTENT: Only inserts missing atoms, preserves existing data.\n"
                      << "Requires HARTONOMOUS_DB_URL environment variable.\n"
                      << "Example: HARTONOMOUS_DB_URL=postgresql://user:pass@localhost/hartonomous\n";
            return 0;
        }
    }

    auto total_start = std::chrono::steady_clock::now();

    try {
        if (!quiet) {
            std::cout << "Hartonomous Database Seeder (IDEMPOTENT)\n";
            std::cout << "========================================\n";
        }

        hartonomous::db::Seeder seeder(quiet);

        // Ensure schema exists (CREATE IF NOT EXISTS)
        seeder.ensure_schema();

        // Seed atoms idempotently (ON CONFLICT DO NOTHING)
        auto [total, inserted] = seeder.seed_unicode_atoms_idempotent();

        auto total_end = std::chrono::steady_clock::now();
        double total_elapsed = std::chrono::duration<double>(total_end - total_start).count();

        if (!quiet) {
            std::cout << "\n=== SEEDING COMPLETE ===\n";
            std::cout << "Total atoms:   " << total << "\n";
            std::cout << "New inserts:   " << inserted << "\n";
            std::cout << "Total time:    " << total_elapsed << "s\n";
        }

        return 0;

    } catch (const std::exception& e) {
        std::cerr << "ERROR: " << e.what() << "\n";
        return 1;
    }
}
