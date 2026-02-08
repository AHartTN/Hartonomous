/**
 * @file astar_search.hpp
 * @brief A* search over the relation graph with S³ geodesic heuristic
 *
 * Goal-directed pathfinding through the substrate. The heuristic is the
 * geodesic distance on S³ between the current composition's physicality
 * centroid and the goal's centroid. This is admissible (geodesic = shortest
 * possible path on the sphere) and consistent (triangle inequality holds
 * on S³), guaranteeing optimal paths.
 *
 * Cost function: inverse ELO × inverse log(observations). We prefer
 * traversing high-confidence, well-evidenced relations.
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <export.hpp>
#include <Eigen/Dense>
#include <vector>
#include <string>
#include <unordered_map>
#include <optional>

namespace Hartonomous {

struct AStarNode {
    BLAKE3Pipeline::Hash composition_id;
    std::string text;
    Eigen::Vector4d position;       // S³ centroid
    double g_cost;                  // Accumulated path cost
    double f_cost;                  // g + h (total estimated)
    BLAKE3Pipeline::Hash parent_id; // For path reconstruction
};

struct AStarPath {
    std::vector<BLAKE3Pipeline::Hash> nodes;  // Composition IDs from start to goal
    std::vector<std::string> texts;           // Readable text for each node
    double total_cost;                        // Accumulated traversal cost
    double avg_elo;                           // Average ELO along path
    double avg_observations;                  // Average observations along path
    bool found;                               // Whether goal was reached
    size_t nodes_expanded;                    // For diagnostics
};

struct AStarConfig {
    size_t max_expansions = 10000;   // Safety limit
    double heuristic_weight = 1.0;   // w=1 is standard A*; w>1 is weighted A* (faster, suboptimal)
    double min_elo = 800.0;          // Skip relations below this ELO
    double min_observations = 1.0;   // Skip relations with fewer observations
    size_t beam_width = 0;           // 0 = full A*; >0 = beam search variant
};

class HARTONOMOUS_API AStarSearch {
public:
    explicit AStarSearch(PostgresConnection& db);

    /**
     * @brief Find optimal path from start composition to goal composition
     *
     * Uses S³ geodesic as admissible heuristic. Cost = 1 / (elo_norm * log(obs+1)).
     * Guaranteed optimal when heuristic_weight = 1.0.
     */
    AStarPath search(const BLAKE3Pipeline::Hash& start,
                     const BLAKE3Pipeline::Hash& goal,
                     const AStarConfig& config = {});

    /**
     * @brief Find path between two text terms
     *
     * Convenience wrapper: resolves text → composition IDs, then calls search().
     */
    AStarPath search_text(const std::string& start_text,
                          const std::string& goal_text,
                          const AStarConfig& config = {});

    /**
     * @brief Multi-goal search: find paths to ANY of the goal compositions
     *
     * Useful for BDI: "reach any sub-goal". Returns the first found path.
     * Heuristic uses minimum geodesic distance to any goal.
     */
    AStarPath search_multi_goal(const BLAKE3Pipeline::Hash& start,
                                const std::vector<BLAKE3Pipeline::Hash>& goals,
                                const AStarConfig& config = {});

    // Utilities
    std::string lookup_text(const BLAKE3Pipeline::Hash& id) const;
    BLAKE3Pipeline::Hash find_composition(const std::string& text);

private:
    struct Neighbor {
        BLAKE3Pipeline::Hash id;
        double elo;
        double observations;
    };

    // Load S³ position for a composition
    std::optional<Eigen::Vector4d> load_position(const BLAKE3Pipeline::Hash& id);

    // Get all neighbors of a composition with ELO/observation data
    std::vector<Neighbor> get_neighbors(const BLAKE3Pipeline::Hash& id,
                                        double min_elo, double min_obs);

    // S³ geodesic heuristic: arccos(clamp(dot(a,b), -1, 1))
    double heuristic(const Eigen::Vector4d& current, const Eigen::Vector4d& goal) const;

    // Edge cost: lower ELO and fewer observations = higher cost
    double edge_cost(double elo, double observations) const;

    // Pre-cache composition text and positions
    void preload_cache();

    PostgresConnection& db_;
    std::unordered_map<BLAKE3Pipeline::Hash, std::string, HashHasher> text_cache_;
    std::unordered_map<BLAKE3Pipeline::Hash, Eigen::Vector4d, HashHasher> position_cache_;
    bool cache_loaded_ = false;
};

} // namespace Hartonomous
