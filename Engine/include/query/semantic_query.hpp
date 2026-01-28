/**
 * @file semantic_query.hpp
 * @brief Semantic query engine - Find answers via relationship traversal
 */

#pragma once

#include <database/postgres_connection.hpp>
#include <string>
#include <vector>
#include <optional>

namespace Hartonomous {

/**
 * @brief Query result
 */
struct QueryResult {
    std::string text;
    double confidence;  // ELO-based or co-occurrence count
    std::vector<std::string> provenance;  // Source relations
};

/**
 * @brief Semantic query engine
 *
 * Queries use relationship traversal, NOT spatial proximity:
 * - Find compositions by text
 * - Find relations containing composition
 * - Find co-occurring compositions (semantic similarity)
 * - Rank by ELO or co-occurrence count
 */
class SemanticQuery {
public:
    explicit SemanticQuery(PostgresConnection& db);

    /**
     * @brief Simple query: Find composition by exact text match
     */
    std::optional<std::string> find_composition(const std::string& text);

    /**
     * @brief Find compositions that co-occur with query text
     *
     * Example: "Captain" → finds "Ahab", "ship", "Pequod"
     *
     * Algorithm:
     * 1. Find all relations containing "Captain"
     * 2. Find all other compositions in those relations
     * 3. Rank by co-occurrence count
     */
    std::vector<QueryResult> find_related(const std::string& query_text, size_t limit = 10);

    /**
     * @brief Answer question via relationship traversal
     *
     * Example: "What is the captain's name?" → "Ahab"
     *
     * Algorithm:
     * 1. Extract key terms from question ("captain", "name")
     * 2. Find compositions for each term
     * 3. Find relations containing those compositions
     * 4. Find compositions that co-occur frequently
     * 5. Filter by context (proper nouns for "name" question)
     * 6. Return highest ranked answer
     */
    std::optional<QueryResult> answer_question(const std::string& question);

    /**
     * @brief Find all relations containing a composition
     */
    std::vector<std::string> find_relations_containing(const std::string& composition_text);

    struct CompositionInfo {
        std::string hash;
        std::string text;
    };

    std::optional<CompositionInfo> get_composition_info(const std::string& text);

private:
    std::vector<std::string> extract_keywords(const std::string& text);
    bool is_proper_noun(const std::string& text);

    PostgresConnection& db_;
};

} // namespace Hartonomous
