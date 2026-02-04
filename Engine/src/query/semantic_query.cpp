/**
 * @file semantic_query.cpp
 * @brief Semantic query implementation - Gravitational Truth and Relationship Traversal
 */

#include <query/semantic_query.hpp>
#include <algorithm>
#include <cctype>
#include <sstream>
#include <map>
#include <cmath>

namespace Hartonomous {

SemanticQuery::SemanticQuery(PostgresConnection& db) : db_(db) {}

std::optional<std::string> SemanticQuery::find_composition(const std::string& text) {
    auto result = db_.query_single(
        "SELECT text FROM hartonomous.composition WHERE LOWER(text) = LOWER($1) LIMIT 1",
        {text}
    );

    return result;
}

std::optional<SemanticQuery::CompositionInfo> SemanticQuery::get_composition_info(const std::string& text) {
    std::optional<CompositionInfo> info;

    db_.query(
        "SELECT id, text FROM hartonomous.composition WHERE LOWER(text) = LOWER($1) LIMIT 1",
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

    auto query_comp = get_composition_info(query_text);
    if (!query_comp) {
        return results;
    }

    std::string sql = R"(
        WITH query_relations AS (
            SELECT DISTINCT rs.relationid
            FROM hartonomous.relationsequence rs
            WHERE rs.compositionid = $1
        ),
        cooccurring AS (
            SELECT
                c.id,
                c.text,
                COUNT(DISTINCT qr.relationid) AS co_occurrence_count
            FROM query_relations qr
            JOIN hartonomous.relationsequence rs ON rs.relationid = qr.relationid
            JOIN hartonomous.composition c ON c.id = rs.compositionid
            WHERE c.id != $1
            GROUP BY c.id, c.text
            ORDER BY co_occurrence_count DESC
            LIMIT $2
        )
        SELECT text, co_occurrence_count FROM cooccurring
    )";

    db_.query(
        sql,
        {query_comp->hash, std::to_string(limit)},
        [&](const std::vector<std::string>& row) {
            QueryResult result;
            result.text = row[0];
            result.confidence = std::stod(row[1]);
            results.push_back(result);
        }
    );

    return results;
}

std::vector<QueryResult> SemanticQuery::find_gravitational_truth(const std::string& query_text, double min_elo, size_t limit) {
    std::vector<QueryResult> results;

    auto query_comp = get_composition_info(query_text);
    if (!query_comp) return results;

    // Truths Cluster, Lies Scatter.
    // 4D Gravitational Consensus:
    // - High ELO (Individual Quality)
    // - High Observations (Social Consensus)
    // - Geometric Proximity (Topological Consensus)
    std::string sql = R"(
        WITH candidates AS (
            SELECT 
                rs2.compositionid as target_id,
                c.text,
                rr.ratingvalue as elo,
                rr.observations,
                p.centroid
            FROM hartonomous.relationsequence rs1
            JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid
            JOIN hartonomous.relationrating rr ON rr.relationid = rs1.relationid
            JOIN hartonomous.composition c ON c.id = rs2.compositionid
            JOIN hartonomous.physicality p ON p.id = c.physicalityid
            WHERE rs1.compositionid = $1 AND rs2.compositionid != $1
              AND rr.ratingvalue >= $2
        ),
        clusters AS (
            SELECT 
                c1.target_id,
                c1.text,
                c1.elo,
                c1.observations,
                (
                    SELECT COUNT(*) 
                    FROM candidates c2 
                    WHERE ST_3DDistance(c1.centroid, c2.centroid) < 0.05
                ) as cluster_density
            FROM candidates c1
        )
        SELECT 
            text, 
            (elo * LOG(observations + 1) * cluster_density) as gravitational_mass
        FROM clusters
        ORDER BY gravitational_mass DESC
        LIMIT $3
    )";

    db_.query(
        sql,
        {query_comp->hash, std::to_string(min_elo), std::to_string(limit)},
        [&](const std::vector<std::string>& row) {
            QueryResult result;
            result.text = row[0];
            result.confidence = std::stod(row[1]) / 10000.0; 
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
        "SELECT relationid FROM hartonomous.relationsequence WHERE compositionid = $1",
        {comp->hash},
        [&](const std::vector<std::string>& row) {
            relation_hashes.push_back(row[0]);
        }
    );

    return relation_hashes;
}

std::vector<std::string> SemanticQuery::extract_keywords(const std::string& text) {
    std::vector<std::string> keywords;
    std::istringstream iss(text);
    std::string word;

    std::vector<std::string> stop_words = {
        "what", "is", "the", "a", "an", "of", "in", "to", "for", "on", "with", "at",
        "by", "from", "as", "and", "or", "but", "not", "this", "that", "these", "those"
    };

    while (iss >> word) {
        std::transform(word.begin(), word.end(), word.begin(), ::tolower);
        word.erase(std::remove_if(word.begin(), word.end(), ::ispunct), word.end());
        if (std::find(stop_words.begin(), stop_words.end(), word) != stop_words.end()) continue;
        if (word.empty()) continue;
        keywords.push_back(word);
    }

    return keywords;
}

bool SemanticQuery::is_proper_noun(const std::string& text) {
    if (text.empty()) return false;
    return std::isupper(text[0]);
}

std::optional<QueryResult> SemanticQuery::answer_question(const std::string& question) {
    auto keywords = extract_keywords(question);
    if (keywords.empty()) return std::nullopt;

    std::map<std::string, double> composition_scores;

    for (const auto& keyword : keywords) {
        auto related = find_related(keyword, 20);
        for (const auto& result : related) {
            double score = result.confidence;
            if (is_proper_noun(result.text)) score *= 2.0;
            composition_scores[result.text] += score;
        }
    }

    if (composition_scores.empty()) return std::nullopt;

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
