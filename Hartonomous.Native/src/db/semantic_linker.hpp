#pragma once

/// SEMANTIC LINKER - Cross-lingual concept relationships
///
/// "cat" and "gato" are different byte sequences but the SAME CONCEPT.
/// Language emerges through relationships, not through bytes.
///
/// This is WHERE the universal substrate becomes universal:
/// - Same concept, different languages → high-weight relationship
/// - Synonyms → relationship with similarity weight
/// - Antonyms → relationship with negative/opposite marker
/// - Hypernyms/hyponyms → hierarchical relationships
///
/// The compositions store WHAT. The relationships store MEANING.

#include "query_store.hpp"
#include "../atoms/grammar_encoder.hpp"
#include <string>
#include <vector>

namespace hartonomous::db {

/// Semantic relationship types for cross-lingual linking
enum class SemanticType : std::int16_t {
    TRANSLATION = 10,     // cat ↔ gato (same meaning, different language)
    SYNONYM = 11,         // big ↔ large (same meaning, same language)
    ANTONYM = 12,         // hot ↔ cold (opposite meaning)
    HYPERNYM = 13,        // animal → cat (parent concept)
    HYPONYM = 14,         // cat → animal (child concept)
    RELATED = 15,         // cat ~ whiskers (associated concepts)
    DEFINITION = 16,      // cat → "small domesticated carnivore"
};

/// Cross-lingual concept entry
struct Concept {
    NodeRef ref;
    std::string text;
    std::string language;  // ISO 639-1: "en", "es", "zh", "ja", etc.
};

/// Semantic linker for cross-lingual relationships
class SemanticLinker {
    QueryStore& store_;
    GrammarEncoder encoder_;

public:
    explicit SemanticLinker(QueryStore& store) : store_(store) {}

    /// Encode text and return concept
    [[nodiscard]] Concept encode(const std::string& text, const std::string& lang) {
        NodeRef ref = encoder_.encode(text);

        // Store compositions
        for (const auto& [parent, left, right] : encoder_.pending()) {
            store_.store_composition(parent, left, right);
        }
        encoder_.clear();

        return {ref, text, lang};
    }

    /// Link two concepts as translations (cat ↔ gato)
    void link_translation(const Concept& a, const Concept& b, double confidence = 1.0) {
        // Bidirectional translation link
        store_.store_relationship(a.ref, b.ref, confidence,
            static_cast<RelType>(SemanticType::TRANSLATION));
        store_.store_relationship(b.ref, a.ref, confidence,
            static_cast<RelType>(SemanticType::TRANSLATION));
    }

    /// Link synonyms (big ↔ large)
    void link_synonym(const Concept& a, const Concept& b, double similarity = 1.0) {
        store_.store_relationship(a.ref, b.ref, similarity,
            static_cast<RelType>(SemanticType::SYNONYM));
        store_.store_relationship(b.ref, a.ref, similarity,
            static_cast<RelType>(SemanticType::SYNONYM));
    }

    /// Link antonyms (hot ↔ cold)
    void link_antonym(const Concept& a, const Concept& b) {
        store_.store_relationship(a.ref, b.ref, -1.0,  // Negative weight = opposite
            static_cast<RelType>(SemanticType::ANTONYM));
        store_.store_relationship(b.ref, a.ref, -1.0,
            static_cast<RelType>(SemanticType::ANTONYM));
    }

    /// Link hypernym/hyponym (animal → cat)
    void link_hierarchy(const Concept& parent, const Concept& child) {
        store_.store_relationship(parent.ref, child.ref, 1.0,
            static_cast<RelType>(SemanticType::HYPONYM));
        store_.store_relationship(child.ref, parent.ref, 1.0,
            static_cast<RelType>(SemanticType::HYPERNYM));
    }

    /// Link related concepts (cat ~ whiskers)
    void link_related(const Concept& a, const Concept& b, double relatedness) {
        store_.store_relationship(a.ref, b.ref, relatedness,
            static_cast<RelType>(SemanticType::RELATED));
        store_.store_relationship(b.ref, a.ref, relatedness,
            static_cast<RelType>(SemanticType::RELATED));
    }

    /// Find translations of a concept
    [[nodiscard]] std::vector<NodeRef> find_translations(const Concept& c) {
        auto rels = store_.find_by_type(c.ref,
            static_cast<RelType>(SemanticType::TRANSLATION));

        std::vector<NodeRef> results;
        results.reserve(rels.size());
        for (const auto& r : rels) {
            results.push_back(r.to);
        }
        return results;
    }

    /// Find all semantic links from a concept
    [[nodiscard]] std::vector<Relationship> find_semantic(const Concept& c) {
        return store_.find_from(c.ref, 1000);
    }

    /// Bulk link dictionary entries (for importing multilingual dictionaries)
    void bulk_link_translations(
        const std::vector<std::pair<std::string, std::string>>& pairs,
        const std::string& lang_a,
        const std::string& lang_b,
        double confidence = 1.0)
    {
        for (const auto& [text_a, text_b] : pairs) {
            Concept a = encode(text_a, lang_a);
            Concept b = encode(text_b, lang_b);
            link_translation(a, b, confidence);
        }
    }
};

} // namespace hartonomous::db
