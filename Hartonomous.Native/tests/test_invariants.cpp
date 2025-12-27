/// HARTONOMOUS INVARIANT TESTS
/// 
/// These tests verify the MATHEMATICAL GUARANTEES that make the system work.
/// If any of these fail, the system is fundamentally broken.
/// 
/// NO VERBOSE OUTPUT. NO PROOF-OF-CONCEPT GARBAGE.
/// JUST THE INVARIANTS THAT MATTER.

#include <catch2/catch_test_macros.hpp>
#include "atoms/semantic_decompose.hpp"
#include "atoms/semantic_hilbert.hpp"
#include "atoms/semantic_point.hpp"
#include "atoms/geometry.hpp"
#include "atoms/pair_encoding_cascade.hpp"
#include "atoms/pair_encoding_engine.hpp"
#include <set>
#include <fstream>
#include <chrono>

using namespace hartonomous;

// =============================================================================
// BIJECTIVITY: Every codepoint has a unique reversible encoding
// =============================================================================

TEST_CASE("Codepoint encoding is bijective for full Unicode", "[invariant][bijective]") {
    std::set<std::pair<std::int64_t, std::int64_t>> seen;
    
    // Sample Unicode space with prime step for coverage
    for (std::int32_t cp = 0; cp <= 0x10FFFF; cp += 997) {
        auto id = SemanticDecompose::get_atom_id(cp);
        auto key = std::make_pair(id.high, id.low);
        
        REQUIRE(seen.find(key) == seen.end());
        seen.insert(key);
        
        // Round-trip
        auto recovered = SemanticHilbert::to_semantic(id);
        auto id2 = SemanticHilbert::from_semantic(recovered);
        REQUIRE(id == id2);
    }
}

TEST_CASE("SemanticCoord pack/unpack is lossless", "[invariant][lossless]") {
    // Boundary values only - don't waste time on obvious cases
    SemanticCoord corners[] = {
        {0, 0, 0, 0},
        {7, 7, 0x1FFFFF, 31},
        {3, 5, 0x4E00, 15},  // CJK middle
        {7, 0, 0x10FFFF, 0}, // Max base
    };
    
    for (const auto& orig : corners) {
        auto unpacked = SemanticCoord::unpack(orig.pack());
        REQUIRE(unpacked.page == orig.page);
        REQUIRE(unpacked.type == orig.type);
        REQUIRE(unpacked.base == orig.base);
        REQUIRE(unpacked.variant == orig.variant);
    }
}

// =============================================================================
// SEMANTIC CLUSTERING: Related characters share bases
// =============================================================================

TEST_CASE("Case pairs share base across all scripts", "[invariant][semantic]") {
    // Latin
    for (int i = 0; i < 26; ++i) {
        auto upper = SemanticDecompose::decompose('A' + i);
        auto lower = SemanticDecompose::decompose('a' + i);
        REQUIRE(upper.base == lower.base);
        REQUIRE(upper.base == 'a' + i);
    }
    
    // Greek (sampled)
    REQUIRE(SemanticDecompose::decompose(0x0391).base == 
            SemanticDecompose::decompose(0x03B1).base);
    
    // Cyrillic (sampled)
    REQUIRE(SemanticDecompose::decompose(0x0410).base == 
            SemanticDecompose::decompose(0x0430).base);
}

TEST_CASE("Diacriticals cluster to base letter", "[invariant][semantic]") {
    auto base_a = SemanticDecompose::decompose('a').base;
    
    std::int32_t a_variants[] = {
        0x00C0, 0x00C1, 0x00C2, 0x00C3, 0x00C4, 0x00C5,  // À Á Â Ã Ä Å
        0x00E0, 0x00E1, 0x00E2, 0x00E3, 0x00E4, 0x00E5,  // à á â ã ä å
    };
    
    for (auto cp : a_variants) {
        REQUIRE(SemanticDecompose::decompose(cp).base == base_a);
    }
}

TEST_CASE("Type classification is correct", "[invariant][semantic]") {
    auto letter_type = SemanticDecompose::decompose('A').type;
    auto digit_type = SemanticDecompose::decompose('0').type;
    auto punct_type = SemanticDecompose::decompose('.').type;
    auto control_type = SemanticDecompose::decompose(0x00).type;
    
    REQUIRE(letter_type != digit_type);
    REQUIRE(letter_type != punct_type);
    REQUIRE(letter_type != control_type);
    REQUIRE(digit_type != punct_type);
    
    // All letters same type
    for (int c = 'a'; c <= 'z'; ++c) {
        REQUIRE(SemanticDecompose::decompose(c).type == letter_type);
    }
    
    // All digits same type
    for (int c = '0'; c <= '9'; ++c) {
        REQUIRE(SemanticDecompose::decompose(c).type == digit_type);
    }
}

TEST_CASE("Scripts are separated by page", "[invariant][semantic]") {
    auto latin_page = SemanticDecompose::decompose('A').page;
    auto greek_page = SemanticDecompose::decompose(0x0391).page;
    auto cjk_page = SemanticDecompose::decompose(0x4E00).page;
    auto emoji_page = SemanticDecompose::decompose(0x1F600).page;
    
    REQUIRE(latin_page != cjk_page);
    REQUIRE(greek_page != cjk_page);
    REQUIRE(emoji_page != latin_page);
}

// =============================================================================
// MERKLE DAG: Content addressing is deterministic
// =============================================================================

TEST_CASE("Same content always produces same root", "[invariant][merkle]") {
    PairEncodingEngine engine1, engine2;
    
    std::string input = "The quick brown fox jumps over the lazy dog.";
    auto root1 = engine1.ingest(input);
    auto root2 = engine2.ingest(input);
    
    REQUIRE(root1.id_high == root2.id_high);
    REQUIRE(root1.id_low == root2.id_low);
}

TEST_CASE("Different content produces different roots", "[invariant][merkle]") {
    PairEncodingEngine engine;
    
    auto root1 = engine.ingest("cat");
    auto root2 = engine.ingest("act");
    
    REQUIRE((root1.id_high != root2.id_high || root1.id_low != root2.id_low));
}

// =============================================================================
// LOSSLESS: Round-trip encoding preserves all data
// =============================================================================

TEST_CASE("Unicode codepoints round-trip losslessly", "[invariant][lossless]") {
    PairEncodingEngine engine;

    // Sample from the FULL 1.1M codepoint space using prime step
    std::vector<std::int32_t> codepoints;
    for (std::int32_t cp = 0; cp <= 0x10FFFF; cp += 997) {
        if (cp >= 0xD800 && cp <= 0xDFFF) continue;  // Skip surrogates
        codepoints.push_back(cp);
    }

    // Encode to UTF-8
    std::string input = UTF8Decoder::encode(codepoints);

    auto root = engine.ingest(input);
    auto decoded = engine.decode(root);

    bool matches = (std::string(decoded.begin(), decoded.end()) == input);
    REQUIRE(matches);
}

TEST_CASE("Large repetitive data round-trips", "[invariant][lossless]") {
    PairEncodingEngine engine;
    
    std::string input;
    for (int i = 0; i < 10000; ++i) {
        input += "abcd";
    }
    
    auto root = engine.ingest(input);
    auto decoded = engine.decode(root);
    
    REQUIRE(std::string(decoded.begin(), decoded.end()) == input);
}

TEST_CASE("Moby Dick round-trips losslessly", "[invariant][lossless][moby]") {
    std::string path = std::string(TEST_DATA_DIR) + "/moby_dick.txt";
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    
    if (!file.good()) {
        SKIP("Test data not available");
    }
    
    auto size = file.tellg();
    REQUIRE(size > 1000000);
    
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
// COMPRESSION: Repeated patterns are deduplicated
// =============================================================================

TEST_CASE("Shared substrings share compositions", "[invariant][compression]") {
    CompositionStore store;
    
    PairEncodingCascade::encode("the cat", store);
    auto count1 = store.size();
    
    PairEncodingCascade::encode("the dog", store);
    auto count2 = store.size();
    
    // Second encode should add fewer compositions (reuses "the ")
    REQUIRE(count2 < count1 * 2);
}

TEST_CASE("Highly repetitive data compresses significantly", "[invariant][compression]") {
    CompositionStore store;
    
    std::string repetitive(1000, 'a');
    PairEncodingCascade::encode(repetitive.c_str(), store);
    
    // 1000 identical chars should compress to very few compositions
    REQUIRE(store.size() < 20);
}

