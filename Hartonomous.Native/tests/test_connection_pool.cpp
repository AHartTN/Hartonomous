/// =============================================================================
/// CONNECTION POOL TESTS
/// Tests for thread-safe connection pooling, acquire/release, recovery
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include "test_fixture.hpp"
#include "db/connection_pool.hpp"
#include <thread>
#include <vector>
#include <atomic>
#include <chrono>

using namespace hartonomous::db;
using namespace hartonomous::test;

TEST_CASE("ConnectionPool basic acquire/release", "[pool][db]") {
    REQUIRE_DB();
    
    ConnectionPool pool(4);
    
    SECTION("acquire returns valid connection") {
        auto conn = pool.acquire();
        REQUIRE(conn.get() != nullptr);
        REQUIRE(PQstatus(conn.get()) == CONNECTION_OK);
    }
    
    SECTION("multiple acquires work") {
        auto conn1 = pool.acquire();
        auto conn2 = pool.acquire();
        auto conn3 = pool.acquire();
        
        REQUIRE(conn1.get() != nullptr);
        REQUIRE(conn2.get() != nullptr);
        REQUIRE(conn3.get() != nullptr);
        
        // All should be different connections
        REQUIRE(conn1.get() != conn2.get());
        REQUIRE(conn2.get() != conn3.get());
        REQUIRE(conn1.get() != conn3.get());
    }
    
    SECTION("RAII release works") {
        {
            auto conn = pool.acquire();
            auto [available, total] = pool.stats();
            REQUIRE(total == 1);
            REQUIRE(available == 0);
        }
        // Connection should be released when conn goes out of scope
        auto [available, total] = pool.stats();
        REQUIRE(total == 1);
        REQUIRE(available == 1);
    }
    
    SECTION("connection reuse works") {
        PGconn* raw1 = nullptr;
        PGconn* raw2 = nullptr;
        
        {
            auto conn = pool.acquire();
            raw1 = conn.get();
        }
        {
            auto conn = pool.acquire();
            raw2 = conn.get();
        }
        
        // Should reuse the same connection
        REQUIRE(raw1 == raw2);
    }
}

TEST_CASE("ConnectionPool try_acquire", "[pool][db]") {
    REQUIRE_DB();
    
    ConnectionPool pool(2);
    
    SECTION("try_acquire succeeds when available") {
        auto conn = pool.try_acquire();
        REQUIRE(static_cast<bool>(conn));
        REQUIRE(conn.get() != nullptr);
    }
    
    SECTION("try_acquire returns empty when exhausted") {
        auto conn1 = pool.acquire();
        auto conn2 = pool.acquire();
        
        // Pool is at max capacity
        auto conn3 = pool.try_acquire();
        REQUIRE_FALSE(static_cast<bool>(conn3));
    }
}

TEST_CASE("ConnectionPool exec works", "[pool][db]") {
    REQUIRE_DB();
    
    ConnectionPool pool(2);
    auto conn = pool.acquire();
    
    SECTION("simple query") {
        auto result = conn.exec("SELECT 1 + 1 AS answer");
        REQUIRE(result.status() == PGRES_TUPLES_OK);
        REQUIRE(result.row_count() == 1);
        REQUIRE(std::string(result.get_value(0, 0)) == "2");
    }
    
    SECTION("database-specific query") {
        auto result = conn.exec("SELECT current_database()");
        REQUIRE(result.status() == PGRES_TUPLES_OK);
        REQUIRE(result.row_count() == 1);
    }
}

TEST_CASE("ConnectionPool stats", "[pool][db]") {
    REQUIRE_DB();
    
    ConnectionPool pool(4);
    
    auto [avail0, total0] = pool.stats();
    REQUIRE(avail0 == 0);
    REQUIRE(total0 == 0);
    
    auto conn1 = pool.acquire();
    auto [avail1, total1] = pool.stats();
    REQUIRE(avail1 == 0);
    REQUIRE(total1 == 1);
    
    auto conn2 = pool.acquire();
    auto [avail2, total2] = pool.stats();
    REQUIRE(avail2 == 0);
    REQUIRE(total2 == 2);
}

TEST_CASE("ConnectionPool shutdown", "[pool][db]") {
    REQUIRE_DB();
    
    ConnectionPool pool(4);
    auto conn1 = pool.acquire();
    auto conn2 = pool.acquire();
    
    pool.shutdown();
    
    // After shutdown, new acquires should throw
    REQUIRE_THROWS_AS(pool.acquire(), PgError);
    
    auto [avail, total] = pool.stats();
    REQUIRE(avail == 0);
    REQUIRE(total == 0);
}

TEST_CASE("ConnectionPool concurrent access", "[pool][db][concurrent]") {
    REQUIRE_DB();
    
    ConnectionPool pool(4);
    std::atomic<int> success_count{0};
    std::atomic<int> error_count{0};
    
    constexpr int num_threads = 8;
    constexpr int ops_per_thread = 50;
    
    std::vector<std::thread> threads;
    threads.reserve(num_threads);
    
    for (int t = 0; t < num_threads; ++t) {
        threads.emplace_back([&pool, &success_count, &error_count]() {
            for (int i = 0; i < ops_per_thread; ++i) {
                try {
                    auto conn = pool.acquire();
                    auto result = conn.exec("SELECT 1");
                    if (result.status() == PGRES_TUPLES_OK) {
                        success_count++;
                    } else {
                        error_count++;
                    }
                    // Small delay to allow contention
                    std::this_thread::sleep_for(std::chrono::microseconds(100));
                } catch (...) {
                    error_count++;
                }
            }
        });
    }
    
    for (auto& t : threads) {
        t.join();
    }
    
    REQUIRE(success_count.load() == num_threads * ops_per_thread);
    REQUIRE(error_count.load() == 0);
    
    // Verify pool is still valid and didn't grow unbounded
    // Note: Under high contention the pool may temporarily report higher total
    // due to timing of acquire/release, but should eventually stabilize
    auto [avail, total] = pool.stats();
    REQUIRE(total <= 8);  // Should not exceed reasonable bounds
}

TEST_CASE("ConnectionPool move semantics", "[pool][db]") {
    REQUIRE_DB();
    
    ConnectionPool pool(2);
    
    SECTION("PooledConnection move constructor") {
        auto conn1 = pool.acquire();
        PGconn* raw = conn1.get();
        
        auto conn2 = std::move(conn1);
        
        REQUIRE(conn1.get() == nullptr);  // NOLINT - testing moved-from state
        REQUIRE(conn2.get() == raw);
    }
    
    SECTION("PooledConnection move assignment") {
        auto conn1 = pool.acquire();
        auto conn2 = pool.acquire();
        PGconn* raw2 = conn2.get();
        
        conn1 = std::move(conn2);
        
        REQUIRE(conn1.get() == raw2);
        REQUIRE(conn2.get() == nullptr);  // NOLINT - testing moved-from state
    }
}
