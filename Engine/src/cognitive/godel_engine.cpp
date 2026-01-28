/**
 * @file godel_engine.cpp
 * @brief Gödel Engine implementation
 */

#include <cognitive/godel_engine.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <sstream>

namespace Hartonomous {

GodelEngine::GodelEngine(PostgresConnection& db) : db_(db) {}

std::string GodelEngine::hash_text(const std::string& text) {
    return BLAKE3Pipeline::to_hex(BLAKE3Pipeline::hash(text));
}

ResearchPlan GodelEngine::analyze_problem(const std::string& problem) {
    ResearchPlan plan;
    plan.original_problem = problem;

    // Level 0: Check if we can solve directly
    if (is_solvable(problem)) {
        plan.total_steps = 0;
        plan.solvable_steps = 0;
        return plan;
    }

    // Level 1: What do we know?
    query_known_facts(problem);

    // Level 2: What don't we know?
    plan.knowledge_gaps = identify_knowledge_gaps(problem);

    // Level 3: Decompose problem
    plan.decomposition = decompose_problem(problem);
    plan.total_steps = plan.decomposition.size();

    // Count solvable steps
    plan.solvable_steps = 0;
    for (const auto& sub : plan.decomposition) {
        if (sub.is_solvable) plan.solvable_steps++;
    }

    // Level 4: Generate research plan
    plan.suggested_readings = generate_research_plan(plan.knowledge_gaps);

    return plan;
}

std::vector<std::string> GodelEngine::query_known_facts(const std::string& topic) {
    std::vector<std::string> facts;

    std::string sql = R"(
        SELECT c.text, se.elo_rating
        FROM hartonomous.compositions c
        JOIN hartonomous.metadata m ON m.hash = $1
        JOIN hartonomous.relation_children rc ON rc.relation_hash = m.hash
        JOIN hartonomous.compositions c2 ON c2.hash = rc.child_hash
        WHERE c2.text ILIKE '%' || $2 || '%'
        ORDER BY se.elo_rating DESC
        LIMIT 20
    )";

    db_.query(sql, {hash_text(topic), topic}, [&](const std::vector<std::string>& row) {
        facts.push_back(row[0]);
    });

    return facts;
}

std::vector<KnowledgeGap> GodelEngine::identify_knowledge_gaps(const std::string& topic) {
    std::vector<KnowledgeGap> gaps;

    std::string sql = R"(
        WITH known_facts AS (
            SELECT rc.child_hash
            FROM hartonomous.relation_children rc
            JOIN hartonomous.compositions c ON c.hash = rc.relation_hash
            WHERE c.text ILIKE '%' || $1 || '%'
        ),
        referenced_concepts AS (
            SELECT DISTINCT rc2.child_hash, COUNT(*) as ref_count
            FROM hartonomous.relations r
            JOIN hartonomous.relation_children rc1 ON rc1.relation_hash = r.hash
            JOIN hartonomous.relation_children rc2 ON rc2.relation_hash = r.hash
            WHERE rc2.child_hash NOT IN (SELECT child_hash FROM known_facts)
            GROUP BY rc2.child_hash
        )
        SELECT c.text, rc.ref_count
        FROM referenced_concepts rc
        JOIN hartonomous.compositions c ON c.hash = rc.child_hash
        ORDER BY rc.ref_count DESC
        LIMIT 20
    )";

    db_.query(sql, {topic}, [&](const std::vector<std::string>& row) {
        KnowledgeGap gap;
        gap.concept_name = row[0];
        gap.references_count = std::stoi(row[1]);
        gap.confidence = 0.0;
        gaps.push_back(gap);
    });

    return gaps;
}

std::vector<SubProblem> GodelEngine::decompose_problem(const std::string& problem) {
    std::vector<SubProblem> subproblems;

    // Simple decomposition: split on keywords
    std::vector<std::string> keywords = {"understand", "prove", "compute", "analyze"};

    int id = 0;
    for (const auto& keyword : keywords) {
        if (problem.find(keyword) != std::string::npos) {
            SubProblem sub;
            sub.node_id = id++;
            sub.description = keyword + " component of problem";
            sub.difficulty = 5;
            sub.is_solvable = false;
            subproblems.push_back(sub);
        }
    }

    // If no keywords found, create generic sub-problems
    if (subproblems.empty()) {
        for (int i = 0; i < 3; i++) {
            SubProblem sub;
            sub.node_id = i;
            sub.description = "Sub-problem " + std::to_string(i + 1);
            sub.difficulty = 5 + i;
            sub.is_solvable = false;
            subproblems.push_back(sub);
        }
    }

    return subproblems;
}

std::vector<std::string> GodelEngine::generate_research_plan(const std::vector<KnowledgeGap>& gaps) {
    std::vector<std::string> readings;

    for (const auto& gap : gaps) {
        readings.push_back("Research: " + gap.concept_name);
        if (readings.size() >= 10) break;
    }

    return readings;
}

bool GodelEngine::is_solvable(const std::string& problem) {
    // Check if we have a direct answer
    auto result = db_.query_single(
        "SELECT 1 FROM hartonomous.compositions WHERE text = $1 LIMIT 1",
        {problem}
    );

    return result.has_value();
}

} // namespace Hartonomous
