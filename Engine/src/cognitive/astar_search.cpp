/**
 * @file astar_search.cpp
 * @brief A* search over the relation graph with S³ geodesic heuristic
 *
 * Finds optimal semantic paths between compositions. The relation graph is
 * treated as a weighted graph where edge costs are derived from ELO ratings
 * and observation counts. The S³ geodesic provides an admissible, consistent
 * heuristic for spatial pruning.
 */

#include <cognitive/astar_search.hpp>
#include <cmath>
#include <queue>
#include <algorithm>
#include <unordered_set>
#include <iostream>

namespace Hartonomous {

AStarSearch::AStarSearch(PostgresConnection& db) : db_(db) {}

void AStarSearch::preload_cache() {
    if (cache_loaded_) return;

    // Preload composition text
    db_.query(
        "SELECT v.composition_id, v.reconstructed_text "
        "FROM hartonomous.v_composition_text v",
        {},
        [&](const std::vector<std::string>& row) {
            auto hash = BLAKE3Pipeline::from_hex(row[0]);
            text_cache_[hash] = row[1];
        }
    );

    // Preload S³ positions
    db_.query(
        "SELECT c.id, ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid) "
        "FROM hartonomous.composition c "
        "JOIN hartonomous.physicality p ON p.id = c.physicalityid",
        {},
        [&](const std::vector<std::string>& row) {
            auto hash = BLAKE3Pipeline::from_hex(row[0]);
            position_cache_[hash] = Eigen::Vector4d(
                std::stod(row[1]), std::stod(row[2]),
                std::stod(row[3]), std::stod(row[4])
            );
        }
    );

    cache_loaded_ = true;
}

std::string AStarSearch::lookup_text(const BLAKE3Pipeline::Hash& id) const {
    auto it = text_cache_.find(id);
    return (it != text_cache_.end()) ? it->second : "";
}

BLAKE3Pipeline::Hash AStarSearch::find_composition(const std::string& text) {
    BLAKE3Pipeline::Hash result = {};
    db_.query(
        "SELECT v.composition_id FROM hartonomous.v_composition_text v "
        "WHERE LOWER(v.reconstructed_text) = LOWER($1) LIMIT 1",
        {text},
        [&](const std::vector<std::string>& row) {
            result = BLAKE3Pipeline::from_hex(row[0]);
        }
    );
    return result;
}

std::optional<Eigen::Vector4d> AStarSearch::load_position(const BLAKE3Pipeline::Hash& id) {
    auto it = position_cache_.find(id);
    if (it != position_cache_.end()) return it->second;
    return std::nullopt;
}

std::vector<AStarSearch::Neighbor> AStarSearch::get_neighbors(
    const BLAKE3Pipeline::Hash& id, double min_elo, double min_obs)
{
    std::vector<Neighbor> neighbors;
    std::string hex = BLAKE3Pipeline::to_hex(id);

    // Aggregate: same composition may appear via multiple relations
    // Take max ELO, sum observations (same as walk engine)
    struct Agg { double max_elo = 0; double total_obs = 0; };
    std::unordered_map<BLAKE3Pipeline::Hash, Agg, HashHasher> agg;

    db_.query(
        "SELECT rs2.compositionid, rr.ratingvalue, uint64_to_double(rr.observations) "
        "FROM hartonomous.relationsequence rs1 "
        "JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid "
        "  AND rs2.compositionid != rs1.compositionid "
        "JOIN hartonomous.relationrating rr ON rr.relationid = rs1.relationid "
        "WHERE rs1.compositionid = $1",
        {hex},
        [&](const std::vector<std::string>& row) {
            auto nid = BLAKE3Pipeline::from_hex(row[0]);
            double elo = std::stod(row[1]);
            double obs = std::stod(row[2]);
            auto& a = agg[nid];
            a.max_elo = std::max(a.max_elo, elo);
            a.total_obs += obs;
        }
    );

    for (const auto& [nid, a] : agg) {
        if (a.max_elo >= min_elo && a.total_obs >= min_obs) {
            neighbors.push_back({nid, a.max_elo, a.total_obs});
        }
    }

    return neighbors;
}

double AStarSearch::heuristic(const Eigen::Vector4d& current, const Eigen::Vector4d& goal) const {
    double d = current.dot(goal);
    d = std::clamp(d, -1.0, 1.0);
    return std::acos(d);  // Geodesic distance on S³, range [0, π]
}

double AStarSearch::edge_cost(double elo, double observations) const {
    // High ELO + high observations = low cost (strong, well-evidenced relation)
    // Normalize ELO to [0,1] range: 800-2000 → 0-1
    double elo_norm = std::clamp((elo - 800.0) / 1200.0, 0.01, 1.0);
    double obs_norm = std::log(observations + 1.0) / std::log(1000.0); // log scale, saturates ~1000
    obs_norm = std::clamp(obs_norm, 0.01, 1.0);

    // Cost is inverse quality: poor relations are expensive to traverse
    return 1.0 / (elo_norm * obs_norm);
}

AStarPath AStarSearch::search(const BLAKE3Pipeline::Hash& start,
                              const BLAKE3Pipeline::Hash& goal,
                              const AStarConfig& config)
{
    preload_cache();

    AStarPath result;
    result.found = false;
    result.total_cost = 0;
    result.avg_elo = 0;
    result.avg_observations = 0;
    result.nodes_expanded = 0;

    auto start_pos = load_position(start);
    auto goal_pos = load_position(goal);
    if (!start_pos || !goal_pos) return result;

    // Priority queue: (f_cost, composition_id)
    using PQEntry = std::pair<double, BLAKE3Pipeline::Hash>;
    auto cmp = [](const PQEntry& a, const PQEntry& b) { return a.first > b.first; };
    std::priority_queue<PQEntry, std::vector<PQEntry>, decltype(cmp)> open(cmp);

    // g_cost map and parent map
    std::unordered_map<BLAKE3Pipeline::Hash, double, HashHasher> g_costs;
    std::unordered_map<BLAKE3Pipeline::Hash, BLAKE3Pipeline::Hash, HashHasher> parents;
    std::unordered_map<BLAKE3Pipeline::Hash, double, HashHasher> edge_elos;      // For path stats
    std::unordered_map<BLAKE3Pipeline::Hash, double, HashHasher> edge_obs;

    g_costs[start] = 0.0;
    double h = config.heuristic_weight * heuristic(*start_pos, *goal_pos);
    open.push({h, start});

    while (!open.empty() && result.nodes_expanded < config.max_expansions) {
        auto [f, current] = open.top();
        open.pop();

        // Skip if we already found a better path to this node
        auto g_it = g_costs.find(current);
        if (g_it != g_costs.end() && f > g_it->second + config.heuristic_weight * M_PI + 0.001) {
            continue; // Stale entry
        }

        // Goal reached
        if (current == goal) {
            result.found = true;
            result.total_cost = g_costs[goal];

            // Reconstruct path
            BLAKE3Pipeline::Hash node = goal;
            while (node != start) {
                result.nodes.push_back(node);
                result.texts.push_back(lookup_text(node));
                auto pit = parents.find(node);
                if (pit == parents.end()) break;
                node = pit->second;
            }
            result.nodes.push_back(start);
            result.texts.push_back(lookup_text(start));
            std::reverse(result.nodes.begin(), result.nodes.end());
            std::reverse(result.texts.begin(), result.texts.end());

            // Compute path statistics
            double elo_sum = 0, obs_sum = 0;
            size_t edge_count = 0;
            for (const auto& nid : result.nodes) {
                auto eit = edge_elos.find(nid);
                if (eit != edge_elos.end()) {
                    elo_sum += eit->second;
                    obs_sum += edge_obs[nid];
                    edge_count++;
                }
            }
            if (edge_count > 0) {
                result.avg_elo = elo_sum / edge_count;
                result.avg_observations = obs_sum / edge_count;
            }

            return result;
        }

        result.nodes_expanded++;

        auto neighbors = get_neighbors(current, config.min_elo, config.min_observations);
        double current_g = g_costs[current];

        for (const auto& [nid, elo, obs] : neighbors) {
            double tentative_g = current_g + edge_cost(elo, obs);

            auto existing = g_costs.find(nid);
            if (existing != g_costs.end() && tentative_g >= existing->second) {
                continue; // Not a better path
            }

            g_costs[nid] = tentative_g;
            parents[nid] = current;
            edge_elos[nid] = elo;
            edge_obs[nid] = obs;

            auto npos = load_position(nid);
            double h_val = npos
                ? config.heuristic_weight * heuristic(*npos, *goal_pos)
                : config.heuristic_weight * M_PI; // Worst case if no position

            double f_val = tentative_g + h_val;

            // Beam search variant: only expand if within beam width
            if (config.beam_width > 0) {
                // For beam search, we rely on the PQ naturally prioritizing
                // the best nodes. The expansion limit acts as the beam constraint.
            }

            open.push({f_val, nid});
        }
    }

    return result; // Not found within expansion limit
}

AStarPath AStarSearch::search_text(const std::string& start_text,
                                   const std::string& goal_text,
                                   const AStarConfig& config)
{
    preload_cache();

    auto start = find_composition(start_text);
    auto goal = find_composition(goal_text);

    if (start == BLAKE3Pipeline::Hash{} || goal == BLAKE3Pipeline::Hash{}) {
        AStarPath empty;
        empty.found = false;
        return empty;
    }

    return search(start, goal, config);
}

AStarPath AStarSearch::search_multi_goal(const BLAKE3Pipeline::Hash& start,
                                         const std::vector<BLAKE3Pipeline::Hash>& goals,
                                         const AStarConfig& config)
{
    preload_cache();

    AStarPath result;
    result.found = false;
    result.nodes_expanded = 0;

    if (goals.empty()) return result;

    // Preload goal positions
    std::vector<std::pair<BLAKE3Pipeline::Hash, Eigen::Vector4d>> goal_positions;
    for (const auto& g : goals) {
        auto pos = load_position(g);
        if (pos) goal_positions.push_back({g, *pos});
    }
    if (goal_positions.empty()) return result;

    auto start_pos = load_position(start);
    if (!start_pos) return result;

    // Multi-goal heuristic: minimum geodesic to ANY goal
    auto multi_heuristic = [&](const Eigen::Vector4d& pos) -> double {
        double min_h = M_PI;
        for (const auto& [gid, gpos] : goal_positions) {
            min_h = std::min(min_h, heuristic(pos, gpos));
        }
        return min_h;
    };

    // Goal set for O(1) membership check
    std::unordered_set<BLAKE3Pipeline::Hash, HashHasher> goal_set(goals.begin(), goals.end());

    using PQEntry = std::pair<double, BLAKE3Pipeline::Hash>;
    auto cmp = [](const PQEntry& a, const PQEntry& b) { return a.first > b.first; };
    std::priority_queue<PQEntry, std::vector<PQEntry>, decltype(cmp)> open(cmp);

    std::unordered_map<BLAKE3Pipeline::Hash, double, HashHasher> g_costs;
    std::unordered_map<BLAKE3Pipeline::Hash, BLAKE3Pipeline::Hash, HashHasher> parents;
    std::unordered_map<BLAKE3Pipeline::Hash, double, HashHasher> edge_elos;
    std::unordered_map<BLAKE3Pipeline::Hash, double, HashHasher> edge_obs;

    g_costs[start] = 0.0;
    open.push({config.heuristic_weight * multi_heuristic(*start_pos), start});

    while (!open.empty() && result.nodes_expanded < config.max_expansions) {
        auto [f, current] = open.top();
        open.pop();

        auto g_it = g_costs.find(current);
        if (g_it != g_costs.end() && f > g_it->second + config.heuristic_weight * M_PI + 0.001) {
            continue;
        }

        // Any goal reached?
        if (goal_set.count(current)) {
            result.found = true;
            result.total_cost = g_costs[current];

            BLAKE3Pipeline::Hash node = current;
            while (node != start) {
                result.nodes.push_back(node);
                result.texts.push_back(lookup_text(node));
                auto pit = parents.find(node);
                if (pit == parents.end()) break;
                node = pit->second;
            }
            result.nodes.push_back(start);
            result.texts.push_back(lookup_text(start));
            std::reverse(result.nodes.begin(), result.nodes.end());
            std::reverse(result.texts.begin(), result.texts.end());

            double elo_sum = 0, obs_sum = 0;
            size_t edge_count = 0;
            for (const auto& nid : result.nodes) {
                auto eit = edge_elos.find(nid);
                if (eit != edge_elos.end()) {
                    elo_sum += eit->second;
                    obs_sum += edge_obs[nid];
                    edge_count++;
                }
            }
            if (edge_count > 0) {
                result.avg_elo = elo_sum / edge_count;
                result.avg_observations = obs_sum / edge_count;
            }
            return result;
        }

        result.nodes_expanded++;

        auto neighbors = get_neighbors(current, config.min_elo, config.min_observations);
        double current_g = g_costs[current];

        for (const auto& [nid, elo, obs] : neighbors) {
            double tentative_g = current_g + edge_cost(elo, obs);

            auto existing = g_costs.find(nid);
            if (existing != g_costs.end() && tentative_g >= existing->second) continue;

            g_costs[nid] = tentative_g;
            parents[nid] = current;
            edge_elos[nid] = elo;
            edge_obs[nid] = obs;

            auto npos = load_position(nid);
            double h_val = npos
                ? config.heuristic_weight * multi_heuristic(*npos)
                : config.heuristic_weight * M_PI;

            open.push({tentative_g + h_val, nid});
        }
    }

    return result;
}

} // namespace Hartonomous
