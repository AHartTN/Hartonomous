/// =============================================================================
/// SEMANTIC LINKER TESTS  
/// Tests for cross-lingual concept relationships, translations, synonyms
/// =============================================================================

#include <catch2/catch_test_macros.hpp>
#include "test_fixture.hpp"
#include "db/semantic_linker.hpp"

using namespace hartonomous::db;
using namespace hartonomous::test;

TEST_CASE("SemanticLinker concept encoding", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    SECTION("encode creates valid concept") {
        auto c = linker.encode("cat", "en");
        
        REQUIRE(c.ref.id_high != 0);
        REQUIRE(c.text == "cat");
        REQUIRE(c.language == "en");
    }
    
    SECTION("identical text produces identical ref") {
        auto c1 = linker.encode("dog", "en");
        auto c2 = linker.encode("dog", "en");
        
        REQUIRE(c1.ref.id_high == c2.ref.id_high);
        REQUIRE(c1.ref.id_low == c2.ref.id_low);
    }
    
    SECTION("different text produces different ref") {
        auto c1 = linker.encode("cat", "en");
        auto c2 = linker.encode("dog", "en");
        
        bool same = (c1.ref.id_high == c2.ref.id_high) && (c1.ref.id_low == c2.ref.id_low);
        REQUIRE_FALSE(same);
    }
    
    SECTION("Unicode text supported") {
        // Use std::string for UTF-8 to avoid C++20 char8_t issues
        std::string chinese_cat = "\xE7\x8C\xAB";  // UTF-8 for 猫
        auto c = linker.encode(chinese_cat, "zh");
        REQUIRE(c.ref.id_high != 0);
        REQUIRE(c.text == chinese_cat);
        REQUIRE(c.language == "zh");
    }
}

TEST_CASE("SemanticLinker translation links", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    auto cat_en = linker.encode("cat", "en");
    auto gato_es = linker.encode("gato", "es");
    auto chat_fr = linker.encode("chat", "fr");
    
    SECTION("link_translation creates bidirectional relationship") {
        linker.link_translation(cat_en, gato_es, 1.0);
        
        auto translations = linker.find_translations(cat_en);
        REQUIRE(translations.size() >= 1);
        
        // Should find gato from cat
        bool found = false;
        for (const auto& ref : translations) {
            if (ref.id_high == gato_es.ref.id_high && ref.id_low == gato_es.ref.id_low) {
                found = true;
                break;
            }
        }
        REQUIRE(found);
    }
    
    SECTION("multiple translations") {
        linker.link_translation(cat_en, gato_es, 1.0);
        linker.link_translation(cat_en, chat_fr, 1.0);
        
        auto translations = linker.find_translations(cat_en);
        REQUIRE(translations.size() >= 2);
    }
}

TEST_CASE("SemanticLinker synonym links", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    auto big = linker.encode("big", "en");
    auto large = linker.encode("large", "en");
    auto huge = linker.encode("huge", "en");
    
    SECTION("link synonyms") {
        linker.link_synonym(big, large, 0.95);
        linker.link_synonym(big, huge, 0.85);
        
        auto rels = linker.find_semantic(big);
        REQUIRE(rels.size() >= 2);
        
        // Check weights are preserved
        for (const auto& rel : rels) {
            if (rel.to.id_high == large.ref.id_high && rel.to.id_low == large.ref.id_low) {
                REQUIRE(rel.weight > 0.9);
            }
        }
    }
}

TEST_CASE("SemanticLinker antonym links", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    auto hot = linker.encode("hot", "en");
    auto cold = linker.encode("cold", "en");
    
    SECTION("antonyms have negative weight") {
        linker.link_antonym(hot, cold);
        
        auto rels = linker.find_semantic(hot);
        
        bool found = false;
        for (const auto& rel : rels) {
            if (rel.to.id_high == cold.ref.id_high && rel.to.id_low == cold.ref.id_low) {
                REQUIRE(rel.weight < 0);  // Antonyms have negative weight
                found = true;
                break;
            }
        }
        REQUIRE(found);
    }
}

TEST_CASE("SemanticLinker hierarchy links", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    auto animal = linker.encode("animal", "en");
    auto cat = linker.encode("cat_animal", "en");  // Unique name to avoid conflicts
    auto siamese = linker.encode("siamese", "en");
    
    SECTION("hypernym/hyponym relationships") {
        linker.link_hierarchy(animal, cat);
        linker.link_hierarchy(cat, siamese);
        
        // Cat should link to animal as hypernym
        auto cat_rels = linker.find_semantic(cat);
        
        bool has_hypernym = false;
        bool has_hyponym = false;
        
        for (const auto& rel : cat_rels) {
            if (rel.rel_type == static_cast<std::int16_t>(SemanticType::HYPERNYM)) {
                has_hypernym = true;
            }
            if (rel.rel_type == static_cast<std::int16_t>(SemanticType::HYPONYM)) {
                has_hyponym = true;
            }
        }
        
        REQUIRE(has_hypernym);  // cat → animal
        REQUIRE(has_hyponym);   // cat → siamese
    }
}

TEST_CASE("SemanticLinker related concepts", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    auto cat = linker.encode("cat_related", "en");
    auto whiskers = linker.encode("whiskers", "en");
    auto meow = linker.encode("meow", "en");
    
    SECTION("related concepts with varying weights") {
        linker.link_related(cat, whiskers, 0.8);
        linker.link_related(cat, meow, 0.9);
        
        auto rels = linker.find_semantic(cat);
        
        // Should have related links with correct weights
        for (const auto& rel : rels) {
            if (rel.rel_type == static_cast<std::int16_t>(SemanticType::RELATED)) {
                REQUIRE(rel.weight > 0);
                REQUIRE(rel.weight <= 1.0);
            }
        }
    }
}

TEST_CASE("SemanticLinker bulk operations", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    SECTION("bulk_link_translations") {
        std::vector<std::pair<std::string, std::string>> pairs = {
            {"one", "uno"},
            {"two", "dos"},
            {"three", "tres"},
            {"four", "cuatro"},
            {"five", "cinco"}
        };
        
        linker.bulk_link_translations(pairs, "en", "es", 1.0);
        
        // Verify at least one translation exists
        auto one = linker.encode("one", "en");
        auto translations = linker.find_translations(one);
        
        REQUIRE(translations.size() >= 1);
    }
}

TEST_CASE("SemanticLinker cross-lingual search", "[linker][db]") {
    REQUIRE_DB();
    
    SemanticLinker linker(TestEnv::store());
    
    // Build a multilingual concept graph
    auto water_en = linker.encode("water", "en");
    auto agua_es = linker.encode("agua", "es");
    auto eau_fr = linker.encode("eau", "fr");
    auto wasser_de = linker.encode("wasser", "de");
    std::string water_ja = "\xE6\xB0\xB4";  // UTF-8 for 水
    auto mizu_ja = linker.encode(water_ja, "ja");
    
    linker.link_translation(water_en, agua_es, 1.0);
    linker.link_translation(water_en, eau_fr, 1.0);
    linker.link_translation(water_en, wasser_de, 1.0);
    linker.link_translation(water_en, mizu_ja, 1.0);
    
    // From English, find all translations
    auto translations = linker.find_translations(water_en);
    REQUIRE(translations.size() >= 4);
    
    // From Japanese, should also find translations (bidirectional)
    auto jp_translations = linker.find_translations(mizu_ja);
    REQUIRE(jp_translations.size() >= 1);
}
