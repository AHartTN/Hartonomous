/**
 * @file ooda_loop.hpp
 * @brief OODA Loop: Observe-Orient-Decide-Act continuous learning
 *
 * Implements Boyd's OODA loop for continuous adaptation:
 * - Observe: Collect feedback and results
 * - Orient: Analyze patterns, update beliefs
 * - Decide: Choose actions (ELO updates, pruning)
 * - Act: Execute updates to database
 */

#pragma once

#include <database/postgres_connection.hpp>
#include <vector>
#include <string>
#include <cstdint>

namespace Hartonomous {

struct Observation {
    std::string query_hash;
    std::string result_hash;
    int user_rating;  // 1-5 stars
    std::string timestamp;
};

struct EdgeUpdate {
    std::string source_hash;
    std::string target_hash;
    int elo_delta;  // Change in ELO rating
    std::string reason;
};

struct OODAMetrics {
    int observations_processed;
    int edges_strengthened;
    int edges_weakened;
    int edges_pruned;
    double avg_user_satisfaction;
};

/**
 * @brief OODA Loop for continuous learning and adaptation
 *
 * Continuously improves the semantic graph based on user feedback
 * and query results.
 */
class OODALoop {
public:
    explicit OODALoop(PostgresConnection& db);

    /**
     * @brief OBSERVE: Record user feedback
     */
    void observe(const std::string& query, const std::string& result, int rating);

    /**
     * @brief ORIENT: Analyze feedback patterns
     */
    std::vector<EdgeUpdate> orient();

    /**
     * @brief DECIDE: Choose which edges to update
     */
    std::vector<EdgeUpdate> decide(const std::vector<EdgeUpdate>& candidates);

    /**
     * @brief ACT: Execute ELO updates and pruning
     */
    void act(const std::vector<EdgeUpdate>& updates);

    /**
     * @brief Run one complete OODA cycle
     */
    OODAMetrics run_cycle();

    /**
     * @brief Run continuous OODA loop (background task)
     */
    void run_continuous(int interval_seconds = 60);

private:
    PostgresConnection& db_;

    // Feedback analysis
    struct EdgeStats {
        std::string source_hash;
        std::string target_hash;
        double avg_rating;
        int feedback_count;
    };

    std::vector<EdgeStats> analyze_feedback();
    int calculate_elo_delta(double avg_rating, int feedback_count);
    bool should_prune(const EdgeStats& stats);
};

} // namespace Hartonomous
