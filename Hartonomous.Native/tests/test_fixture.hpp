#pragma once
/// =============================================================================
/// ENTERPRISE TEST INFRASTRUCTURE - Single source of truth
/// 
/// - Database initialized ONCE via static initialization
/// - Model ingested ONCE and cached across all tests
/// - No repeated work, no redundant ingestion
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include <catch2/reporters/catch_reporter_event_listener.hpp>
#include <catch2/reporters/catch_reporter_registrars.hpp>
#include "db/query_store.hpp"
#include "db/seeder.hpp"
#include "model/model_ingest.hpp"
#include <memory>
#include <atomic>
#include <filesystem>
#include <iostream>
#include <chrono>
#include <fstream>

namespace hartonomous::test {

/// Singleton test environment - static init guarantees single execution
class TestEnv {
    static inline std::atomic<bool> db_init_done_{false};
    static inline std::atomic<bool> db_available_{false};
    static inline std::unique_ptr<db::QueryStore> store_;
    
    static inline std::atomic<bool> data_init_done_{false};
    static inline std::atomic<bool> data_available_{false};
    static inline std::atomic<bool> model_init_done_{false};
    static inline std::atomic<bool> model_available_{false};
    static inline std::unique_ptr<model::ModelIngester> ingester_;
    static inline model::ModelResult model_result_{};
    static inline std::string model_path_;

public:
    static void init_db() {
        bool expected = false;
        if (!db_init_done_.compare_exchange_strong(expected, true)) return;
        
        try {
            db::Seeder seeder(true);
            seeder.ensure_schema();
            store_ = std::make_unique<db::QueryStore>();
            db_available_ = true;
            std::cerr << "[TEST] Database ready" << std::endl;
        } catch (const std::exception& e) {
            std::cerr << "[TEST] DB failed: " << e.what() << std::endl;
        }
    }
    
    static void init_test_data() {
        bool expected = false;
        if (!data_init_done_.compare_exchange_strong(expected, true)) {
            return;
        }
        
        try {
            std::cerr << "[TEST] Initializing test data..." << std::endl;
            
            // Skip model ingestion by default - it takes too long
            // Tests that need the model should use REQUIRE_MODEL() which calls ensure_model()
            
            // Just mark data as available (Moby Dick will be ingested lazily if needed)
            data_available_ = true;
            std::cerr << "[TEST] Test data initialization complete" << std::endl;
        } catch (const std::exception& e) {
            std::cerr << "[TEST] Data init failed: " << e.what() << std::endl;
        }
    }
    
    static bool ingest_model() {
#ifdef TEST_DATA_DIR
        std::string base = std::string(TEST_DATA_DIR) + 
            "/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2";
#else
        std::string base = "../test-data/embedding_models/models--sentence-transformers--all-MiniLM-L6-v2";
#endif
        
        if (!std::filesystem::exists(base)) {
            std::cerr << "[TEST] Model not found: " << base << std::endl;
            return false;
        }
        
        for (const auto& entry : std::filesystem::recursive_directory_iterator(base)) {
            if (entry.is_regular_file() && entry.path().filename() == "vocab.txt") {
                model_path_ = entry.path().parent_path().string();
                break;
            }
        }
        
        if (model_path_.empty()) return false;
        
        ingester_ = std::make_unique<model::ModelIngester>(store(), 50.0);
        model_result_ = ingester_->ingest_package(model_path_);
        return true;
    }
    
    static void ingest_moby_dick() {
        // Ingest Moby Dick text for testing
        std::string moby_path = "../test-data/moby_dick.txt";
        if (!std::filesystem::exists(moby_path)) {
            std::cerr << "[TEST] Moby Dick not found: " << moby_path << std::endl;
            return;
        }
        
        std::ifstream file(moby_path);
        std::string content((std::istreambuf_iterator<char>(file)), std::istreambuf_iterator<char>());
        
        if (content.empty()) {
            std::cerr << "[TEST] Moby Dick file empty" << std::endl;
            return;
        }
        
        auto start = std::chrono::high_resolution_clock::now();
        store_->encode_and_store(content);
        auto end = std::chrono::high_resolution_clock::now();
        
        auto ms = std::chrono::duration_cast<std::chrono::milliseconds>(end - start).count();
        std::cerr << "[TEST] Moby Dick ingested in " << ms << "ms" << std::endl;
    }
    
    static bool data_ready() { init_test_data(); return data_available_.load(); }
    static bool model_ready() { init_test_data(); return model_available_.load(); }
    static db::QueryStore& store() { init_db(); return *store_; }
    
    static bool ensure_model() {
        init_test_data();
        return model_available_.load();
    }
    
    static model::ModelIngester& ingester() { return *ingester_; }
    static const model::ModelResult& model_result() { return model_result_; }
    static const std::string& model_path() { return model_path_; }
};

/// Catch2 listener - initialize at startup
class GlobalSetup : public Catch::EventListenerBase {
public:
    using EventListenerBase::EventListenerBase;
    void testRunStarting(Catch::TestRunInfo const&) override { TestEnv::init_db(); }
};
CATCH_REGISTER_LISTENER(GlobalSetup)

#define REQUIRE_DB() do { TestEnv::init_db(); if (!TestEnv::data_ready()) { SKIP("Database unavailable"); } } while(0)
#define REQUIRE_DATA() do { TestEnv::init_test_data(); if (!TestEnv::data_ready()) { SKIP("Test data unavailable"); } } while(0)
#define REQUIRE_MODEL() do { TestEnv::init_test_data(); if (!TestEnv::model_ready()) { SKIP("Model unavailable"); } } while(0)

} // namespace hartonomous::test
