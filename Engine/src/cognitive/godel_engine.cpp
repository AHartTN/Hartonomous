/**
 * @file godel_engine.cpp
 * @brief Gödel Engine implementation - Recursive Meta-Reasoning
 */

#include <cognitive/godel_engine.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <iostream>
#include <queue>

namespace Hartonomous {

GodelEngine::GodelEngine(PostgresConnection& db) : db_(db) {}

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
                c.text,
                COALESCE(rr.ratingvalue, 0) as rating,
                COALESCE(rr.observations, 0) as observations
            FROM related_concepts rc
            JOIN hartonomous.composition c ON c.id = rc.concept_id
            LEFT JOIN hartonomous.relationrating rr ON rr.relationid = rc.concept_id
        )
        SELECT text, observations, rating
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
            c.text,
            COALESCE(rr.ratingvalue, 0),
            COALESCE(rr.observations, 0)
        FROM hartonomous.relationsequence rs1
        JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid
        JOIN hartonomous.composition c ON c.id = rs2.compositionid
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
        std::string prereq_sql = "SELECT c.text FROM hartonomous.relationsequence rs "
                                "JOIN hartonomous.composition c ON c.id = rs.compositionid "
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

} // namespace Hartonomous