/**
 * @file walk_engine.cpp
 * @brief Implementation of canonical generative walking logic
 */

#include <cognitive/walk_engine.hpp>
#include <random>
#include <cmath>
#include <iostream>
#include <algorithm>
#include <iomanip>

namespace Hartonomous {

WalkEngine::WalkEngine(PostgresConnection& db) : db_(db) {}

WalkState WalkEngine::init_walk(const BLAKE3Pipeline::Hash& start_id, double initial_energy) {
    WalkState state;
    state.current_composition = start_id;
    state.current_energy = initial_energy;
    state.trajectory.push_back(start_id);
    state.visit_counts[start_id] = 1;
    state.recent.push_back(start_id);

    std::string hex_id = BLAKE3Pipeline::to_hex(start_id);
    db_.query("SELECT ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid) "
              "FROM hartonomous.physicality p "
              "JOIN hartonomous.composition c ON c.physicalityid = p.id "
              "WHERE c.id = $1", {hex_id},
              [&](const std::vector<std::string>& row) {
                  state.current_position = Eigen::Vector4d(
                      std::stod(row[0]), std::stod(row[1]), std::stod(row[2]), std::stod(row[3])
                  );
                  state.previous_position = state.current_position;
              });
    
    return state;
}

void WalkEngine::set_goal(WalkState& state, const BLAKE3Pipeline::Hash& goal_id) {
    state.goal_composition = goal_id;
    std::string hex_id = BLAKE3Pipeline::to_hex(goal_id);
    db_.query("SELECT ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid) "
              "FROM hartonomous.physicality p "
              "JOIN hartonomous.composition c ON c.physicalityid = p.id "
              "WHERE c.id = $1", {hex_id},
              [&](const std::vector<std::string>& row) {
                  state.goal_position = Eigen::Vector4d(
                      std::stod(row[0]), std::stod(row[1]), std::stod(row[2]), std::stod(row[3])
                  );
              });
}

Eigen::Vector4d WalkEngine::get_position(const BLAKE3Pipeline::Hash& comp_id) {
    auto it = position_cache_.find(comp_id);
    if (it != position_cache_.end()) return it->second;

    Eigen::Vector4d pos(0, 0, 0, 1);
    std::string hex_id = BLAKE3Pipeline::to_hex(comp_id);
    db_.query("SELECT ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid) "
              "FROM hartonomous.physicality p "
              "JOIN hartonomous.composition c ON c.physicalityid = p.id "
              "WHERE c.id = $1", {hex_id},
              [&](const std::vector<std::string>& row) {
                  pos = Eigen::Vector4d(std::stod(row[0]), std::stod(row[1]), std::stod(row[2]), std::stod(row[3]));
              });
    position_cache_[comp_id] = pos;
    return pos;
}

std::vector<WalkEngine::Candidate> WalkEngine::get_candidates(const WalkState& state) {
    std::vector<Candidate> candidates;
    if (state.trajectory.empty()) return candidates;

    std::string current_hex = BLAKE3Pipeline::to_hex(state.current_composition);

    // Fast indexed query using existing RelationSequence + RelationRating
    // idx_RelationSequence_CompositionId makes this fast
    std::string sql = R"(
        SELECT
            rs2.compositionid,
            rr.observations,
            rr.ratingvalue,
            ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid)
        FROM hartonomous.relationsequence rs1
        JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid AND rs2.compositionid != rs1.compositionid
        JOIN hartonomous.relationrating rr ON rr.relationid = rs1.relationid
        JOIN hartonomous.composition c ON c.id = rs2.compositionid
        JOIN hartonomous.physicality p ON p.id = c.physicalityid
        WHERE rs1.compositionid = $1
        ORDER BY rr.ratingvalue DESC
        LIMIT 500
    )";

    db_.query(sql, {current_hex}, [&](const std::vector<std::string>& row) {
        Candidate c;
        c.id = BLAKE3Pipeline::from_hex(row[0]);
        double obs = std::stod(row[1]);
        double rating = std::stod(row[2]);
        c.position = Eigen::Vector4d(std::stod(row[3]), std::stod(row[4]), std::stod(row[5]), std::stod(row[6]));

        c.model_sim = rating / 2000.0;
        c.text_sim = std::log1p(obs) / 10.0;
        c.rel_strength = obs;
        double dot = state.current_position.dot(c.position);
        c.geo_sim = (dot + 1.0) / 2.0;
        c.hilbert_sim = 0.5;

        candidates.push_back(c);
    });

    // 2. Spatial Candidates (KNN in 4D for Semantic Drift)
    // This enables "creative" leaps between concepts that are close in meaning (embedding space)
    // but not explicitly linked in the training data.
    std::string spatial_sql = R"(
        SELECT
            c.id,
            ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid)
        FROM hartonomous.physicality p
        JOIN hartonomous.composition c ON c.physicalityid = p.id
        WHERE p.id != (SELECT physicalityid FROM hartonomous.composition WHERE id = $1)
        ORDER BY p.centroid <-> (
            SELECT centroid FROM hartonomous.physicality 
            WHERE id = (SELECT physicalityid FROM hartonomous.composition WHERE id = $1)
        )
        LIMIT 20
    )";

    db_.query(spatial_sql, {current_hex}, [&](const std::vector<std::string>& row) {
        BLAKE3Pipeline::Hash id = BLAKE3Pipeline::from_hex(row[0]);
        
        // Dedup: Skip if already found via graph
        for (const auto& existing : candidates) {
            if (existing.id == id) return;
        }

        Candidate c;
        c.id = id;
        c.position = Eigen::Vector4d(std::stod(row[1]), std::stod(row[2]), std::stod(row[3]), std::stod(row[4]));
        
        // Base scores for purely spatial neighbors
        c.model_sim = 0.5; // Neutral
        c.text_sim = 0.0;
        c.rel_strength = 0.0;
        
        double dot = state.current_position.dot(c.position);
        c.geo_sim = (dot + 1.0) / 2.0;
        c.hilbert_sim = 0.5; // TODO: Calculate actual hilbert distance if needed

        candidates.push_back(c);
    });

    if (candidates.empty()) {
        std::cerr << "WARNING: No neighbors for " << current_hex << std::endl;
    }

    return candidates;
}

double WalkEngine::score_candidate(const WalkState& state, const Candidate& c, const WalkParameters& params) {
    double score = 0.0;

    // 1. Adjacency signals
    score += params.w_model   * c.model_sim;
    score += params.w_text    * c.text_sim;
    score += params.w_rel     * (1.0 / (1.0 + std::exp(-c.rel_strength / 100.0)));
    score += params.w_geo     * c.geo_sim;
    score += params.w_hilbert * c.hilbert_sim;

    // 2. Goal Attraction
    if (state.goal_position.has_value()) {
        double g_sim = state.goal_position->dot(c.position);
        score += params.goal_attraction * ((g_sim + 1.0) / 2.0);
    }

    // 3. Repetition penalty
    auto it = state.visit_counts.find(c.id);
    if (it != state.visit_counts.end()) {
        score -= params.w_repeat * static_cast<double>(it->second);
    }

    // 4. Novelty penalty (recent window)
    if (std::find(state.recent.begin(), state.recent.end(), c.id) != state.recent.end()) {
        score -= params.w_novelty;
    }

    // 5. Energy-based exploration bonus
    score += params.w_energy * state.current_energy;

    return score;
}

size_t WalkEngine::select_index(const std::vector<double>& probs) {
    static std::random_device rd;
    static std::mt19937 gen(rd());
    std::discrete_distribution<> d(probs.begin(), probs.end());
    return d(gen);
}

WalkStepResult WalkEngine::step(WalkState& state, const WalkParameters& params) {
    WalkStepResult result;
    result.terminated = false;

    if (state.current_energy <= 0) {
        result.terminated = true;
        result.reason = "Out of energy";
        return result;
    }

    auto candidates = get_candidates(state);
    if (candidates.empty()) {
        result.terminated = true;
        result.reason = "Trapped in manifold (no neighbors)";
        return result;
    }

    // Compute scores
    std::vector<double> scores;
    scores.reserve(candidates.size());
    for (const auto& c : candidates) {
        scores.push_back(score_candidate(state, c, params));
    }

    // Energy-modulated temperature
    double temperature = params.base_temp + (params.energy_alpha * state.current_energy);

    // Softmax sampling
    double max_s = *std::max_element(scores.begin(), scores.end());
    double sum = 0.0;
    std::vector<double> probs(scores.size());
    for (size_t i = 0; i < scores.size(); ++i) {
        probs[i] = std::exp((scores[i] - max_s) / std::max(0.01, temperature));
        sum += probs[i];
    }
    for (auto& p : probs) p /= sum;

    size_t chosen = select_index(probs);
    auto& selected = candidates[chosen];

    // Update State
    state.previous_position = state.current_position;
    state.current_composition = selected.id;
    state.current_position = selected.position;
    state.current_energy -= params.energy_decay;
    state.trajectory.push_back(selected.id);
    state.visit_counts[selected.id]++;
    
    state.recent.push_back(selected.id);
    if (state.recent.size() > params.recent_window) {
        state.recent.pop_front();
    }

    result.next_composition = selected.id;
    result.probability = probs[chosen];
    result.energy_remaining = state.current_energy;

    if (state.goal_composition.has_value() && state.current_composition == *state.goal_composition) {
        result.terminated = true;
        result.reason = "Goal reached";
    }

    return result;
}

} // namespace Hartonomous
