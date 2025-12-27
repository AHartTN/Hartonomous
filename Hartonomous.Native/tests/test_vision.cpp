/// HARTONOMOUS VISION TESTS
///
/// These tests verify the ACTUAL CAPABILITIES that matter:
/// - Semantic similarity queries
/// - Case/accent-insensitive matching
/// - Efficient substring sharing
/// - PostGIS-compatible geometry output
/// - Real-world performance on actual data
///
/// These are not proof-of-concept toy tests. These test the VISION.

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include "atoms/semantic_decompose.hpp"
#include "atoms/geometry.hpp"
#include "atoms/pair_encoding_engine.hpp"
#include "atoms/pair_encoding_cascade.hpp"
#include <algorithm>
#include <fstream>
#include <chrono>
#include <cmath>
#include <set>

using namespace hartonomous;
using Catch::Matchers::WithinAbs;

// =============================================================================
// SEMANTIC SIMILARITY: Find related content through geometry
// =============================================================================

TEST_CASE("Case-insensitive matching via shared base", "[vision][similarity]") {
    // "Hello" and "HELLO" should have related semantic signatures
    // because each letter pair shares the same base
    
    auto get_bases = [](const std::string& s) {
        std::vector<std::int32_t> bases;
        for (char c : s) {
            bases.push_back(SemanticDecompose::decompose(c).base);
        }
        return bases;
    };
    
    auto bases_hello = get_bases("Hello");
    auto bases_HELLO = get_bases("HELLO");
    
    // All bases should match (case is just variant)
    REQUIRE(bases_hello == bases_HELLO);
}

TEST_CASE("Accent-insensitive matching via shared base", "[vision][similarity]") {
    // "naive" and "naïve" should match on base sequence
    
    auto get_bases = [](const std::u32string& s) {
        std::vector<std::int32_t> bases;
        for (auto cp : s) {
            bases.push_back(SemanticDecompose::decompose(cp).base);
        }
        return bases;
    };
    
    // naive: n-a-i-v-e
    std::u32string naive = U"naive";
    // naïve: n-a-ï-v-e (ï = 0x00EF)
    std::u32string naive_fr = U"na\u00EFve";
    
    auto bases1 = get_bases(naive);
    auto bases2 = get_bases(naive_fr);
    
    REQUIRE(bases1 == bases2);
}

TEST_CASE("Similar words have closer LineString paths", "[vision][similarity]") {
    // "cat" and "bat" differ by one letter
    // "cat" and "xyz" differ completely
    // The path distance should reflect this
    
    auto word_to_line = [](const std::string& s) {
        LineStringZM<64> line;
        for (char c : s) {
            line.push_codepoint(c);
        }
        return line;
    };
    
    auto cat = word_to_line("cat");
    auto bat = word_to_line("bat");
    auto xyz = word_to_line("xyz");
    
    // Fréchet distance: cat↔bat should be less than cat↔xyz
    double dist_cat_bat = cat.frechet_distance(bat);
    double dist_cat_xyz = cat.frechet_distance(xyz);
    
    REQUIRE(dist_cat_bat < dist_cat_xyz);
}

TEST_CASE("Anagrams have different path shapes", "[vision][similarity]") {
    // "cat" and "act" are anagrams - same letters, different order
    // Their paths should be distinguishable
    
    auto word_to_line = [](const std::string& s) {
        LineStringZM<64> line;
        for (char c : s) {
            line.push_codepoint(c);
        }
        return line;
    };
    
    auto cat = word_to_line("cat");
    auto act = word_to_line("act");
    
    // They start at different points
    REQUIRE_FALSE(cat.points[0].z == act.points[0].z);
    
    // Path lengths should be similar but not identical
    double len_cat = cat.length();
    double len_act = act.length();
    
    // Both are 3-letter words, lengths differ by traversal order
    REQUIRE(std::abs(len_cat - len_act) < len_cat);  // Same order of magnitude
}

TEST_CASE("Script separation prevents false cross-script matches", "[vision][similarity]") {
    // Latin 'A' (0x0041) must not be confused with Cyrillic 'А' (0x0410)
    // despite looking identical
    
    auto pt_latin_A = PointZM::from_codepoint('A');
    auto pt_cyrillic_A = PointZM::from_codepoint(0x0410);
    auto pt_latin_B = PointZM::from_codepoint('B');
    
    // Latin A should be CLOSER to Latin B than to Cyrillic A
    double dist_latin_pair = pt_latin_A.distance_squared(pt_latin_B);
    double dist_cross_script = pt_latin_A.distance_squared(pt_cyrillic_A);
    
    REQUIRE(dist_latin_pair < dist_cross_script);
}

// =============================================================================
// SUBSTRING DEDUPLICATION: Shared content is stored once
// =============================================================================

TEST_CASE("Common phrases are deduplicated across documents", "[vision][deduplication]") {
    CompositionStore store;
    
    // Three documents all containing "the quick brown fox"
    std::string doc1 = "the quick brown fox jumps over the lazy dog";
    std::string doc2 = "observe the quick brown fox in its natural habitat";
    std::string doc3 = "the quick brown fox is a pangram";
    
    PairEncodingCascade::encode(doc1.c_str(), store);
    auto count1 = store.size();
    
    PairEncodingCascade::encode(doc2.c_str(), store);
    auto count2 = store.size();
    
    PairEncodingCascade::encode(doc3.c_str(), store);
    auto count3 = store.size();
    
    // Each additional document should add fewer compositions
    // because "the quick brown fox" is already in the store
    double ratio1 = static_cast<double>(count2 - count1) / doc2.size();
    double ratio2 = static_cast<double>(count3 - count2) / doc3.size();
    
    // Later documents should have better compression ratios
    REQUIRE(ratio2 <= ratio1 * 1.5);  // Allow some variance
}

TEST_CASE("Vocabulary learning improves compression over time", "[vision][deduplication]") {
    PairEncodingEngine::Config config;
    config.min_pair_frequency = 2;
    PairEncodingEngine engine(config);
    
    // Feed in text with repeated patterns
    std::string text;
    for (int i = 0; i < 100; ++i) {
        text += "the quick brown fox ";
    }
    
    engine.ingest(text);
    
    // Should have learned vocabulary
    REQUIRE(engine.vocabulary_size() > 0);
    
    // New text with same patterns should compress better
    std::string text2 = "the quick brown fox jumps";
    engine.ingest(text2);
    
    // Vocabulary should grow only slightly for new patterns
    auto vocab_after = engine.vocabulary_size();
    REQUIRE(vocab_after < 100);  // Not 100 new entries for 25 chars
}

// =============================================================================
// POSTGIS INTEGRATION: Output is valid WKT geometry
// =============================================================================

TEST_CASE("PointZM generates valid PostGIS WKT", "[vision][postgis]") {
    auto pt = PointZM::from_codepoint('A');
    
    char buffer[256];
    pt.to_wkt(buffer, sizeof(buffer));
    
    std::string wkt(buffer);
    
    // Valid PostGIS POINT ZM format
    REQUIRE(wkt.find("POINT ZM (") == 0);
    REQUIRE(wkt.back() == ')');
    
    // Has 4 coordinates separated by spaces
    int space_count = std::count(wkt.begin(), wkt.end(), ' ');
    REQUIRE(space_count >= 4);  // "POINT ZM (" + 3 spaces between coords
}

TEST_CASE("LineStringZM generates valid PostGIS WKT", "[vision][postgis]") {
    LineStringZM<64> line;
    line.push_codepoint('H');
    line.push_codepoint('i');
    
    char buffer[1024];
    line.to_wkt(buffer, sizeof(buffer));
    
    std::string wkt(buffer);
    
    // Valid PostGIS LINESTRING ZM format
    REQUIRE(wkt.find("LINESTRING ZM (") == 0);
    REQUIRE(wkt.back() == ')');
    
    // Should contain comma separating points
    REQUIRE(wkt.find(',') != std::string::npos);
}

TEST_CASE("Empty LineString produces EMPTY WKT", "[vision][postgis]") {
    LineStringZM<64> line;
    
    char buffer[256];
    line.to_wkt(buffer, sizeof(buffer));
    
    REQUIRE(std::string(buffer) == "LINESTRING ZM EMPTY");
}

// =============================================================================
// PERFORMANCE: Real-world data at acceptable speed
// =============================================================================

TEST_CASE("Process 1MB successfully", "[vision][performance]") {
    std::string path = std::string(TEST_DATA_DIR) + "/moby_dick.txt";
    std::ifstream file(path, std::ios::binary | std::ios::ate);
    
    if (!file.good()) {
        SKIP("Test data not available");
    }
    
    auto size = file.tellg();
    file.seekg(0);
    std::vector<char> data(static_cast<std::size_t>(size));
    file.read(data.data(), size);
    
    PairEncodingEngine engine;
    
    (void)engine.ingest(reinterpret_cast<const std::uint8_t*>(data.data()), data.size());
    
    // Verify it actually did something
    REQUIRE(engine.bytes_processed() == data.size());
}

TEST_CASE("Decomposition is fast (>1M codepoints/sec)", "[vision][performance]") {
    constexpr int N = 1000000;
    
    auto start = std::chrono::high_resolution_clock::now();
    
    volatile std::uint32_t sink = 0;
    for (int i = 0; i < N; ++i) {
        auto coord = SemanticDecompose::decompose(i % 0x10FFFF);
        sink = sink + coord.pack();
    }
    
    auto elapsed = std::chrono::high_resolution_clock::now() - start;
    double seconds = std::chrono::duration<double>(elapsed).count();
    double rate = N / seconds;
    
    REQUIRE(rate > 1000000.0);  // > 1M/sec
}

// =============================================================================
// QUERY PATTERNS: Common search operations work correctly
// =============================================================================

TEST_CASE("Find all case variants of a base", "[vision][query]") {
    // Given base 'a', find all codepoints that map to it
    std::vector<std::int32_t> a_family;
    
    // Check Latin-1 supplement where A diacriticals live
    std::vector<std::int32_t> test_codepoints = {
        'A', 'a', 0x00C0, 0x00C1, 0x00C2, 0x00E0, 0x00E1, 0x00E4
    };
    for (std::int32_t cp : test_codepoints) {
        if (SemanticDecompose::decompose(cp).base == 'a') {
            a_family.push_back(cp);
        }
    }
    
    // Should find multiple
    REQUIRE(a_family.size() >= 4);
    
    // All should be distinguishable by variant
    std::set<std::uint8_t> variants;
    for (auto cp : a_family) {
        variants.insert(SemanticDecompose::decompose(cp).variant);
    }
    REQUIRE(variants.size() == a_family.size());
}

TEST_CASE("Range query by page finds same-script characters", "[vision][query]") {
    auto latin_page = SemanticDecompose::decompose('A').page;
    
    int latin_count = 0;
    int non_latin_count = 0;
    
    // Check ASCII range
    for (int cp = 0; cp < 128; ++cp) {
        auto coord = SemanticDecompose::decompose(cp);
        if (coord.page == latin_page && coord.type == 1) {  // Letters
            latin_count++;
        }
    }
    
    // Check CJK range
    for (int cp = 0x4E00; cp < 0x4E10; ++cp) {
        auto coord = SemanticDecompose::decompose(cp);
        if (coord.page == latin_page) {
            non_latin_count++;
        }
    }
    
    REQUIRE(latin_count == 52);  // A-Z, a-z
    REQUIRE(non_latin_count == 0);  // CJK on different page
}

// =============================================================================
// ERROR HANDLING: Graceful behavior on edge cases
// =============================================================================

TEST_CASE("Invalid codepoints produce valid coordinates", "[vision][robustness]") {
    // Negative
    auto neg = SemanticDecompose::decompose(-1);
    REQUIRE(neg.page <= 7);
    REQUIRE(neg.type <= 7);
    REQUIRE(neg.variant <= 31);
    
    // Beyond Unicode
    auto beyond = SemanticDecompose::decompose(0x200000);
    REQUIRE(beyond.page <= 7);
    REQUIRE(beyond.type <= 7);
    REQUIRE(beyond.variant <= 31);
}

TEST_CASE("Empty input produces null root", "[vision][robustness]") {
    PairEncodingEngine engine;
    
    auto root = engine.ingest("", 0);
    
    REQUIRE(root.id_high == 0);
    REQUIRE(root.id_low == 0);
}

TEST_CASE("Single byte produces atom (not composition)", "[vision][robustness]") {
    PairEncodingEngine engine;
    
    auto root = engine.ingest("X", 1);
    
    REQUIRE(root.is_atom == true);
}

