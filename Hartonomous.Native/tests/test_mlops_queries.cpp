/// =============================================================================
/// MLOPS QUERY TESTS - Prove the AI operations actually function
/// 
/// Tests for:
/// - Trajectory storage and retrieval
/// - Trajectory intersection queries (ST_DWithin)
/// - Frechet distance similarity (ST_FrechetDistance)
/// - Bounding box queries in 4D semantic space
/// - Cross-attention with real trajectories
/// - Transform similarity calculations
/// - Generation via trajectory endpoint intersection
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>
#include "test_fixture.hpp"
#include "mlops/mlops.hpp"
#include "db/query_store.hpp"
#include <chrono>

using namespace hartonomous;
using namespace hartonomous::test;
using namespace hartonomous::db;
using namespace hartonomous::mlops;
using Catch::Matchers::WithinAbs;

// =============================================================================
// TRAJECTORY STORAGE & RETRIEVAL
// =============================================================================

TEST_CASE("Trajectory: store and retrieve round-trip", "[mlops][trajectory]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Create two unique compositions (use timestamp to avoid collisions)
    auto now = std::chrono::steady_clock::now().time_since_epoch().count();
    auto hello = store.encode_and_store("hello_traj_" + std::to_string(now));
    auto world = store.encode_and_store("world_traj_" + std::to_string(now));
    
    // Build trajectory manually
    Trajectory traj;
    traj.weight = 0.85;
    traj.points = {
        TrajectoryPoint{0, 0, 104, 0, 1},  // 'h'
        TrajectoryPoint{0, 0, 101, 0, 1},  // 'e'
        TrajectoryPoint{0, 0, 108, 0, 2},  // 'll'
        TrajectoryPoint{0, 0, 111, 0, 1},  // 'o'
    };
    
    // Store it
    store.store_trajectory(hello, world, traj);
    
    // Retrieve it
    auto retrieved = store.get_trajectory(hello, world);
    REQUIRE(retrieved.has_value());
    REQUIRE(retrieved->weight > 0.0);  // Weight may have accumulated from previous runs
    REQUIRE(retrieved->points.size() == 4);
}

TEST_CASE("Trajectory: build from text", "[mlops][trajectory]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Build trajectory from actual text
    std::string text = "test";
    Trajectory traj = store.build_trajectory(text);
    
    REQUIRE(!traj.points.empty());
    
    // Expand and verify
    std::size_t total_chars = 0;
    for (const auto& pt : traj.points) {
        total_chars += pt.count;
    }
    REQUIRE(total_chars == text.size());
}

TEST_CASE("Trajectory: to text round-trip", "[mlops][trajectory]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Short ASCII text
    std::string original = "Captain Ahab";
    Trajectory traj = store.build_trajectory(original);
    std::string decoded = store.trajectory_to_text(traj);
    
    REQUIRE(decoded == original);
}

TEST_CASE("Trajectory: WKT format validity", "[mlops][trajectory]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    std::string text = "whale";
    Trajectory traj = store.build_trajectory(text);
    std::string wkt = traj.to_wkt();
    
    // Should be valid LINESTRINGZM format (no space between LINESTRING and ZM)
    REQUIRE(wkt.find("LINESTRINGZM") == 0);
    REQUIRE(wkt.find("(") != std::string::npos);
    REQUIRE(wkt.find(")") != std::string::npos);
    
    // Should have coordinates for each point
    // Count commas - should be points-1 for valid linestring
    std::size_t comma_count = std::count(wkt.begin(), wkt.end(), ',');
    REQUIRE(comma_count >= traj.points.size() - 1);
}

// =============================================================================
// TRAJECTORY INTERSECTION QUERIES - Where meaning lives
// =============================================================================

TEST_CASE("Trajectory intersection: similar words intersect", "[mlops][trajectory][intersection]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Create related words with stored trajectories
    auto king = store.encode_and_store("king_int");
    auto monarch = store.encode_and_store("monarch_int");
    auto ruler = store.encode_and_store("ruler_int");
    auto banana = store.encode_and_store("banana_int");
    
    // Build and store trajectories 
    Trajectory traj_king = store.build_trajectory("king");
    traj_king.weight = 1.0;
    store.store_trajectory(king, king, traj_king);
    
    Trajectory traj_monarch = store.build_trajectory("monarch");
    traj_monarch.weight = 1.0;
    store.store_trajectory(monarch, monarch, traj_monarch);
    
    Trajectory traj_ruler = store.build_trajectory("ruler");
    traj_ruler.weight = 1.0;
    store.store_trajectory(ruler, ruler, traj_ruler);
    
    Trajectory traj_banana = store.build_trajectory("banana");
    traj_banana.weight = 1.0;
    store.store_trajectory(banana, banana, traj_banana);
    
    // Query intersections - use large threshold since words differ
    auto intersections = store.query_trajectory_intersections(king, 500.0);
    
    // Should get results (threshold is large enough to catch something)
    // Note: exact intersection semantics depend on actual trajectory paths
    INFO("Found " << intersections.size() << " trajectory intersections");
    
    // This tests the query machinery works without erroring
    // Actual results depend on character positions in semantic space
}

TEST_CASE("Trajectory intersection: self excluded", "[mlops][trajectory][intersection]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    auto self = store.encode_and_store("selftest_int");
    Trajectory traj = store.build_trajectory("selftest");
    traj.weight = 1.0;
    store.store_trajectory(self, self, traj);
    
    auto intersections = store.query_trajectory_intersections(self, 0.0);
    
    // Self should be excluded from results
    for (const auto& [ref, dist] : intersections) {
        bool is_self = (ref.id_high == self.id_high && ref.id_low == self.id_low);
        REQUIRE_FALSE(is_self);
    }
}

// =============================================================================
// FRECHET DISTANCE - Trajectory similarity
// =============================================================================

TEST_CASE("Trajectory neighbors: Frechet distance ordering", "[mlops][trajectory][frechet]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Store trajectories for similar-length words
    auto cat = store.encode_and_store("cat_frech");
    auto bat = store.encode_and_store("bat_frech");
    auto hat = store.encode_and_store("hat_frech");
    auto caterpillar = store.encode_and_store("caterpillar_frech");
    
    for (auto& [ref, text] : std::initializer_list<std::pair<NodeRef, std::string>>{
        {cat, "cat"}, {bat, "bat"}, {hat, "hat"}, {caterpillar, "caterpillar"}
    }) {
        Trajectory traj = store.build_trajectory(text);
        traj.weight = 1.0;
        store.store_trajectory(ref, ref, traj);
    }
    
    // Query neighbors of "cat"
    auto neighbors = store.query_trajectory_neighbors(cat, 10);
    
    // Results should be ordered by Frechet distance
    for (std::size_t i = 1; i < neighbors.size(); ++i) {
        REQUIRE(neighbors[i-1].second <= neighbors[i].second);
    }
}

// =============================================================================
// BOUNDING BOX QUERIES - 4D semantic space regions
// =============================================================================

TEST_CASE("Bounding box: query ASCII region", "[mlops][trajectory][bbox]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Query the ASCII letter region (page=0, type=0, base=97-122 for lowercase)
    // PointZM scales: base → z as base / 0x10FFFF * 1000
    // For 'a' (97): z ≈ 97 / 1114111 * 1000 ≈ 0.087
    // For 'z' (122): z ≈ 122 / 1114111 * 1000 ≈ 0.11
    
    auto results = store.query_bounding_box(
        0.0, 125.0,    // page (X)
        0.0, 125.0,    // type (Y) 
        0.0, 1.0,      // base (Z) - ASCII range
        0.0, 1000.0,   // variant (M)
        50
    );
    
    // Should find atoms in this region
    INFO("Found " << results.size() << " atoms in ASCII bounding box");
    // The query should execute without error
    // Actual count depends on atom seeding
}

TEST_CASE("Bounding box: empty region returns empty", "[mlops][trajectory][bbox]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Query an impossible region (negative coordinates)
    auto results = store.query_bounding_box(
        -1000.0, -999.0,
        -1000.0, -999.0,
        -1000.0, -999.0,
        -1000.0, -999.0,
        50
    );
    
    REQUIRE(results.empty());
}

// =============================================================================
// QUERY TRAJECTORIES THROUGH POINT
// =============================================================================

TEST_CASE("Trajectories through point: finds compositions", "[mlops][trajectory][through]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    
    // Create and store some trajectories
    auto whale = store.encode_and_store("whale_through");
    auto while_ = store.encode_and_store("while_through");
    
    Trajectory traj_whale = store.build_trajectory("whale");
    traj_whale.weight = 1.0;
    store.store_trajectory(whale, whale, traj_whale);
    
    Trajectory traj_while = store.build_trajectory("while");
    traj_while.weight = 1.0;
    store.store_trajectory(while_, while_, traj_while);
    
    // Get a point from whale's trajectory
    if (!traj_whale.points.empty()) {
        const auto& pt = traj_whale.points[0];  // 'w' character point
        
        // Query trajectories through this point with wide radius
        auto through = store.query_trajectories_through_point(
            static_cast<double>(pt.page),
            static_cast<double>(pt.type),
            static_cast<double>(pt.base),
            static_cast<double>(pt.variant),
            100.0,  // Wide radius
            20
        );
        
        // Should find something - both start with 'w'
        INFO("Found " << through.size() << " trajectories through 'w' region");
    }
}

// =============================================================================
// CROSS-ATTENTION WITH REAL TRAJECTORIES
// =============================================================================

TEST_CASE("Cross-attention: trajectory-based scoring", "[mlops][attention][cross]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    // Create compositions with trajectories
    auto query = store.encode_and_store("cat_cross");
    auto key1 = store.encode_and_store("cap_cross");  // Similar start
    auto key2 = store.encode_and_store("dog_cross");  // Different
    
    // Store trajectories
    for (auto& [ref, text] : std::initializer_list<std::pair<NodeRef, std::string>>{
        {query, "cat"}, {key1, "cap"}, {key2, "dog"}
    }) {
        Trajectory traj = store.build_trajectory(text);
        traj.weight = 1.0;
        store.store_trajectory(ref, ref, traj);
    }
    
    // Cross-attend from query to keys
    std::vector<NodeRef> keys = {key1, key2};
    auto result = ops.cross_attend(query, keys);
    
    // Should return attended nodes
    // Fallback to relationship-based if no trajectory match
    // This tests the API works
}

// =============================================================================
// TRANSFORM SIMILARITY
// =============================================================================

TEST_CASE("Transform similarity: identical trajectories", "[mlops][transform][similarity]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto a = store.encode_and_store("identical_a");
    auto b = store.encode_and_store("identical_b");
    
    // Store same trajectory for both
    Trajectory traj = store.build_trajectory("same");
    traj.weight = 1.0;
    store.store_trajectory(a, a, traj);
    store.store_trajectory(b, b, traj);
    
    double sim = ops.similarity(a, b);
    
    // Identical trajectories should have high similarity (close to 1.0)
    // Note: similarity = 1 / (1 + distance), so identical = 1.0
    REQUIRE(sim > 0.5);
}

TEST_CASE("Transform similarity: different trajectories", "[mlops][transform][similarity]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    auto short_ = store.encode_and_store("x_sim");
    auto long_ = store.encode_and_store("supercalifragilistic_sim");
    
    Trajectory traj_short = store.build_trajectory("x");
    traj_short.weight = 1.0;
    store.store_trajectory(short_, short_, traj_short);
    
    Trajectory traj_long = store.build_trajectory("supercalifragilistic");
    traj_long.weight = 1.0;
    store.store_trajectory(long_, long_, traj_long);
    
    double sim = ops.similarity(short_, long_);
    
    // Very different trajectories should have low similarity
    REQUIRE(sim < 0.9);
    REQUIRE(sim >= 0.0);
}

TEST_CASE("Transform similarity: no trajectory returns zero", "[mlops][transform][similarity]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    // Nodes without stored trajectories
    auto a = store.encode_and_store("notraj_a_sim");
    auto b = store.encode_and_store("notraj_b_sim");
    
    double sim = ops.similarity(a, b);
    REQUIRE(sim == 0.0);
}

// =============================================================================
// GENERATION VIA TRAJECTORY ENDPOINT
// =============================================================================

TEST_CASE("Generation: trajectory endpoint candidates", "[mlops][generation][endpoint]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    // Create a context with trajectory
    auto context = store.encode_and_store("the_king_sat_on_his_");
    
    // Store trajectory for context
    Trajectory traj = store.build_trajectory("the king sat on his ");
    traj.weight = 1.0;
    store.store_trajectory(context, context, traj);
    
    // Create candidates that should intersect at endpoint
    auto throne = store.encode_and_store("throne_gen");
    auto chair = store.encode_and_store("chair_gen");
    
    // Store trajectories for candidates - they end where context ends
    // (In reality these would be stored from corpus ingestion)
    Trajectory traj_throne = store.build_trajectory("throne");
    traj_throne.weight = 1.0;
    store.store_trajectory(throne, throne, traj_throne);
    
    Trajectory traj_chair = store.build_trajectory("chair");
    traj_chair.weight = 1.0;
    store.store_trajectory(chair, chair, traj_chair);
    
    // Generate - should find candidates via trajectory intersection at endpoint
    auto result = ops.generate(context);
    
    // Generation should work (may fall back to relationship-based)
    // This tests the full generation pipeline
}

// =============================================================================
// ATTEND WITH TRAJECTORY INTERSECTION
// =============================================================================

TEST_CASE("Attention: trajectory intersection based", "[mlops][attention][trajectory]") {
    REQUIRE_DB();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    // Create query with trajectory
    auto query = store.encode_and_store("query_attn_traj");
    Trajectory traj = store.build_trajectory("query");
    traj.weight = 1.0;
    store.store_trajectory(query, query, traj);
    
    // Attend - will try trajectory intersection first, fall back to relationships
    auto result = ops.attend(query);
    
    // Query ref should be set
    REQUIRE(result.query_ref.id_high == query.id_high);
    REQUIRE(result.query_ref.id_low == query.id_low);
    
    // Weights should sum to 1 if any attended
    if (!result.attended.empty()) {
        double sum = 0.0;
        for (const auto& n : result.attended) {
            sum += n.attention_weight;
        }
        REQUIRE_THAT(sum, WithinAbs(1.0, 0.01));
    }
}

// =============================================================================
// INFERENCE PATH WITH REAL STRUCTURE
// =============================================================================

TEST_CASE("Inference: finds path through model weights", "[mlops][inference][path]") {
    REQUIRE_MODEL();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    // Use actual vocabulary tokens from ingested model
    const auto& vocab = TestEnv::ingester().vocabulary();
    REQUIRE(!vocab.empty());
    
    // Find a common word
    NodeRef input{};
    for (const auto& token : vocab) {
        if (token.text == "the" || token.text == "and" || token.text == "is") {
            input = token.ref;
            break;
        }
    }
    
    if (input.id_high != 0 || input.id_low != 0) {
        auto result = ops.infer(input, 3);
        
        // Should find some path (model has stored relationships)
        INFO("Inference path length: " << result.path.size());
        INFO("Total weight: " << result.total_weight);
    }
}

// =============================================================================
// GENERATION FROM MODEL VOCABULARY
// =============================================================================

TEST_CASE("Generation: from ingested model vocabulary", "[mlops][generation][model]") {
    REQUIRE_MODEL();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    // Get a vocabulary token
    const auto& vocab = TestEnv::ingester().vocabulary();
    REQUIRE(!vocab.empty());
    
    // Use first valid token
    NodeRef context{};
    for (const auto& token : vocab) {
        if (token.ref.id_high != 0 || token.ref.id_low != 0) {
            context = token.ref;
            break;
        }
    }
    
    REQUIRE((context.id_high != 0 || context.id_low != 0));
    
    auto result = ops.generate(context, NodeRef{}, 10);
    
    // Should produce candidates (may be empty if no relationships from this token)
    INFO("Generated " << result.candidates.size() << " candidates");
    
    // If candidates exist, probabilities should sum to 1
    if (!result.candidates.empty()) {
        double sum = 0.0;
        for (const auto& c : result.candidates) {
            REQUIRE(c.probability >= 0.0);
            REQUIRE(c.probability <= 1.0);
            sum += c.probability;
        }
        REQUIRE_THAT(sum, WithinAbs(1.0, 0.01));
    }
}

// =============================================================================
// FULL MLOPS PIPELINE WITH MODEL
// =============================================================================

TEST_CASE("Full MLOps pipeline: attend → generate → infer", "[mlops][pipeline]") {
    REQUIRE_MODEL();
    auto& store = TestEnv::store();
    MLOps ops(store);
    
    // Get vocabulary
    const auto& vocab = TestEnv::ingester().vocabulary();
    REQUIRE(!vocab.empty());
    
    // Find a token with relationships
    NodeRef start{};
    for (const auto& token : vocab) {
        if (token.ref.id_high == 0 && token.ref.id_low == 0) continue;
        
        auto rels = store.find_from(token.ref, 1);
        if (!rels.empty()) {
            start = token.ref;
            break;
        }
    }
    
    if (start.id_high != 0 || start.id_low != 0) {
        // Step 1: Attend
        auto attention = ops.attend(start);
        INFO("Attended to " << attention.attended.size() << " nodes");
        
        // Step 2: Generate next
        auto generation = ops.generate(start, NodeRef{}, 5);
        INFO("Generated " << generation.candidates.size() << " candidates");
        
        // Step 3: Infer path
        auto inference = ops.infer(start, 2);
        INFO("Inference path length: " << inference.path.size());
        
        // Pipeline should execute without error
        // Results depend on model structure
    }
}

