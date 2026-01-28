/**
 * @file semantic_query.cpp
 * @brief Semantic query implementation
 */

#include <query/semantic_query.hpp>
#include <algorithm>
#include <cctype>
#include <sstream>
#include <map>

namespace Hartonomous {

SemanticQuery::SemanticQuery(PostgresConnection& db) : db_(db) {}

std::optional<std::string> SemanticQuery::find_composition(const std::string& text) {
    auto result = db_.query_single(
        "SELECT text FROM hartonomous.compositions WHERE LOWER(text) = LOWER($1) LIMIT 1",
        {text}
    );

    return result;
}

std::optional<SemanticQuery::CompositionInfo> SemanticQuery::get_composition_info(const std::string& text) {
    std::optional<CompositionInfo> info;

    db_.query(
        "SELECT hash, text FROM hartonomous.compositions WHERE LOWER(text) = LOWER($1) LIMIT 1",
        {text},
        [&](const std::vector<std::string>& row) {
            CompositionInfo comp;
            comp.hash = row[0];
            comp.text = row[1];
            info = comp;
        }
    );

    return info;
}

std::vector<QueryResult> SemanticQuery::find_related(const std::string& query_text, size_t limit) {
    std::vector<QueryResult> results;

    // Get composition info for query
    auto query_comp = get_composition_info(query_text);
    if (!query_comp) {
        return results;  // Not found
    }

    // Find all relations containing this composition
    std::string sql = R"(
        WITH query_relations AS (
            -- Find all relations containing the query composition
            SELECT DISTINCT rc.relation_hash
            FROM hartonomous.relation_children rc
            WHERE rc.child_hash = $1
        ),
        cooccurring_compositions AS (
            -- Find all compositions in those relations
            SELECT
                c.hash,
                c.text,
                COUNT(DISTINCT qr.relation_hash) AS co_occurrence_count
            FROM query_relations qr
            JOIN hartonomous.relation_children rc ON rc.relation_hash = qr.relation_hash
            JOIN hartonomous.compositions c ON c.hash = rc.child_hash
            WHERE c.hash != $1  -- Exclude query itself
            GROUP BY c.hash, c.text
            ORDER BY co_occurrence_count DESC
            LIMIT $2
        )
        SELECT text, co_occurrence_count FROM cooccurring_compositions
    )";

    db_.query(
        sql,
        {query_comp->hash, std::to_string(limit)},
        [&](const std::vector<std::string>& row) {
            QueryResult result;
            result.text = row[0];
            result.confidence = std::stod(row[1]);  // Co-occurrence count
            results.push_back(result);
        }
    );

    return results;
}

std::vector<std::string> SemanticQuery::find_relations_containing(const std::string& composition_text) {
    std::vector<std::string> relation_hashes;

    auto comp = get_composition_info(composition_text);
    if (!comp) return relation_hashes;

    db_.query(
        "SELECT relation_hash FROM hartonomous.relation_children WHERE child_hash = $1",
        {comp->hash},
        [&](const std::vector<std::string>& row) {
            relation_hashes.push_back(row[0]);
        }
    );

    return relation_hashes;
}

std::vector<std::string> SemanticQuery::extract_keywords(const std::string& text) {
    std::vector<std::string> keywords;

    // Simple keyword extraction: remove common words, split on whitespace
    std::istringstream iss(text);
    std::string word;

    // Common stop words to skip
    std::vector<std::string> stop_words = {
        "what", "is", "the", "a", "an", "of", "in", "to", "for", "on", "with", "at",
        "by", "from", "as", "and", "or", "but", "not", "this", "that", "these", "those"
    };

    while (iss >> word) {
        // Convert to lowercase
        std::transform(word.begin(), word.end(), word.begin(), ::tolower);

        // Remove punctuation
        word.erase(std::remove_if(word.begin(), word.end(), ::ispunct), word.end());

        // Skip stop words
        if (std::find(stop_words.begin(), stop_words.end(), word) != stop_words.end()) {
            continue;
        }

        // Skip empty
        if (word.empty()) continue;

        keywords.push_back(word);
    }

    return keywords;
}

bool SemanticQuery::is_proper_noun(const std::string& text) {
    if (text.empty()) return false;

    // Simple heuristic: starts with capital letter
    return std::isupper(text[0]);
}

std::optional<QueryResult> SemanticQuery::answer_question(const std::string& question) {
    // Extract keywords from question
    auto keywords = extract_keywords(question);

    if (keywords.empty()) {
        return std::nullopt;
    }

    // Find compositions for each keyword
    std::map<std::string, int> composition_scores;

    for (const auto& keyword : keywords) {
        // Find compositions related to this keyword
        auto related = find_related(keyword, 20);

        for (const auto& result : related) {
            // Give higher weight to proper nouns (likely answers)
            int score = (int)result.confidence;
            if (is_proper_noun(result.text)) {
                score *= 2;
            }

            composition_scores[result.text] += score;
        }
    }

    // Find highest scoring composition
    if (composition_scores.empty()) {
        return std::nullopt;
    }

    auto best = std::max_element(
        composition_scores.begin(),
        composition_scores.end(),
        [](const auto& a, const auto& b) {
            return a.second < b.second;
        }
    );

    QueryResult result;
    result.text = best->first;
    result.confidence = best->second;

    return result;
}

} // namespace Hartonomous
