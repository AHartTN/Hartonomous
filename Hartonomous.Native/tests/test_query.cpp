/// QUERY TESTS - Prove the universal substrate actually works
///
/// These tests verify:
/// 1. Content-addressable lookup: "Captain Ahab" → root hash → exists
/// 2. Deterministic encoding: same text → same hash, always
/// 3. Spatial queries using PostGIS
/// 4. Semantic similarity via geometry
/// 5. Lossless round-trip through database

#include <catch2/catch_test_macros.hpp>
#include "db/query_store.hpp"
#include "db/schema_manager.hpp"
#include "db/seeder.hpp"
#include <chrono>

using namespace hartonomous;
using namespace hartonomous::db;

namespace {
    // Schema validated once per test run
    static bool schema_ready = false;
    
    void ensure_db_ready() {
        if (schema_ready) return;
        
        SchemaManager mgr;
        auto status = mgr.ensure_schema();
        if (status.has_errors()) {
            throw std::runtime_error("Schema validation failed: " + status.summary());
        }
        
        // Seed atoms
        Seeder seeder(true);
        seeder.ensure_schema();
        
        schema_ready = true;
    }
}

// =============================================================================
// CONTENT-ADDRESSABLE LOOKUP
// =============================================================================

TEST_CASE("Content addressing: same text → same hash", "[content][deterministic]") {
    QueryStore store;

    SECTION("Identical strings produce identical roots") {
        auto root1 = store.compute_root("Captain Ahab");
        auto root2 = store.compute_root("Captain Ahab");

        REQUIRE(root1.id_high == root2.id_high);
        REQUIRE(root1.id_low == root2.id_low);
    }

    SECTION("Different strings produce different roots") {
        auto root1 = store.compute_root("Captain Ahab");
        auto root2 = store.compute_root("Captain Ahaz");  // One char different

        REQUIRE((root1.id_high != root2.id_high || root1.id_low != root2.id_low));
    }

    SECTION("Empty string → null root") {
        auto root = store.compute_root("");
        REQUIRE(root.id_high == 0);
        REQUIRE(root.id_low == 0);
    }

    SECTION("Single byte → atom") {
        auto root = store.compute_root("X");
        REQUIRE(root.is_atom == true);
    }
}

TEST_CASE("Encode and store: content becomes queryable", "[content][store]") {
    QueryStore store;

    SECTION("Store then find") {
        // Use unique text to avoid collision with previous test runs
        auto now = std::chrono::high_resolution_clock::now().time_since_epoch().count();
        std::string text = "Captain Ahab Test " + std::to_string(now);

        // Before storing
        auto root_before = store.compute_root(text);
        bool existed_before = store.exists(root_before);

        // Store it
        auto stored_root = store.encode_and_store(text);

        // After storing - same root, now exists
        REQUIRE(stored_root.id_high == root_before.id_high);
        REQUIRE(stored_root.id_low == root_before.id_low);
        REQUIRE(store.exists(stored_root));

        // If it didn't exist before, verify store actually added it
        if (!existed_before) {
            INFO("Content was new - verified store creates entries");
        }
    }

    SECTION("Lookup returns correct children") {
        auto root = store.encode_and_store("AB");

        auto children = store.lookup(root);
        REQUIRE(children.has_value());
        REQUIRE(children->first.is_atom == true);   // 'A'
        REQUIRE(children->second.is_atom == true);  // 'B'
    }

    SECTION("Idempotent: storing twice doesn't duplicate") {
        auto count_before = store.composition_count();

        store.encode_and_store("Test content");
        auto count_after_first = store.composition_count();

        store.encode_and_store("Test content");
        auto count_after_second = store.composition_count();

        REQUIRE(count_after_second == count_after_first);
    }
}

TEST_CASE("Lossless round-trip through database", "[content][lossless]") {
    QueryStore store;

    SECTION("Simple string") {
        std::string original = "Hello, World!";
        auto root = store.encode_and_store(original);
        auto decoded = store.decode_string(root);

        REQUIRE(decoded == original);
    }

    SECTION("Binary with nulls (as Latin-1 codepoints)") {
        // Raw bytes 0x80+ are treated as Latin-1 codepoints and encoded to UTF-8
        // So we test with ASCII range which round-trips exactly
        std::vector<std::uint8_t> original = {0x00, 0x20, 0x00, 0x7F, 0x01, 0x00};
        auto root = store.encode_and_store(
            original.data(), original.size());
        auto decoded = store.decode(root);

        REQUIRE(decoded == original);
    }

    SECTION("Unicode text") {
        // C++20 char8_t handling
        const char* original = reinterpret_cast<const char*>(u8"日本語テスト 🎉");
        auto root = store.encode_and_store(original);
        auto decoded = store.decode_string(root);

        REQUIRE(decoded == original);
    }
}

TEST_CASE("Query performance: microsecond lookup", "[content][performance]") {
    QueryStore store;

    // Store test content
    std::string captain = "Captain Ahab";
    auto root = store.encode_and_store(captain);

    // Warm up
    (void)store.exists(root);

    // Time the lookup
    auto start = std::chrono::high_resolution_clock::now();

    for (int i = 0; i < 1000; ++i) {
        bool found = store.exists(root);
        REQUIRE(found);
    }

    auto end = std::chrono::high_resolution_clock::now();
    auto duration = std::chrono::duration_cast<std::chrono::microseconds>(end - start);

    double avg_us = static_cast<double>(duration.count()) / 1000.0;

    // Should be sub-millisecond, ideally under 100 microseconds
    REQUIRE(avg_us < 1000.0);

    INFO("Average lookup time: " << avg_us << " microseconds");
}

// =============================================================================
// SPATIAL QUERIES - Actually using PostGIS
// =============================================================================

TEST_CASE("Spatial query: find similar characters", "[spatial]") {
    ensure_db_ready();
    QueryStore store;

    SECTION("Find characters near 'a'") {
        auto matches = store.find_similar('a', 10);

        // Should find related characters (b, c, etc. or case variants)
        REQUIRE_FALSE(matches.empty());

        // 'a' should not be in results (excluded self)
        for (const auto& m : matches) {
            REQUIRE(m.codepoint != 'a');
        }
    }

    SECTION("Case variants share same base") {
        auto variants = store.find_case_variants('a');

        // Should find 'a' and 'A' at minimum
        bool found_lower = false;
        bool found_upper = false;

        for (const auto& v : variants) {
            if (v.codepoint == 'a') found_lower = true;
            if (v.codepoint == 'A') found_upper = true;
        }

        REQUIRE(found_lower);
        REQUIRE(found_upper);
    }
}

TEST_CASE("Spatial query: semantic proximity", "[spatial]") {
    ensure_db_ready();
    QueryStore store;

    SECTION("Letters are closer to letters than to digits") {
        auto near_a = store.find_near_codepoint('a', 500.0, 50);

        int letter_count = 0;
        int digit_count = 0;

        for (const auto& m : near_a) {
            if (m.codepoint >= 'a' && m.codepoint <= 'z') letter_count++;
            if (m.codepoint >= 'A' && m.codepoint <= 'Z') letter_count++;
            if (m.codepoint >= '0' && m.codepoint <= '9') digit_count++;
        }

        // Letters should dominate the near matches
        REQUIRE(letter_count > digit_count);
    }
}

// =============================================================================
// THE ACTUAL USE CASE: Query "Captain Ahab"
// =============================================================================

// =============================================================================
// INDEX VERIFICATION - Prove we're not doing sequential scans
// =============================================================================

TEST_CASE("Queries use proper indexes", "[db][index]") {
    ensure_db_ready();
    QueryStore store;

    SECTION("Composition lookup uses primary key index") {
        // Store something first so table isn't empty
        store.encode_and_store("test");

        bool uses_index = store.verify_composition_index_usage();
        INFO("Composition query should use Index Scan on primary key");
        REQUIRE(uses_index);
    }

    SECTION("Spatial query uses GIST index") {
        // Atoms seeded by ensure_schema() - no conditional check needed

        bool uses_index = store.verify_spatial_index_usage();
        INFO("Spatial query should use GIST index (R-tree)");
        // With 1.1M atoms, PostgreSQL WILL use the index
        REQUIRE(uses_index);
    }

    SECTION("Relationship query uses B-tree index") {
        // Create a relationship first
        auto a = store.compute_root("A");
        auto b = store.compute_root("B");
        store.store_relationship(a, b, 0.5);

        bool uses_index = store.verify_relationship_index_usage();
        INFO("Relationship query should use B-tree index on from_high, from_low");
        REQUIRE(uses_index);
    }
}

TEST_CASE("The Captain Ahab query", "[content][usecase]") {
    QueryStore store;

    // Store Moby Dick excerpt
    std::string moby_excerpt =
        "Call me Ishmael. Some years ago—never mind how long precisely—"
        "having little or no money in my purse, and nothing particular "
        "to interest me on shore, I thought I would sail about a little "
        "and see the watery part of the world. Captain Ahab was the "
        "commander of the Pequod.";

    store.encode_and_store(moby_excerpt);

    // Now query "Captain Ahab" specifically
    std::string query_text = "Captain Ahab";

    SECTION("Compute hash without storing") {
        auto root = store.compute_root(query_text);

        // The hash is deterministic
        REQUIRE(root.id_high != 0);
        REQUIRE(root.id_low != 0);
    }

    SECTION("Find Captain Ahab if stored as substring composition") {
        // Store "Captain Ahab" separately
        auto captain_root = store.encode_and_store(query_text);

        // Verify it exists
        REQUIRE(store.exists(captain_root));

        // Decode it back
        auto decoded = store.decode_string(captain_root);
        REQUIRE(decoded == query_text);
    }

    SECTION("Query is fast") {
        auto captain_root = store.encode_and_store(query_text);

        auto start = std::chrono::high_resolution_clock::now();
        bool found = store.exists(captain_root);
        auto end = std::chrono::high_resolution_clock::now();

        REQUIRE(found);

        auto duration = std::chrono::duration_cast<std::chrono::microseconds>(end - start);
        INFO("Captain Ahab lookup: " << duration.count() << " microseconds");

        // Should be under 1ms
        REQUIRE(duration.count() < 1000);
    }
}

// =============================================================================
// WEIGHTED RELATIONSHIPS - Model weights, knowledge edges
// =============================================================================

TEST_CASE("Weighted relationships", "[db][relationship]") {
    ensure_db_ready();
    QueryStore store;

    // Create some nodes
    auto hello = store.encode_and_store("hello");
    auto world = store.encode_and_store("world");
    auto foo = store.encode_and_store("foo");

    SECTION("Store and retrieve relationship") {
        // Clean slate: delete any existing relationship from prior test runs
        store.delete_relationship(hello, world);
        
        store.store_relationship(hello, world, 0.95, REL_DEFAULT);

        auto weight = store.get_weight(hello, world);
        REQUIRE(weight.has_value());
        REQUIRE(*weight == 0.95);
    }

    SECTION("Find outgoing edges") {
        store.store_relationship(hello, world, 0.8);
        store.store_relationship(hello, foo, 0.6);

        auto edges = store.find_from(hello);

        REQUIRE(edges.size() >= 2);

        // Sorted by weight descending
        REQUIRE(edges[0].weight >= edges[1].weight);
    }

    SECTION("Find incoming edges") {
        store.store_relationship(hello, world, 0.7);
        store.store_relationship(foo, world, 0.5);

        auto edges = store.find_to(world);

        REQUIRE(edges.size() >= 2);
    }

    SECTION("Find by weight range") {
        store.store_relationship(hello, world, 0.9);
        store.store_relationship(hello, foo, 0.3);

        auto high_weight = store.find_by_weight(0.8, 1.0);
        auto low_weight = store.find_by_weight(0.0, 0.5);

        // High weight should include hello→world
        bool found_high = false;
        for (const auto& r : high_weight) {
            if (r.weight >= 0.8) found_high = true;
        }
        REQUIRE(found_high);
    }

    SECTION("Bulk store model weights") {
        auto model_ctx = store.compute_root("test_model_v1");

        std::vector<std::tuple<NodeRef, NodeRef, double>> weights = {
            {hello, world, 0.95},
            {hello, foo, 0.75},
            {world, foo, 0.50},
        };

        store.store_model_weights(weights, model_ctx, REL_DEFAULT);

        auto count = store.relationship_count();
        REQUIRE(count >= 3);
    }

    SECTION("Relationship type filtering") {
        // RelType is now a uniform default - types are distinguished by context hash
        store.store_relationship(hello, world, 0.8, REL_DEFAULT);
        store.store_relationship(hello, foo, 0.7, REL_DEFAULT);

        auto edges = store.find_from(hello);

        // Both should be found
        REQUIRE(edges.size() >= 2);

        // All relationships now use REL_DEFAULT (0)
        for (const auto& e : edges) {
            REQUIRE(e.rel_type == REL_DEFAULT);
        }
    }
}
