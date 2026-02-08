/**
 * @file reasoning_engine.hpp
 * @brief Reasoning Engine: OODA + BDI + Tree of Thought + Reflexion
 *
 * This is the cognitive orchestration layer that transforms a prompt into
 * a coherent response by composing all substrate engines:
 *
 *   OBSERVE:  Parse prompt → extract keywords → find seed compositions
 *   ORIENT:   Gödel decomposes problem → identify sub-goals + knowledge gaps
 *   DECIDE:   BDI selects intentions → A* plans paths to sub-goals
 *   ACT:      Tree of Thought: K parallel searches, scored by path quality
 *   REFLECT:  Evaluate output coherence → re-search if below threshold
 *
 * BDI Framework:
 *   Beliefs  = Substrate state (what relations exist, their ELO/observations)
 *   Desires  = User intent extracted from prompt (what they want answered)
 *   Intentions = Sub-goals from Gödel decomposition, prioritized by solvability
 */

#pragma once

#include <cognitive/walk_engine.hpp>
#include <cognitive/astar_search.hpp>
#include <cognitive/godel_engine.hpp>
#include <query/semantic_query.hpp>
#include <database/postgres_connection.hpp>
#include <export.hpp>
#include <string>
#include <vector>
#include <optional>
#include <functional>

namespace Hartonomous {

// =============================================================================
// Configuration
// =============================================================================

struct ReasoningConfig {
    // Tree of Thought
    size_t beam_width = 4;           // Parallel hypotheses to maintain
    size_t max_depth = 8;            // Maximum reasoning depth per hypothesis

    // A* search
    AStarConfig astar;               // Defaults are sensible

    // Walk engine (for creative/generative passages)
    WalkParameters walk;             // Defaults from walk_engine.hpp
    size_t walk_max_steps = 40;      // Max walk steps per passage

    // Reflexion
    double min_path_quality = 0.3;   // Minimum average ELO quality (normalized)
    int max_reflexion_rounds = 3;    // Maximum re-search attempts

    // Response assembly
    size_t max_response_words = 200; // Target response length
    bool include_reasoning_trace = false; // Include "[path: X→Y→Z]" annotations

    // System prompt (injected context for reasoning)
    std::string system_prompt;

    // Conversation history (for multi-turn)
    std::vector<std::pair<std::string, std::string>> history; // (role, content)
};

// =============================================================================
// Internal structures
// =============================================================================

struct Intention {
    std::string description;          // What this intention aims to resolve
    BLAKE3Pipeline::Hash target_id;   // Goal composition
    double priority;                  // Higher = more important (from Gödel difficulty)
    bool resolved = false;
    AStarPath path;                   // Filled during ACT phase
};

struct Hypothesis {
    std::vector<Intention> intentions;
    std::vector<AStarPath> paths;     // Resolved paths for each intention
    std::string assembled_text;       // Assembled response from this hypothesis
    double quality_score = 0.0;       // Reflexion quality metric
};

struct ReasoningResult {
    std::string response;             // Final assembled text
    double confidence;                // Overall confidence (0-1)
    size_t intentions_resolved;       // How many sub-goals were answered
    size_t intentions_total;          // Total sub-goals identified
    size_t reflexion_rounds;          // How many re-search rounds occurred
    size_t nodes_expanded;            // Total A* nodes expanded
    std::vector<std::string> reasoning_trace; // Optional trace
};

// =============================================================================
// Streaming callback
// =============================================================================

using ReasoningStreamCallback = std::function<bool(const std::string& token, size_t step)>;

// =============================================================================
// The Engine
// =============================================================================

class HARTONOMOUS_API ReasoningEngine {
public:
    explicit ReasoningEngine(PostgresConnection& db);

    /**
     * @brief Full reasoning pipeline: prompt → response
     *
     * OODA loop:
     *   1. OBSERVE: Parse prompt, extract seed compositions
     *   2. ORIENT:  Gödel decomposition → sub-goals + knowledge gaps
     *   3. DECIDE:  BDI: prioritize intentions, plan A* paths
     *   4. ACT:     Tree of Thought: K parallel path searches
     *   5. REFLECT: Evaluate quality, re-search if needed
     *   6. ASSEMBLE: Stitch paths + walk passages into coherent text
     */
    ReasoningResult reason(const std::string& prompt,
                           const ReasoningConfig& config = {});

    /**
     * @brief Streaming variant — calls back with each token as generated
     */
    ReasoningResult reason_stream(const std::string& prompt,
                                  ReasoningStreamCallback callback,
                                  const ReasoningConfig& config = {});

    /**
     * @brief Quick answer — skip full reasoning, use co-occurrence + A*
     *
     * For simple factual queries. Falls back to full reason() if no direct answer.
     */
    ReasoningResult quick_answer(const std::string& prompt,
                                 const ReasoningConfig& config = {});

private:
    // OODA phases
    struct Observation {
        std::string prompt;
        std::string system_context;
        std::vector<std::string> keywords;
        std::vector<BLAKE3Pipeline::Hash> seed_compositions;
        bool is_question;           // Detected question intent
        bool is_creative;           // Detected creative/generative intent
    };

    struct Orientation {
        std::vector<SubProblem> sub_problems;
        std::vector<KnowledgeGap> gaps;
        bool solvable;
        std::vector<std::string> known_facts;
    };

    // Phase implementations
    Observation observe(const std::string& prompt, const ReasoningConfig& config);
    Orientation orient(const Observation& obs);
    std::vector<Intention> decide(const Observation& obs, const Orientation& ort);
    std::vector<Hypothesis> act(const std::vector<Intention>& intentions,
                                const Observation& obs,
                                const ReasoningConfig& config);
    Hypothesis reflect(std::vector<Hypothesis>& hypotheses,
                       const Observation& obs,
                       const ReasoningConfig& config);

    // Response assembly
    std::string assemble_response(const Hypothesis& best,
                                  const Observation& obs,
                                  const ReasoningConfig& config);

    // Walk-based passage generation for gaps between A* paths
    std::string generate_passage(const BLAKE3Pipeline::Hash& seed,
                                 size_t max_words,
                                 const WalkParameters& params);

    // Intent detection
    bool detect_question(const std::string& prompt) const;
    bool detect_creative(const std::string& prompt) const;

    // Quality scoring for reflexion
    double score_hypothesis(const Hypothesis& h) const;

    PostgresConnection& db_;
    WalkEngine walk_;
    AStarSearch astar_;
    GodelEngine godel_;
    SemanticQuery query_;
};

} // namespace Hartonomous
