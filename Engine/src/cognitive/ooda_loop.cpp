/**
 * @file ooda_loop.cpp
 * @brief OODA Loop implementation - Aligning with RelationRating schema
 */

#include <cognitive/ooda_loop.hpp>
#include <hashing/blake3_pipeline.hpp>

namespace Hartonomous {

OODALoop::OODALoop(PostgresConnection& db) : db_(db) {}

void OODALoop::observe(const std::string& query, const std::string& result, int rating) {
    [[maybe_unused]] auto query_hash = BLAKE3Pipeline::hash(query);
    [[maybe_unused]] auto result_hash = BLAKE3Pipeline::hash(result);

    // TODO: Complete implementation - need to use query_hash, result_hash, and rating
    // Create a relation between query and result if one doesn't exist,
    // then add evidence to the RelationEvidence table.
    
    std::string rel_sql = R"(
        WITH new_rel AS (
            INSERT INTO hartonomous.relation (id, physicalityid)
            VALUES (gen_random_uuid(), '00000000-0000-0000-0000-000000000000') -- Placeholder phys
            ON CONFLICT DO NOTHING
            RETURNING id
        )
        INSERT INTO hartonomous.relationevidence (id, relationid, contentid, ispositive, strength, weight)
        SELECT gen_random_uuid(), id, $1, $2, $3, 1.0 FROM new_rel
    )";

    // Note: This needs actual ContentId from somewhere, or we store the observation itself.
    // For now, using rating parameter to indicate positive/negative feedback
    // TODO: Complete this implementation with proper hash and rating usage
    (void)rating; // Suppress unused parameter warning until implementation is complete
}

std::vector<EdgeUpdate> OODALoop::orient() {
    std::vector<EdgeUpdate> updates;

    // Analyze RelationEvidence to calculate ELO deltas
    std::string sql = R"(
        SELECT 
            relationid, 
            AVG(CASE WHEN ispositive THEN strength ELSE -strength END) as avg_strength,
            COUNT(*) as obs_count
        FROM hartonomous.relationevidence
        WHERE validatedat < NOW() - INTERVAL '1 hour'
        GROUP BY relationid
        HAVING COUNT(*) > 5
    )";

    db_.query(sql, [&](const std::vector<std::string>& row) {
        EdgeUpdate update;
        update.source_hash = row[0];
        update.elo_delta = calculate_elo_delta(std::stod(row[1]), std::stoi(row[2]));
        updates.push_back(update);
    });

    return updates;
}

void OODALoop::act(const std::vector<EdgeUpdate>& updates) {
    for (const auto& update : updates) {
        // Update the ACTUAL RelationRating table
        db_.execute(
            "UPDATE hartonomous.relationrating "
            "SET ratingvalue = ratingvalue + $1, observations = observations + 1, modifiedat = NOW() "
            "WHERE relationid = $2",
            {std::to_string(update.elo_delta), update.source_hash}
        );
    }
}

int OODALoop::calculate_elo_delta(double avg_strength, int feedback_count) {
    double confidence = std::min(1.0, feedback_count / 100.0);
    return static_cast<int>(avg_strength * 32.0 * confidence); // K=32 standard
}

} // namespace Hartonomous