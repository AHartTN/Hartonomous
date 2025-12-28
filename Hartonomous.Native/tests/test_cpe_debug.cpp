/// Debug CPE - compute hashes and query DB directly
#include <catch2/catch_test_macros.hpp>
#include "test_fixture.hpp"
#include "db/query_store.hpp"
#include <iostream>

using namespace hartonomous;
using namespace hartonomous::test;
using namespace hartonomous::db;

TEST_CASE("DEBUG: CPE hash for Captain Ahab", "[debug][cpe]") {
    REQUIRE(TestEnv::db_ready());
    auto& store = TestEnv::store();
    
    std::string text = "Captain Ahab";
    
    std::cerr << "\n===== CPE DEBUG for '" << text << "' =====\n\n";
    
    // Test 1: compute_root gives us the hash
    auto root = store.compute_root(text);
    std::cerr << "compute_root(\"" << text << "\") = " << root.id_high << ":" << root.id_low << "\n";
    
    // Test 2: Does it exist?
    bool exists_before = store.exists(root);
    std::cerr << "exists() before encode_and_store = " << (exists_before ? "YES" : "NO") << "\n";
    
    // Test 3: find_content result
    auto result = store.find_content(text);
    std::cerr << "find_content result: exists=" << result.exists 
              << " root=" << result.root.id_high << ":" << result.root.id_low << "\n";
    
    // Test 4: encode_and_store then check again
    auto stored_root = store.encode_and_store(text);
    std::cerr << "encode_and_store result: " << stored_root.id_high << ":" << stored_root.id_low << "\n";
    
    // Test 5: Check if it exists now
    bool exists_after = store.exists(stored_root);
    std::cerr << "exists() after encode_and_store = " << (exists_after ? "YES" : "NO") << "\n";
    
    // Test 6: find_content again
    auto result2 = store.find_content(text);
    std::cerr << "find_content after store: exists=" << result2.exists << "\n";
    
    // Test 7: Check individual characters
    std::cerr << "\n--- Testing individual characters ---\n";
    for (char c : text) {
        std::string single(1, c);
        auto char_root = store.compute_root(single);
        bool char_exists = store.exists(char_root);
        std::cerr << "  '" << c << "' = " << char_root.id_high << ":" << char_root.id_low 
                  << " exists=" << (char_exists ? "YES" : "NO") << "\n";
    }
    
    // Test 8: Test "Ca" specifically
    std::cerr << "\n--- Testing 'Ca' ---\n";
    auto ca_root = store.compute_root("Ca");
    bool ca_exists = store.exists(ca_root);
    std::cerr << "compute_root(\"Ca\") = " << ca_root.id_high << ":" << ca_root.id_low 
              << " exists=" << (ca_exists ? "YES" : "NO") << "\n";
    
    // Store just "Ca" and see
    auto ca_stored = store.encode_and_store("Ca");
    std::cerr << "encode_and_store(\"Ca\") = " << ca_stored.id_high << ":" << ca_stored.id_low << "\n";
    
    bool ca_exists_after = store.exists(ca_stored);
    std::cerr << "exists() after storing \"Ca\" = " << (ca_exists_after ? "YES" : "NO") << "\n";
    
    std::cerr << "\n===== END DEBUG =====\n\n";
    
    REQUIRE(true);  // This test is for output, not assertions
}
