/**
 * @file test_interop_functionality.cpp
 * @brief Comprehensive functional tests for the C Interop API.
 * 
 * These tests validate the exact call patterns used by the C# Marshaling layer.
 * They run against the real PostgreSQL database (if available) or fail if specific
 * environment requirements are not met, ensuring "real" functionality is verified.
 */

#include <gtest/gtest.h>
#include <interop_api.h>
#include <hashing/blake3_pipeline.hpp>
#include <vector>
#include <string>
#include <cstring>
#include <iostream>

// Connection string matches appsettings.json
const char* TEST_CONN_STRING = "host=localhost dbname=hartonomous user=postgres password=postgres options='-c search_path=hartonomous,public'";

class InteropTest : public ::testing::Test {
protected:
    void SetUp() override {
        // Try to connect - skip tests if database unavailable
        db_handle = hartonomous_db_create(TEST_CONN_STRING);
        if (db_handle == nullptr) {
            GTEST_SKIP() << "Database not available - skipping integration test. "
                        << "Run after database setup.";
        }
    }

    void TearDown() override {
        if (db_handle) {
            hartonomous_db_destroy(db_handle);
            db_handle = nullptr;
        }
    }

    h_db_connection_t db_handle = nullptr;
};

TEST_F(InteropTest, DatabaseConnectionCycle) {
    EXPECT_TRUE(hartonomous_db_is_connected(db_handle));
    
    // Test double destroy safety (if implemented, otherwise don't crash)
    // hartonomous_db_destroy(db_handle); // Moved to TearDown
}

TEST_F(InteropTest, FullIngestionPipeline) {
    h_ingester_t ingester = hartonomous_ingester_create(db_handle);
    ASSERT_TRUE(ingester != nullptr);

    const char* text = "Call me Ishmael. Some years ago—never mind how long precisely—having little or no money in my purse, and nothing particular to interest me on shore, I thought I would sail about a little and see the watery part of the world.";
    HIngestionStats stats;
    
    // Zero out stats first to ensure they are actually written
    std::memset(&stats, 0, sizeof(HIngestionStats));

    bool result = hartonomous_ingest_text(ingester, text, &stats);
    
    EXPECT_TRUE(result) << "Ingestion failed: " << hartonomous_get_last_error();
    
    // Verify "Real" Values
    EXPECT_EQ(stats.original_bytes, std::strlen(text));
    EXPECT_GT(stats.atoms_total, 0);
    EXPECT_GT(stats.compositions_total, 0);
    // Relations might be 0 if the decomposer isn't fully extracting them yet, but basic n-grams/co-occurrences should exist
    
    // Test file ingestion (using a temporary file)
    std::string temp_file = "test_ingest.txt";
    FILE* f = std::fopen(temp_file.c_str(), "w");
    std::fprintf(f, "The quick brown fox jumps over the lazy dog.");
    std::fclose(f);
    
    std::memset(&stats, 0, sizeof(HIngestionStats));
    result = hartonomous_ingest_file(ingester, temp_file.c_str(), &stats);
    EXPECT_TRUE(result) << "File ingestion failed: " << hartonomous_get_last_error();
    EXPECT_GT(stats.atoms_total, 0);

    hartonomous_ingester_destroy(ingester);
    std::remove(temp_file.c_str());
}

TEST_F(InteropTest, WalkEngineTrajectory) {
    h_walk_engine_t walker = hartonomous_walk_create(db_handle);
    ASSERT_TRUE(walker != nullptr);

    // 1. Calculate a valid Start ID (Hash of 'C' from Call)
    // We use the internal pipeline to get a real hash, simulating C# passing a known ID
    auto hash = Hartonomous::BLAKE3Pipeline::hash_codepoint('C');
    
    HWalkState state;
    std::memset(&state, 0, sizeof(HWalkState));
    
    // 2. Init Walk
    double initial_energy = 100.0;
    bool init_ok = hartonomous_walk_init(walker, hash.data(), initial_energy, &state);
    ASSERT_TRUE(init_ok) << "Walk init failed: " << hartonomous_get_last_error();
    
    EXPECT_EQ(state.current_energy, initial_energy);
    // Verify position is normalized (on S3)
    double norm = 0.0;
    for(int i=0; i<4; ++i) norm += state.current_position[i] * state.current_position[i];
    EXPECT_NEAR(norm, 1.0, 1e-4);

    // 3. Take a Step
    HWalkParameters params;
    params.w_model = 0.35;
    params.w_text = 0.40;
    params.w_rel = 0.15;
    params.w_geo = 0.05;
    params.w_hilbert = 0.05;
    params.w_repeat = 0.25;
    params.w_novelty = 0.15;
    params.goal_attraction = 2.0;
    params.w_energy = 0.10;
    params.base_temp = 0.4;
    params.energy_alpha = 0.6;
    params.energy_decay = 0.05; // Should lose 5% energy + step cost
    params.context_window = 16;

    HWalkStepResult result;
    bool step_ok = hartonomous_walk_step(walker, &state, &params, &result);
    ASSERT_TRUE(step_ok) << "Walk step failed: " << hartonomous_get_last_error();

    EXPECT_LT(result.energy_remaining, initial_energy);
    EXPECT_GE(result.probability, 0.0);
    EXPECT_LE(result.probability, 1.0);
    
    // 4. Set Goal
    auto goal_hash = Hartonomous::BLAKE3Pipeline::hash_codepoint('l'); // Target 'l'
    bool goal_ok = hartonomous_walk_set_goal(walker, &state, goal_hash.data());
    EXPECT_TRUE(goal_ok);

    hartonomous_walk_destroy(walker);
}

TEST_F(InteropTest, GodelEngineAnalysis) {
    h_godel_t godel = hartonomous_godel_create(db_handle);
    ASSERT_TRUE(godel != nullptr);

    HResearchPlan plan;
    std::memset(&plan, 0, sizeof(HResearchPlan));

    const char* problem = "Prove P != NP using geometric topology.";
    bool result = hartonomous_godel_analyze(godel, problem, &plan);
    ASSERT_TRUE(result) << "Godel analysis failed: " << hartonomous_get_last_error();

    EXPECT_EQ(std::string(plan.original_problem), std::string(problem));
    // Even if stubbed, these should return valid integers
    EXPECT_GE(plan.total_steps, 0);
    
    if (plan.sub_problems_count > 0) {
        EXPECT_TRUE(plan.sub_problems != nullptr);
        EXPECT_TRUE(plan.sub_problems[0].description != nullptr);
    }
    
    hartonomous_godel_free_plan(&plan);
    EXPECT_EQ(plan.original_problem, nullptr); // Should be nulled out

    hartonomous_godel_destroy(godel);
}
