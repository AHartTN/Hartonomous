/// =============================================================================
/// MOBY DICK SEMANTIC TESTS - Real queries on real data
/// 
/// These tests prove the system can:
/// 1. Store and retrieve exact phrases from Moby Dick
/// 2. Find content case-insensitively
/// 3. Verify relationships between concepts that were ingested
/// 4. Query trajectories of known phrases
/// 
/// If these fail, the implementation is broken.
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>
#include <catch2/matchers/catch_matchers_string.hpp>
#include "test_fixture.hpp"
#include "db/query_store.hpp"
#include <fstream>
#include <sstream>

using namespace hartonomous;
using namespace hartonomous::test;
using namespace hartonomous::db;
using Catch::Matchers::ContainsSubstring;

namespace {
    // Singleton for Moby Dick ingestion - only ingest once
    class MobyDickEnv {
        static inline std::atomic<bool> init_done_{false};
        static inline std::atomic<bool> available_{false};
        static inline NodeRef root_{};
        static inline std::string content_;
        
    public:
        static bool ensure() {
            if (!TestEnv::db_ready()) return false;
            
            bool expected = false;
            if (!init_done_.compare_exchange_strong(expected, true)) {
                return available_.load();
            }
            
            // Read Moby Dick
            std::string path;
#ifdef TEST_DATA_DIR
            path = std::string(TEST_DATA_DIR) + "/moby_dick.txt";
#else
            path = "../test-data/moby_dick.txt";
#endif
            std::ifstream file(path);
            if (!file.good()) {
                std::cerr << "[MOBY] File not found: " << path << std::endl;
                return false;
            }
            
            std::stringstream buffer;
            buffer << file.rdbuf();
            content_ = buffer.str();
            
            if (content_.empty()) {
                std::cerr << "[MOBY] File empty" << std::endl;
                return false;
            }
            
            std::cerr << "[MOBY] Ingesting " << content_.size() << " bytes..." << std::endl;
            root_ = TestEnv::store().encode_and_store(content_);
            std::cerr << "[MOBY] Ingested as root: " << root_.id_high << ":" << root_.id_low << std::endl;
            
            available_ = true;
            return true;
        }
        
        static NodeRef root() { return root_; }
        static const std::string& content() { return content_; }
        static QueryStore& store() { return TestEnv::store(); }
    };
}

#define REQUIRE_MOBY() do { if (!MobyDickEnv::ensure()) { SKIP("Moby Dick unavailable"); } } while(0)

// =============================================================================
// EXACT PHRASE RETRIEVAL
// =============================================================================

TEST_CASE("Moby Dick: exact phrase lookup - 'Call me Ishmael'", "[moby][exact]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // The famous opening line
    std::string phrase = "Call me Ishmael";
    
    // Compute the root for this phrase
    auto result = store.find_content(phrase);
    
    INFO("Phrase: " << phrase);
    INFO("Root: " << result.root.id_high << ":" << result.root.id_low);
    INFO("Exists: " << result.exists);
    
    // THE PROOF: This phrase exists in the ingested content
    REQUIRE(result.exists);
    
    // Decode it back and verify
    std::string decoded = store.decode_string(result.root);
    REQUIRE(decoded == phrase);
}

TEST_CASE("Moby Dick: exact phrase lookup - 'Pequod'", "[moby][exact]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    std::string phrase = "Pequod";
    auto result = store.find_content(phrase);
    
    INFO("Phrase: " << phrase);
    INFO("Exists: " << result.exists);
    
    // THE PROOF: "Pequod" exists in Moby Dick
    REQUIRE(result.exists);
    
    std::string decoded = store.decode_string(result.root);
    REQUIRE(decoded == phrase);
}

TEST_CASE("Moby Dick: exact phrase lookup - 'Captain Ahab'", "[moby][exact]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    std::string phrase = "Captain Ahab";
    auto result = store.find_content(phrase);
    
    INFO("Phrase: " << phrase);
    INFO("Exists: " << result.exists);
    
    // THE PROOF: "Captain Ahab" exists in Moby Dick
    REQUIRE(result.exists);
    
    std::string decoded = store.decode_string(result.root);
    REQUIRE(decoded == phrase);
}

TEST_CASE("Moby Dick: exact phrase lookup - 'white whale'", "[moby][exact]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    std::string phrase = "white whale";
    auto result = store.find_content(phrase);
    
    INFO("Phrase: " << phrase);
    INFO("Exists: " << result.exists);
    
    // THE PROOF: "white whale" exists in Moby Dick
    REQUIRE(result.exists);
}

TEST_CASE("Moby Dick: nonexistent phrase returns false", "[moby][exact]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // This phrase does NOT appear in Moby Dick
    std::string phrase = "quantum blockchain synergy";
    auto result = store.find_content(phrase);
    
    INFO("Phrase: " << phrase);
    INFO("Exists: " << result.exists);
    
    // THE PROOF: Garbage phrase is NOT found
    REQUIRE_FALSE(result.exists);
}

// =============================================================================
// FULL CONTENT ROUND-TRIP
// =============================================================================

TEST_CASE("Moby Dick: full content decode matches original", "[moby][roundtrip]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    auto root = MobyDickEnv::root();
    const auto& original = MobyDickEnv::content();
    
    INFO("Original size: " << original.size() << " bytes");
    INFO("Root: " << root.id_high << ":" << root.id_low);
    
    std::string decoded = store.decode_string(root);
    
    INFO("Decoded size: " << decoded.size() << " bytes");
    
    // THE PROOF: Decoded content exactly matches original
    REQUIRE(decoded.size() == original.size());
    REQUIRE(decoded == original);
}

// =============================================================================
// CASE INSENSITIVE SEARCH  
// =============================================================================

TEST_CASE("Moby Dick: case insensitive - 'PEQUOD' finds 'Pequod'", "[moby][case]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // Query uppercase
    auto variants = store.find_case_insensitive("PEQUOD");
    
    INFO("Case variants found: " << variants.size());
    
    // Check if any variant exists
    bool found = false;
    for (const auto& ref : variants) {
        if (store.exists(ref)) {
            found = true;
            std::string decoded = store.decode_string(ref);
            INFO("Found variant: " << decoded);
        }
    }
    
    // THE PROOF: Case-insensitive search finds the content
    REQUIRE(found);
}

TEST_CASE("Moby Dick: case insensitive - 'ishmael' finds 'Ishmael'", "[moby][case]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    auto variants = store.find_case_insensitive("ishmael");
    
    bool found = false;
    std::string found_text;
    for (const auto& ref : variants) {
        if (store.exists(ref)) {
            found = true;
            found_text = store.decode_string(ref);
            break;
        }
    }
    
    INFO("Found: " << found_text);
    REQUIRE(found);
}

// =============================================================================
// TRAJECTORY QUERIES
// =============================================================================

TEST_CASE("Moby Dick: trajectory for known phrase", "[moby][trajectory]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // Build trajectory for "Ahab"
    std::string name = "Ahab";
    auto traj = store.build_trajectory(name);
    
    INFO("Trajectory for '" << name << "':");
    INFO("  Points: " << traj.points.size());
    
    REQUIRE(traj.points.size() == 4);  // A, h, a, b
    
    // Verify character positions
    // A = 65, but stored as base=97 (lowercase) with variant=1 (uppercase)
    REQUIRE(traj.points[0].base == 97);   // 'a' base
    REQUIRE(traj.points[0].variant == 1); // uppercase marker
    REQUIRE(traj.points[1].base == 104);  // 'h'
    REQUIRE(traj.points[2].base == 97);   // 'a'
    REQUIRE(traj.points[3].base == 98);   // 'b'
    
    // Expand back to text
    std::string expanded = store.trajectory_to_text(traj);
    REQUIRE(expanded == name);
}

// =============================================================================
// COMPOSITION STRUCTURE
// =============================================================================

TEST_CASE("Moby Dick: composition tree is navigable", "[moby][structure]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // Get the composition for "whale"
    auto result = store.find_content("whale");
    REQUIRE(result.exists);
    
    // Look up its children
    auto children = store.lookup(result.root);
    
    INFO("'whale' children exist: " << children.has_value());
    
    // THE PROOF: The composition has internal structure (not a leaf)
    REQUIRE(children.has_value());
    
    // Navigate deeper
    auto [left, right] = *children;
    INFO("Left child: " << left.id_high << ":" << left.id_low);
    INFO("Right child: " << right.id_high << ":" << right.id_low);
    
    // Both children should exist
    REQUIRE(store.exists(left));
    REQUIRE(store.exists(right));
}

TEST_CASE("Moby Dick: atoms are leaves in tree", "[moby][structure]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // Single character is an atom
    auto result = store.find_content("w");
    REQUIRE(result.exists);
    REQUIRE(result.root.is_atom);
    
    // Atoms have no children
    auto children = store.lookup(result.root);
    REQUIRE_FALSE(children.has_value());
}

// =============================================================================
// SEMANTIC DISTANCE
// =============================================================================

TEST_CASE("Moby Dick: similar characters are spatially close", "[moby][spatial]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // Find characters near 'a'
    auto near_a = store.find_similar('a', 50);
    
    INFO("Characters near 'a': " << near_a.size());
    
    // Should find other lowercase letters nearby
    bool found_b = false, found_c = false;
    for (const auto& match : near_a) {
        if (match.codepoint == 'b') {
            found_b = true;
            INFO("Distance a->b: " << match.distance);
            REQUIRE(match.distance < 10.0);  // Should be very close
        }
        if (match.codepoint == 'c') {
            found_c = true;
            INFO("Distance a->c: " << match.distance);
            REQUIRE(match.distance < 10.0);
        }
    }
    
    REQUIRE(found_b);
    REQUIRE(found_c);
}

// =============================================================================
// KNOWN FACTS ABOUT MOBY DICK
// =============================================================================

TEST_CASE("Moby Dick: contains expected phrases", "[moby][content]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // List of phrases that MUST be in Moby Dick
    std::vector<std::string> must_exist = {
        "Call me Ishmael",
        "Pequod",
        "Captain Ahab",
        "Moby Dick",
        "white whale",
        "Queequeg",
        "Starbuck",
        "harpooner",
        "Nantucket",
        "the sea"
    };
    
    for (const auto& phrase : must_exist) {
        auto result = store.find_content(phrase);
        INFO("Checking: '" << phrase << "'");
        REQUIRE(result.exists);
    }
}

TEST_CASE("Moby Dick: chapter titles exist", "[moby][content]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    // Chapter titles from Moby Dick
    std::vector<std::string> chapters = {
        "Loomings",
        "The Carpet-Bag",
        "The Spouter-Inn",
        "Cetology",
        "The Whiteness of the Whale"
    };
    
    for (const auto& title : chapters) {
        auto result = store.find_content(title);
        INFO("Chapter: '" << title << "'");
        REQUIRE(result.exists);
    }
}

// =============================================================================
// CONTENT STATISTICS
// =============================================================================

TEST_CASE("Moby Dick: ingestion creates expected composition count", "[moby][stats]") {
    REQUIRE_MOBY();
    auto& store = MobyDickEnv::store();
    
    auto comp_count = store.composition_count();
    
    INFO("Compositions in DB: " << comp_count);
    INFO("Moby Dick size: " << MobyDickEnv::content().size() << " bytes");
    
    // Moby Dick is ~1.2MB, should create many compositions
    // (not as many as bytes due to merkle tree structure)
    REQUIRE(comp_count > 100000);
}

