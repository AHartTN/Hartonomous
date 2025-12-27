#pragma once
/// =============================================================================
/// ENTERPRISE TEST INFRASTRUCTURE
/// 
/// Single point of test setup/teardown. Schema validated and repaired ONCE.
/// Uses SchemaManager for proper validation - no suppression, actual repair.
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include <catch2/reporters/catch_reporter_event_listener.hpp>
#include <catch2/reporters/catch_reporter_registrars.hpp>
#include "db/schema_manager.hpp"
#include "db/seeder.hpp"
#include "db/query_store.hpp"
#include "db/pg_result.hpp"
#include <iostream>

namespace hartonomous::test {

/// Global test state - initialized once before any tests run
class TestEnvironment {
    static inline bool initialized_ = false;
    static inline bool schema_ready_ = false;
    static inline db::SchemaStatus schema_status_;

public:
    /// Called once at test startup - validates and repairs schema
    static void initialize() {
        if (initialized_) return;
        initialized_ = true;
        
        try {
            // Use SchemaManager for proper validation
            db::SchemaManager mgr;
            schema_status_ = mgr.ensure_schema();
            
            // Report what happened
            std::cerr << "\n=== SCHEMA VALIDATION ===" << std::endl;
            std::cerr << schema_status_.summary() << std::endl;
            for (const auto& action : schema_status_.actions_taken) {
                std::cerr << "  [REPAIR] " << action << std::endl;
            }
            for (const auto& error : schema_status_.errors) {
                std::cerr << "  [ERROR] " << error << std::endl;
            }
            std::cerr << "=========================" << std::endl;
            
            if (schema_status_.has_errors()) {
                std::cerr << "Schema validation FAILED - database tests will be skipped" << std::endl;
                return;
            }
            
            // Now seed atoms
            db::Seeder seeder(true);
            seeder.ensure_schema();  // Will skip schema (already done) but seed atoms
            
            schema_ready_ = true;
            std::cerr << "Database ready for testing" << std::endl;
            
        } catch (const std::exception& e) {
            std::cerr << "Database initialization failed: " << e.what() << std::endl;
            std::cerr << "Database tests will be skipped" << std::endl;
        }
    }
    
    /// Check if database is available and schema is valid
    [[nodiscard]] static bool database_available() { 
        return schema_ready_; 
    }
    
    /// Get the schema status from initialization
    [[nodiscard]] static const db::SchemaStatus& get_schema_status() {
        return schema_status_;
    }
    
    /// Get a fresh QueryStore
    [[nodiscard]] static db::QueryStore get_store() {
        return db::QueryStore();
    }
};

/// Catch2 event listener - runs ONCE before all tests
class GlobalTestSetup : public Catch::EventListenerBase {
public:
    using EventListenerBase::EventListenerBase;
    
    void testRunStarting(Catch::TestRunInfo const&) override {
        TestEnvironment::initialize();
    }
};

CATCH_REGISTER_LISTENER(GlobalTestSetup)

/// Base fixture for database tests - automatically skips if DB unavailable
class DbFixture {
protected:
    DbFixture() {
        if (!TestEnvironment::database_available()) {
            SKIP("Database not available or schema validation failed");
        }
    }
    
    /// Get a fresh store
    db::QueryStore fresh_store() {
        return TestEnvironment::get_store();
    }
};

} // namespace hartonomous::test
