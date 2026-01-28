/**
 * @file ooda_loop.cpp
 * @brief OODA Loop implementation
 */

#include <cognitive/ooda_loop.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <chrono>
#include <thread>

namespace Hartonomous {

OODALoop::OODALoop(PostgresConnection& db) : db_(db) {}

void OODALoop::observe(const std::string& query, const std::string& result, int rating) {
    auto query_hash = BLAKE3Pipeline::to_hex(BLAKE3Pipeline::hash(query));
    auto result_hash = BLAKE3Pipeline::to_hex(BLAKE3Pipeline::hash(result));

    db_.execute(
        "INSERT INTO hartonomous.metadata (hash, entity_type, key, value) "
        "VALUES ($1, 'feedback', 'query_result', jsonb_build_object('query', $2, 'result', $3, 'rating', $4, 'timestamp', NOW()))",
        {query_hash, query, result, std::to_string(rating)}
    );
}

std::vector<EdgeUpdate> OODALoop::orient() {
    std::vector<EdgeUpdate> updates;
    auto stats = analyze_feedback();

    for (const auto& stat : stats) {
        EdgeUpdate update;
        update.source_hash = stat.source_hash;
        update.target_hash = stat.target_hash;
        update.elo_delta = calculate_elo_delta(stat.avg_rating, stat.feedback_count);
        update.reason = "Feedback avg=" + std::to_string(stat.avg_rating) +
                       ", count=" + std::to_string(stat.feedback_count);
        updates.push_back(update);
    }

    return updates;
}

std::vector<EdgeUpdate> OODALoop::decide(const std::vector<EdgeUpdate>& candidates) {
    std::vector<EdgeUpdate> decisions;

    for (const auto& candidate : candidates) {
        // Only act on significant changes
        if (std::abs(candidate.elo_delta) >= 25) {
            decisions.push_back(candidate);
        }
    }

    return decisions;
}

void OODALoop::act(const std::vector<EdgeUpdate>& updates) {
    for (const auto& update : updates) {
        // Update ELO in metadata (semantic_edges stored in metadata for now)
        db_.execute(
            "UPDATE hartonomous.metadata SET value = jsonb_set(value, '{elo}', to_jsonb(COALESCE((value->>'elo')::int, 1500) + $1)) "
            "WHERE hash = $2",
            {std::to_string(update.elo_delta), update.source_hash}
        );
    }
}

OODAMetrics OODALoop::run_cycle() {
    OODAMetrics metrics = {};

    // OBSERVE (done via observe() calls)

    // ORIENT
    auto candidates = orient();
    metrics.observations_processed = candidates.size();

    // DECIDE
    auto updates = decide(candidates);

    // ACT
    act(updates);

    // Count results
    for (const auto& update : updates) {
        if (update.elo_delta > 0) metrics.edges_strengthened++;
        else if (update.elo_delta < 0) metrics.edges_weakened++;
    }

    return metrics;
}

void OODALoop::run_continuous(int interval_seconds) {
    while (true) {
        run_cycle();
        std::this_thread::sleep_for(std::chrono::seconds(interval_seconds));
    }
}

std::vector<OODALoop::EdgeStats> OODALoop::analyze_feedback() {
    std::vector<EdgeStats> stats;

    std::string sql = R"(
        SELECT
            hash,
            value->>'result' as target,
            AVG((value->>'rating')::int) as avg_rating,
            COUNT(*) as feedback_count
        FROM hartonomous.metadata
        WHERE entity_type = 'feedback'
        GROUP BY hash, value->>'result'
        HAVING COUNT(*) > 5
    )";

    db_.query(sql, [&](const std::vector<std::string>& row) {
        EdgeStats stat;
        stat.source_hash = row[0];
        stat.target_hash = row[1];
        stat.avg_rating = std::stod(row[2]);
        stat.feedback_count = std::stoi(row[3]);
        stats.push_back(stat);
    });

    return stats;
}

int OODALoop::calculate_elo_delta(double avg_rating, int feedback_count) {
    // Higher feedback count = more confident update
    // avg_rating: 1-5 stars
    // ELO delta: -100 to +100

    double normalized_rating = (avg_rating - 3.0) / 2.0;  // Map to [-1, 1]
    double confidence_factor = std::min(1.0, feedback_count / 100.0);

    return static_cast<int>(normalized_rating * 100 * confidence_factor);
}

bool OODALoop::should_prune(const EdgeStats& stats) {
    return stats.avg_rating < 2.0 && stats.feedback_count > 20;
}

} // namespace Hartonomous
