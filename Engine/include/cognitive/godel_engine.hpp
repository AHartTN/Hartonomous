/**
 * @file godel_engine.hpp
 * @brief Gödel Engine: Meta-reasoning for unsolvable problems
 *
 * Implements self-referential reasoning to:
 * - Identify knowledge gaps
 * - Decompose complex problems
 * - Generate research plans
 * - Track provability
 */

#pragma once

#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <vector>
#include <string>
#include <optional>
#include <map>

namespace Hartonomous {

struct KnowledgeGap {
    std::string concept_name;  // Renamed from 'concept' (C++20 keyword)
    int references_count;
    double confidence;
};

struct SubProblem {
    BLAKE3Pipeline::Hash node_id;
    std::string description;
    int difficulty;  // 1-10
    bool is_solvable;
    std::vector<std::string> prerequisites;
};

struct ResearchPlan {
    std::string original_problem;
    std::vector<SubProblem> decomposition;
    std::vector<KnowledgeGap> knowledge_gaps;
    std::vector<std::string> suggested_readings;
    int total_steps;
    int solvable_steps;
};

/**
 * @brief Gödel Engine for meta-reasoning
 *
 * Implements recursive self-referential reasoning:
 * Level 0: Do I know the answer?
 * Level 1: What do I know about this?
 * Level 2: What don't I know?
 * Level 3: How can I break this down?
 * Level 4: What research is needed?
 */
class GodelEngine {
public:
    explicit GodelEngine(PostgresConnection& db);

    /**
     * @brief Analyze a problem and generate meta-reasoning plan
     *
     * @param problem Problem statement
     * @return ResearchPlan Complete analysis with gaps and decomposition
     */
    ResearchPlan analyze_problem(const std::string& problem);

    /**
     * @brief Level 1: What do we know?
     */
    std::vector<std::string> query_known_facts(const std::string& topic);

    /**
     * @brief Level 2: What don't we know?
     */
    std::vector<KnowledgeGap> identify_knowledge_gaps(const std::string& topic);

    /**
     * @brief Level 3: Decompose into sub-problems
     */
    std::vector<SubProblem> decompose_problem(const std::string& problem);

    /**
     * @brief Level 4: Generate research plan
     */
    std::vector<std::string> generate_research_plan(const std::vector<KnowledgeGap>& gaps);

    /**
     * @brief Check if problem is solvable with current knowledge
     */
    bool is_solvable(const std::string& problem);

private:
    PostgresConnection& db_;

    std::string hash_text(const std::string& text);

    std::vector<SubProblem> decompose_problem_recursive(const std::string& current_id, int depth, int max_depth);
};

} // namespace Hartonomous
