/// PAIR ENCODING TESTS
///
/// Tests for the BPE-style pair encoding engine and cascade.
/// Focused on: lossless round-trip, determinism, compression efficiency.
/// NO VERBOSE OUTPUT. NO TRIVIAL CONSTRUCTOR TESTS.

#include <catch2/catch_test_macros.hpp>
#include "atoms/pair_encoding.hpp"
#include "atoms/pair_encoding_engine.hpp"
#include "atoms/pair_encoding_cascade.hpp"
#include <string>
#include <fstream>
#include <chrono>

using namespace hartonomous;

// =============================================================================
// LOSSLESS ROUND-TRIP: The only test that truly matters
// =============================================================================

TEST_CASE("PairEncodingEngine: Lossless round-trip", "[encoding][lossless]") {
    PairEncodingEngine engine;
    
    SECTION("All byte values") {
        std::vector<std::uint8_t> input(256);
        for (int i = 0; i < 256; ++i) input[i] = static_cast<std::uint8_t>(i);
        
        auto root = engine.ingest(input.data(), input.size());
        auto decoded = engine.decode(root);
        
        REQUIRE(decoded == input);
    }
    
    SECTION("Hello World") {
        std::string input = "Hello, World!";
        auto root = engine.ingest(input);
        auto decoded = engine.decode(root);
        
        REQUIRE(std::string(decoded.begin(), decoded.end()) == input);
    }
    
    SECTION("Binary with nulls") {
        std::vector<std::uint8_t> input = {0x00, 0xFF, 0x00, 0x7F, 0x80, 0x00};
        auto root = engine.ingest(input.data(), input.size());
        auto decoded = engine.decode(root);
        
        REQUIRE(decoded == input);
    }
    
    SECTION("Large repetitive pattern") {
        std::string input;
        for (int i = 0; i < 10000; ++i) input += "abcd";
        
        auto root = engine.ingest(input);
        auto decoded = engine.decode(root);
        
        REQUIRE(std::string(decoded.begin(), decoded.end()) == input);
    }
    
    SECTION("Pseudo-random binary") {
        std::vector<std::uint8_t> input(10000);
        std::uint32_t seed = 12345;
        for (auto& b : input) {
            seed = seed * 1103515245 + 12345;
            b = static_cast<std::uint8_t>((seed >> 16) & 0xFF);
        }
        
        auto root = engine.ingest(input.data(), input.size());
        auto decoded = engine.decode(root);
        
        REQUIRE(decoded == input);
    }
}

TEST_CASE("PairEncodingCascade: Lossless round-trip", "[encoding][lossless]") {
    CompositionStore store;
    
    SECTION("All byte values") {
        std::vector<std::uint8_t> input(256);
        for (int i = 0; i < 256; ++i) input[i] = static_cast<std::uint8_t>(i);
        
        auto root = PairEncodingCascade::encode(input.data(), input.size(), store);
        auto decoded = PairEncodingCascade::decode(root, store);
        
        REQUIRE(decoded == input);
    }
    
    SECTION("Repeated patterns") {
        std::string input;
        for (int i = 0; i < 1000; ++i) input += "banana";
        
        auto root = PairEncodingCascade::encode(
            reinterpret_cast<const std::uint8_t*>(input.data()), input.size(), store);
        auto decoded = PairEncodingCascade::decode(root, store);
        
        REQUIRE(std::string(decoded.begin(), decoded.end()) == input);
    }
}

TEST_CASE("Moby Dick: Complete lossless", "[encoding][lossless][moby]") {
    std::string path = std::string(TEST_DATA_DIR) + "/moby_dick.txt";
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    
    if (!file.good()) {
        SKIP("Test data not available");
    }
    
    auto size = file.tellg();
    REQUIRE(size > 1000000);  // Must be > 1MB
    
    file.seekg(0);
    std::vector<std::uint8_t> original(static_cast<std::size_t>(size));
    file.read(reinterpret_cast<char*>(original.data()), size);
    
    PairEncodingEngine engine;
    
    auto root = engine.ingest(original.data(), original.size());
    auto decoded = engine.decode(root);
    
    // Use bool to prevent Catch2 from expanding 1.1M characters
    REQUIRE(decoded.size() == original.size());
    bool matches = (decoded == original);
    REQUIRE(matches);
}

// =============================================================================
// DETERMINISM: Same input always produces same output
// =============================================================================

TEST_CASE("Encoding is deterministic", "[encoding][deterministic]") {
    SECTION("Engine: Same input → same root") {
        PairEncodingEngine engine1, engine2;
        
        std::string input = "The quick brown fox jumps over the lazy dog.";
        auto root1 = engine1.ingest(input);
        auto root2 = engine2.ingest(input);
        
        REQUIRE(root1.id_high == root2.id_high);
        REQUIRE(root1.id_low == root2.id_low);
    }
    
    SECTION("Cascade: Same input → same root") {
        CompositionStore store1, store2;
        
        auto root1 = PairEncodingCascade::encode("banana", store1);
        auto root2 = PairEncodingCascade::encode("banana", store2);
        
        REQUIRE(root1.id_high == root2.id_high);
        REQUIRE(root1.id_low == root2.id_low);
    }
    
    SECTION("Different input → different root") {
        PairEncodingEngine engine;
        
        auto root1 = engine.ingest("cat");
        auto root2 = engine.ingest("act");
        
        REQUIRE((root1.id_high != root2.id_high || root1.id_low != root2.id_low));
    }
}

// =============================================================================
// COMPRESSION: Vocabulary learning and pattern sharing
// =============================================================================

TEST_CASE("Vocabulary learning", "[encoding][vocabulary]") {
    PairEncodingEngine::Config config;
    config.min_pair_frequency = 2;
    PairEncodingEngine engine(config);
    
    SECTION("Repeated patterns grow vocabulary") {
        std::string input;
        for (int i = 0; i < 100; ++i) input += "abcd";
        
        engine.ingest(input);
        
        REQUIRE(engine.vocabulary_size() > 0);
    }
}

TEST_CASE("Composition sharing", "[encoding][compression]") {
    CompositionStore store;
    
    SECTION("Shared substrings reuse compositions") {
        PairEncodingCascade::encode("the cat", store);
        auto count1 = store.size();
        
        PairEncodingCascade::encode("the dog", store);
        auto count2 = store.size();
        
        // Second string should reuse "the " compositions
        REQUIRE(count2 < count1 * 2);
    }
    
    SECTION("Highly repetitive data compresses well") {
        std::string input(1000, 'a');
        PairEncodingCascade::encode(input.c_str(), store);
        
        // 1000 identical chars should compress dramatically
        REQUIRE(store.size() < 20);
    }
}

// =============================================================================
// EDGE CASES
// =============================================================================

TEST_CASE("Edge cases", "[encoding][edge]") {
    PairEncodingEngine engine;
    
    SECTION("Empty input → null root") {
        auto root = engine.ingest("", 0);
        REQUIRE(root.id_high == 0);
        REQUIRE(root.id_low == 0);
    }
    
    SECTION("Single byte → atom") {
        auto root = engine.ingest("X", 1);
        REQUIRE(root.is_atom == true);
    }
    
    SECTION("Two bytes → composition") {
        auto root = engine.ingest("AB", 2);
        REQUIRE(root.is_atom == false);
    }
}

// =============================================================================
// RLE SEQUENCE (internal component)
// =============================================================================

TEST_CASE("RLESequence collapses runs", "[encoding][rle]") {
    RLESequence seq;
    NodeRef a = NodeRef::atom(AtomId{0, 'a'});
    NodeRef b = NodeRef::atom(AtomId{0, 'b'});
    
    SECTION("Consecutive identical → one entry with count") {
        seq.push(a);
        seq.push(a);
        seq.push(a);
        
        REQUIRE(seq.items.size() == 1);
        REQUIRE(seq.items[0].count == 3);
    }
    
    SECTION("Mixed → multiple entries") {
        seq.push(a);
        seq.push(a);
        seq.push(b);
        seq.push(b);
        seq.push(b);
        seq.push(a);
        
        REQUIRE(seq.items.size() == 3);
        REQUIRE(seq.items[0].count == 2);
        REQUIRE(seq.items[1].count == 3);
        REQUIRE(seq.items[2].count == 1);
    }
    
    SECTION("Expand recovers original sequence") {
        seq.push(a);
        seq.push(a);
        seq.push(b);
        seq.push(a);
        
        auto expanded = seq.expand();
        REQUIRE(expanded.size() == 4);
        REQUIRE(expanded[0] == a);
        REQUIRE(expanded[1] == a);
        REQUIRE(expanded[2] == b);
        REQUIRE(expanded[3] == a);
    }
}

// =============================================================================
// COMPOSITION STORE (internal component)
// =============================================================================

TEST_CASE("CompositionStore operations", "[encoding][store]") {
    CompositionStore store;
    NodeRef a = NodeRef::atom(AtomId{0, 'a'});
    NodeRef b = NodeRef::atom(AtomId{0, 'b'});
    NodeRef c = NodeRef::atom(AtomId{0, 'c'});
    
    SECTION("get_or_create is idempotent") {
        auto comp1 = store.get_or_create(a, b);
        auto comp2 = store.get_or_create(a, b);
        
        REQUIRE(comp1 == comp2);
        REQUIRE(store.size() == 1);
    }
    
    SECTION("Different pairs → different compositions") {
        auto ab = store.get_or_create(a, b);
        auto ac = store.get_or_create(a, c);
        
        REQUIRE_FALSE(ab == ac);
        REQUIRE(store.size() == 2);
    }
    
    SECTION("Decomposition returns original children") {
        auto comp = store.get_or_create(a, b);
        auto children = store.decompose(comp);
        
        REQUIRE(children.has_value());
        REQUIRE(children->first == a);
        REQUIRE(children->second == b);
    }
}

