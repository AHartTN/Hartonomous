/// GEOMETRY & SPATIAL TESTS
///
/// Tests for PostGIS-compatible geometry output and semantic spatial operations.
/// Focused on: WKT output validity, distance calculations, path-based similarity.

#include <catch2/catch_test_macros.hpp>
#include <catch2/matchers/catch_matchers_floating_point.hpp>

#include "atoms/geometry.hpp"
#include "atoms/semantic_decompose.hpp"
#include "atoms/semantic_hilbert.hpp"
#include "hilbert/hilbert_encoder.hpp"
#include <cstring>

using namespace hartonomous;
using Catch::Matchers::WithinAbs;

// =============================================================================
// HILBERT ENCODING: 4D space-filling curve for locality preservation
// =============================================================================

TEST_CASE("Hilbert encoding round-trips", "[hilbert]") {
    // Boundary values
    auto test_roundtrip = [](std::uint32_t x, std::uint32_t y, std::uint32_t z, std::uint32_t w) {
        auto id = HilbertEncoder::encode(x, y, z, w);
        auto coords = HilbertEncoder::decode(id);
        REQUIRE(coords[0] == x);
        REQUIRE(coords[1] == y);
        REQUIRE(coords[2] == z);
        REQUIRE(coords[3] == w);
    };
    
    // Zero
    test_roundtrip(0, 0, 0, 0);
    
    // Max
    test_roundtrip(0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF, 0xFFFFFFFF);
    
    // Mixed values
    test_roundtrip(0x12345678, 42, 0x1FFFFF, 31);
}

TEST_CASE("SemanticHilbert preserves coordinates", "[hilbert]") {
    // All page/type combinations
    for (int page = 0; page < 8; ++page) {
        for (int type = 0; type < 8; ++type) {
            SemanticCoord original{
                static_cast<std::uint8_t>(page),
                static_cast<std::uint8_t>(type),
                0x1234,
                17
            };
            
            auto id = SemanticHilbert::from_semantic(original);
            auto recovered = SemanticHilbert::to_semantic(id);
            
            REQUIRE(recovered.page == original.page);
            REQUIRE(recovered.type == original.type);
            REQUIRE(recovered.base == original.base);
            REQUIRE(recovered.variant == original.variant);
        }
    }
}

// =============================================================================
// POINTZM: 4D point with PostGIS compatibility
// =============================================================================

TEST_CASE("PointZM from semantic coordinates", "[geometry]") {
    SECTION("Coordinate mapping") {
        // Page → X (0-7 → 0-875)
        REQUIRE(PointZM::from_semantic({0, 0, 0, 0}).x == 0.0);
        REQUIRE(PointZM::from_semantic({7, 0, 0, 0}).x == 875.0);
        
        // Type → Y
        REQUIRE(PointZM::from_semantic({0, 7, 0, 0}).y == 875.0);
        
        // Base → Z (max Unicode → ~1000)
        REQUIRE_THAT(PointZM::from_semantic({0, 0, 0x10FFFF, 0}).z, WithinAbs(1000.0, 1.0));
        
        // Variant → M (0-31 → 0-~1000)
        REQUIRE_THAT(PointZM::from_semantic({0, 0, 0, 31}).m, WithinAbs(1000.0, 1.0));
    }
    
    SECTION("Distance calculations") {
        PointZM a{0, 0, 0, 0};
        PointZM b{3, 4, 0, 0};
        
        REQUIRE(a.distance_squared(a) == 0.0);
        REQUIRE(a.distance_squared(b) == 25.0);  // 3² + 4²
        REQUIRE(a.distance_squared(b) == b.distance_squared(a));  // Symmetric
    }
    
    SECTION("WKT output") {
        PointZM pt{1.5, 2.5, 3.5, 4.5};
        char buffer[128];
        pt.to_wkt(buffer, sizeof(buffer));
        
        REQUIRE(std::strncmp(buffer, "POINT ZM (", 10) == 0);
        REQUIRE(std::strstr(buffer, ")") != nullptr);
    }
}

// =============================================================================
// LINESTRINGZM: Path through semantic space
// =============================================================================

TEST_CASE("LineStringZM operations", "[geometry]") {
    SECTION("Basic usage") {
        LineStringZM<64> line;
        REQUIRE(line.count == 0);
        
        line.push_codepoint('H');
        line.push_codepoint('i');
        REQUIRE(line.count == 2);
    }
    
    SECTION("Capacity limit") {
        LineStringZM<3> line;
        REQUIRE(line.push_back(PointZM{}) == true);
        REQUIRE(line.push_back(PointZM{}) == true);
        REQUIRE(line.push_back(PointZM{}) == true);
        REQUIRE(line.push_back(PointZM{}) == false);  // Full
    }
    
    SECTION("Path length") {
        LineStringZM<64> line;
        line.push_back(PointZM{0, 0, 0, 0});
        line.push_back(PointZM{3, 0, 0, 0});  // +3
        line.push_back(PointZM{3, 4, 0, 0});  // +4
        
        REQUIRE_THAT(line.length(), WithinAbs(7.0, 0.001));
    }
    
    SECTION("WKT output") {
        LineStringZM<64> line;
        char buffer[256];
        
        // Empty
        line.to_wkt(buffer, sizeof(buffer));
        REQUIRE(std::strcmp(buffer, "LINESTRING ZM EMPTY") == 0);
        
        // With points
        line.push_back(PointZM{1, 2, 3, 4});
        line.push_back(PointZM{5, 6, 7, 8});
        line.to_wkt(buffer, sizeof(buffer));
        REQUIRE(std::strncmp(buffer, "LINESTRING ZM (", 15) == 0);
        REQUIRE(std::strstr(buffer, ", ") != nullptr);  // Comma between points
    }
}

TEST_CASE("string_to_linestring conversion", "[geometry]") {
    SECTION("Deterministic") {
        auto line1 = string_to_linestring("Hello");
        auto line2 = string_to_linestring("Hello");
        
        REQUIRE(line1.count == line2.count);
        for (std::size_t i = 0; i < line1.count; ++i) {
            REQUIRE(line1.points[i].z == line2.points[i].z);
        }
    }
    
    SECTION("Respects capacity") {
        auto line = string_to_linestring<5>("Hello World!");
        REQUIRE(line.count == 5);  // Capped
    }
}

// =============================================================================
// FRECHET DISTANCE: Path similarity metric
// =============================================================================

TEST_CASE("Fréchet distance", "[geometry][similarity]") {
    SECTION("Identical paths → zero") {
        auto line = string_to_linestring("test");
        REQUIRE_THAT(line.frechet_distance(line), WithinAbs(0.0, 0.001));
    }
    
    SECTION("Symmetric") {
        auto line1 = string_to_linestring("abc");
        auto line2 = string_to_linestring("xyz");
        
        REQUIRE_THAT(line1.frechet_distance(line2), 
                     WithinAbs(line2.frechet_distance(line1), 0.001));
    }
    
    SECTION("Similar < different") {
        auto hello = string_to_linestring("hello");
        auto hallo = string_to_linestring("hallo");
        auto world = string_to_linestring("world");
        
        REQUIRE(hello.frechet_distance(hallo) < hello.frechet_distance(world));
    }
    
    SECTION("Anagrams have nonzero distance") {
        auto cat = string_to_linestring("cat");
        auto act = string_to_linestring("act");
        
        REQUIRE(cat.frechet_distance(act) > 0.0);
    }
}

// =============================================================================
// WEIGHTED EDGE: For graph representation
// =============================================================================

TEST_CASE("WeightedEdge", "[geometry]") {
    AtomId from{100, 200};
    AtomId to{300, 400};
    WeightedEdge edge{from, to, 0.75};
    
    REQUIRE(edge.from == from);
    REQUIRE(edge.to == to);
    REQUIRE(edge.weight == 0.75);
    
    char buffer[256];
    edge.to_wkt(buffer, sizeof(buffer));
    REQUIRE(std::strncmp(buffer, "LINESTRING ZM (", 15) == 0);
}

// =============================================================================
// SEMANTIC PATH SIMILARITY: The actual use case
// =============================================================================

TEST_CASE("Case variants produce similar paths", "[geometry][semantic]") {
    auto lower = string_to_linestring("hello");
    auto upper = string_to_linestring("HELLO");
    
    // Same count
    REQUIRE(lower.count == upper.count);
    
    // Same X, Y, Z (page, type, base) but different M (variant)
    for (std::size_t i = 0; i < lower.count; ++i) {
        REQUIRE(lower.points[i].x == upper.points[i].x);
        REQUIRE(lower.points[i].y == upper.points[i].y);
        REQUIRE(lower.points[i].z == upper.points[i].z);
        REQUIRE(lower.points[i].m != upper.points[i].m);
    }
}

TEST_CASE("Related words are closer in path space", "[geometry][semantic]") {
    auto banana = string_to_linestring("banana");
    auto bandana = string_to_linestring("bandana");
    auto xyz = string_to_linestring("xyzzy");
    
    REQUIRE(banana.frechet_distance(bandana) < banana.frechet_distance(xyz));
}

