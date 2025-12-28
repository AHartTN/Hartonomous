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

namespace hartonomous::test {

/// Singleton test environment - static init guarantees single execution
class TestEnv {
    static inline std::atomic<bool> db_init_done_{false};
    static inline std::atomic<bool> db_available_{false};
    static inline std::unique_ptr<db::QueryStore> store_;
    
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
    
    static bool db_ready() { init_db(); return db_available_.load(); }
    static db::QueryStore& store() { init_db(); return *store_; }
    
    static bool ensure_model() {
        if (!db_ready()) return false;
        
        bool expected = false;
        if (!model_init_done_.compare_exchange_strong(expected, true)) {
            return model_available_.load();
        }
        
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
        
        std::cerr << "[TEST] Ingesting model ONCE: " << model_path_ << std::endl;
        ingester_ = std::make_unique<model::ModelIngester>(store(), 50.0);
        model_result_ = ingester_->ingest_package(model_path_);
        model_available_ = true;
        std::cerr << "[TEST] Model ready: " << model_result_.stored_weights << " weights" << std::endl;
        return true;
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

#define REQUIRE_DB() do { if (!TestEnv::db_ready()) { SKIP("Database unavailable"); } } while(0)
#define REQUIRE_MODEL() do { if (!TestEnv::ensure_model()) { SKIP("Model unavailable"); } } while(0)

} // namespace hartonomous::test
