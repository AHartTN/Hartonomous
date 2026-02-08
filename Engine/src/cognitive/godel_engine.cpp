/**
 * @file godel_engine.cpp
 * @brief Gödel Engine implementation - Recursive Meta-Reasoning
 *
 * Five levels of self-referential reasoning:
 *   Level 0: Do I know the answer? (is_solvable)
 *   Level 1: What do I know about this? (query_known_facts)
 *   Level 2: What don't I know? (identify_knowledge_gaps)
 *   Level 3: How can I break this down? (decompose_problem)
 *   Level 4: What research is needed? (generate_research_plan)
 */

#include <cognitive/godel_engine.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <iostream>
#include <queue>
#include <algorithm>
#include <sstream>
#include <cctype>

namespace Hartonomous {

GodelEngine::GodelEngine(PostgresConnection& db) : db_(db) {}

std::string GodelEngine::hash_text(const std::string& text) {
    return BLAKE3Pipeline::to_hex(BLAKE3Pipeline::hash(text));
}

ResearchPlan GodelEngine::analyze_problem(const std::string& problem) {
    ResearchPlan plan;
    plan.original_problem = problem;

    // Use actual BLAKE3 hash for the problem statement
    BLAKE3Pipeline::Hash problem_hash = BLAKE3Pipeline::hash(problem);
    std::string problem_uuid = BLAKE3Pipeline::to_hex(problem_hash);

    // 1. Knowledge Gap Analysis (What concepts are referenced but lack strong relations?)
    plan.knowledge_gaps = identify_knowledge_gaps(problem_uuid);

    // 2. Recursive Decomposition (Break into solvable sub-problems)
    plan.decomposition = decompose_problem_recursive(problem_uuid, 0, 5);
    
    plan.total_steps = plan.decomposition.size();
    plan.solvable_steps = 0;
    for (const auto& sub : plan.decomposition) {
        if (sub.is_solvable) plan.solvable_steps++;
    }

    return plan;
}

std::vector<SubProblem> GodelEngine::decompose_problem(const std::string& problem) {
    BLAKE3Pipeline::Hash problem_hash = BLAKE3Pipeline::hash(problem);
    std::string problem_uuid = BLAKE3Pipeline::to_hex(problem_hash);
    return decompose_problem_recursive(problem_uuid, 0, 5);
}

std::vector<KnowledgeGap> GodelEngine::identify_knowledge_gaps(const std::string& problem_uuid) {
    std::vector<KnowledgeGap> gaps;

    // QUERY THE REAL SCHEMA: 
    // Find compositions related to the problem that have LOW rating values or NO evidence.
    // This identifies where the "gravitational well" is shallow.
    std::string sql = R"(
        WITH related_concepts AS (
            SELECT DISTINCT rs2.compositionid as concept_id
            FROM hartonomous.relationsequence rs1
            JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid
            WHERE rs1.compositionid = $1
        ),
        concept_strength AS (
            SELECT 
                rc.concept_id,
                v.reconstructed_text,
                COALESCE(rr.ratingvalue, 0) as rating,
                COALESCE(uint64_to_double(rr.observations), 0) as observations
            FROM related_concepts rc
            JOIN hartonomous.v_composition_text v ON v.composition_id = rc.concept_id
            LEFT JOIN hartonomous.relationrating rr ON rr.relationid = rc.concept_id
        )
        SELECT reconstructed_text, observations, rating
        FROM concept_strength
        WHERE rating < 1200 OR observations < 5
        ORDER BY rating ASC
        LIMIT 10
    )";

    db_.query(sql, {problem_uuid}, [&](const std::vector<std::string>& row) {
        KnowledgeGap gap;
        gap.concept_name = row[0];
        gap.references_count = std::stoi(row[1]);
        gap.confidence = std::stod(row[2]) / 2000.0;
        gaps.push_back(gap);
    });

    return gaps;
}

std::vector<SubProblem> GodelEngine::decompose_problem_recursive(const std::string& current_id, int depth, int max_depth) {
    if (depth >= max_depth) return {};

    std::vector<SubProblem> subproblems;

    // Recursive traversal: Find children and their ratings
    std::string sql = R"(
        SELECT 
            rs2.compositionid,
            v.reconstructed_text,
            COALESCE(rr.ratingvalue, 0),
            COALESCE(uint64_to_double(rr.observations), 0)
        FROM hartonomous.relationsequence rs1
        JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid
        JOIN hartonomous.v_composition_text v ON v.composition_id = rs2.compositionid
        LEFT JOIN hartonomous.relationrating rr ON rr.relationid = rs1.relationid
        WHERE rs1.compositionid = $1 AND rs2.compositionid != $1
        ORDER BY rr.ratingvalue DESC
    )";

    db_.query(sql, {current_id}, [&](const std::vector<std::string>& row) {
        SubProblem sub;
        sub.node_id = BLAKE3Pipeline::from_hex(row[0]);
        sub.description = row[1];
        double rating = std::stod(row[2]);
        sub.difficulty = static_cast<int>(10.0 * (1.0 - (rating / 2000.0)));
        sub.is_solvable = (rating > 1800);
        
        // Find prerequisites for this sub-problem
        std::string prereq_sql = "SELECT v.reconstructed_text FROM hartonomous.relationsequence rs "
                                "JOIN hartonomous.v_composition_text v ON v.composition_id = rs.compositionid "
                                "WHERE rs.relationid = (SELECT relationid FROM hartonomous.relationsequence "
                                "WHERE compositionid = $1 LIMIT 1) AND rs.compositionid != $1";
        
        db_.query(prereq_sql, {row[0]}, [&](const std::vector<std::string>& p_row) {
            sub.prerequisites.push_back(p_row[0]);
        });

        subproblems.push_back(sub);

        if (!sub.is_solvable) {
            auto children = decompose_problem_recursive(row[0], depth + 1, max_depth);
            subproblems.insert(subproblems.end(), children.begin(), children.end());
        }
    });

    return subproblems;
}

// =============================================================================
// Level 0: Is the problem solvable with current knowledge?
// =============================================================================

bool GodelEngine::is_solvable(const std::string& problem) {
    // Extract keywords from the problem
    std::istringstream iss(problem);
    std::string word;
    std::vector<std::string> keywords;

    while (iss >> word) {
        // Strip punctuation, lowercase
        word.erase(std::remove_if(word.begin(), word.end(), ::ispunct), word.end());
        if (word.empty()) continue;
        std::transform(word.begin(), word.end(), word.begin(), ::tolower);

        // Skip stop words
        static const std::vector<std::string> stops = {
            "what", "is", "the", "a", "an", "of", "in", "to", "for", "on", "with",
            "at", "by", "from", "as", "and", "or", "but", "not", "how", "why", "can",
            "do", "does", "did", "will", "would", "could", "should", "this", "that"
        };
        if (std::find(stops.begin(), stops.end(), word) != stops.end()) continue;
        keywords.push_back(word);
    }

    if (keywords.empty()) return false;

    // Check if we have high-confidence relations between the key concepts
    // Solvable = at least one keyword has strong relations (ELO > 1500, obs > 10)
    int strong_concepts = 0;

    for (const auto& kw : keywords) {
        auto result = db_.query_single(
            "SELECT COUNT(*) FROM hartonomous.v_composition_text v "
            "JOIN hartonomous.relationsequence rs ON rs.compositionid = v.composition_id "
            "JOIN hartonomous.relationrating rr ON rr.relationid = rs.relationid "
            "WHERE LOWER(v.reconstructed_text) = $1 "
            "  AND rr.ratingvalue > 1500 "
            "  AND uint64_to_double(rr.observations) > 10",
            {kw}
        );
        if (result && std::stoi(*result) > 0) {
            strong_concepts++;
        }
    }

    // Solvable if majority of keywords have strong substrate presence
    return strong_concepts > 0 && static_cast<double>(strong_concepts) / keywords.size() >= 0.3;
}

// =============================================================================
// Level 1: What do we know about this topic?
// =============================================================================

std::vector<std::string> GodelEngine::query_known_facts(const std::string& topic) {
    std::vector<std::string> facts;

    // Find the topic composition
    std::string comp_id;
    db_.query(
        "SELECT v.composition_id FROM hartonomous.v_composition_text v "
        "WHERE LOWER(v.reconstructed_text) = LOWER($1) LIMIT 1",
        {topic},
        [&](const std::vector<std::string>& row) { comp_id = row[0]; }
    );

    if (comp_id.empty()) return facts;

    // Find strongly related concepts (high ELO, high observations)
    // These represent "known facts" — well-evidenced, high-confidence relations
    db_.query(
        R"(SELECT DISTINCT
            v2.reconstructed_text,
            rr.ratingvalue,
            uint64_to_double(rr.observations) as obs
        FROM hartonomous.relationsequence rs1
        JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid
            AND rs2.compositionid != rs1.compositionid
        JOIN hartonomous.relationrating rr ON rr.relationid = rs1.relationid
        JOIN hartonomous.v_composition_text v2 ON v2.composition_id = rs2.compositionid
        WHERE rs1.compositionid = $1
          AND rr.ratingvalue > 1400
          AND uint64_to_double(rr.observations) > 5
        ORDER BY rr.ratingvalue * LOG(uint64_to_double(rr.observations) + 1) DESC
        LIMIT 20)",
        {comp_id},
        [&](const std::vector<std::string>& row) {
            facts.push_back(row[0]);
        }
    );

    return facts;
}

// =============================================================================
// Level 4: Generate research plan for knowledge gaps
// =============================================================================

std::vector<std::string> GodelEngine::generate_research_plan(const std::vector<KnowledgeGap>& gaps) {
    std::vector<std::string> plan;

    if (gaps.empty()) {
        plan.push_back("No knowledge gaps identified — problem appears well-covered in substrate.");
        return plan;
    }

    // Sort gaps by severity (lowest confidence first)
    std::vector<KnowledgeGap> sorted = gaps;
    std::sort(sorted.begin(), sorted.end(),
        [](const KnowledgeGap& a, const KnowledgeGap& b) { return a.confidence < b.confidence; });

    for (const auto& gap : sorted) {
        std::string action;

        if (gap.confidence < 0.1) {
            // Near-zero confidence: concept exists but has almost no relations
            action = "CRITICAL: '" + gap.concept_name + "' has minimal substrate presence "
                     "(confidence=" + std::to_string(gap.confidence) + "). "
                     "Requires new ingestion from authoritative sources.";
        } else if (gap.confidence < 0.5) {
            // Low confidence: some relations exist but quality is poor
            action = "WEAK: '" + gap.concept_name + "' has low-quality relations "
                     "(confidence=" + std::to_string(gap.confidence) + "). "
                     "Needs additional evidence from diverse sources to strengthen.";

            // Check what sources we DO have
            std::string comp_id;
            db_.query(
                "SELECT v.composition_id FROM hartonomous.v_composition_text v "
                "WHERE LOWER(v.reconstructed_text) = LOWER($1) LIMIT 1",
                {gap.concept_name},
                [&](const std::vector<std::string>& row) { comp_id = row[0]; }
            );

            if (!comp_id.empty()) {
                int source_count = 0;
                db_.query(
                    "SELECT COUNT(DISTINCT re.contentid) "
                    "FROM hartonomous.relationsequence rs "
                    "JOIN hartonomous.relationevidence re ON re.relationid = rs.relationid "
                    "WHERE rs.compositionid = $1",
                    {comp_id},
                    [&](const std::vector<std::string>& row) {
                        source_count = std::stoi(row[0]);
                    }
                );
                action += " Currently backed by " + std::to_string(source_count) + " source(s).";
            }
        } else {
            // Moderate confidence: exists but could be stronger
            action = "MODERATE: '" + gap.concept_name + "' has adequate but improvable coverage "
                     "(confidence=" + std::to_string(gap.confidence) + "). "
                     "Would benefit from specialized domain sources.";
        }

        plan.push_back(action);
    }

    return plan;
}

} // namespace Hartonomous