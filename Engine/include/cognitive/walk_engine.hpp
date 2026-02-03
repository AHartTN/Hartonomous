/**
 * @file walk_engine.hpp
 * @brief Core engine for generative walks over the semantic-geometric substrate
 */

#pragma once

#include <hashing/blake3_pipeline.hpp>
#include <database/postgres_connection.hpp>
#include <ingestion/ngram_extractor.hpp>
#include <export.hpp>
#include <Eigen/Dense>
#include <vector>
#include <optional>
#include <memory>
#include <unordered_map>
#include <unordered_set>
#include <deque>

namespace Hartonomous {

struct WalkParameters {
    // Adjacency Weights
    double w_model = 0.35;               // From AI model KNN
    double w_text = 0.40;                // From text co-occurrence
    double w_rel = 0.15;                 // From relation ratings
    double w_geo = 0.05;                 // S3 proximity
    double w_hilbert = 0.05;             // Hilbert locality

    // Penalties
    double w_repeat = 0.25;              // Per-visit penalty
    double w_novelty = 0.15;             // Recent visit penalty

    // Goals
    double goal_attraction = 2.0;        // Pull towards goal

    // Energy / Exploration
    double w_energy = 0.10;              // Energy bonus
    double base_temp = 0.4;              // Minimum temperature
    double energy_alpha = 0.6;           // Energy-to-temp multiplier
    double energy_decay = 0.05;          // Energy lost per step
    
    size_t recent_window = 16;           // For novelty loop detection
};

struct WalkState {
    BLAKE3Pipeline::Hash current_composition;
    Eigen::Vector4d current_position;    // S3 centroid
    Eigen::Vector4d previous_position;   // For momentum
    double current_energy;
    
    std::vector<BLAKE3Pipeline::Hash> trajectory;
    std::unordered_map<BLAKE3Pipeline::Hash, int, HashHasher> visit_counts;
    std::deque<BLAKE3Pipeline::Hash> recent; // Fixed-size window
    
    std::optional<BLAKE3Pipeline::Hash> goal_composition;
    std::optional<Eigen::Vector4d> goal_position;
};

struct WalkStepResult {
    BLAKE3Pipeline::Hash next_composition;
    double probability;
    double energy_remaining;
    bool terminated;
    std::string reason;
};

class HARTONOMOUS_API WalkEngine {
public:
    explicit WalkEngine(PostgresConnection& db);

    WalkState init_walk(const BLAKE3Pipeline::Hash& start_id, double initial_energy = 1.0);
    WalkStepResult step(WalkState& state, const WalkParameters& params);
    void set_goal(WalkState& state, const BLAKE3Pipeline::Hash& goal_id);

private:
    struct Candidate {
        BLAKE3Pipeline::Hash id;
        Eigen::Vector4d position;
        
        // Adjacency signals
        double model_sim = 0.0;
        double text_sim = 0.0;
        double rel_strength = 0.0;
        double geo_sim = 0.0;
        double hilbert_sim = 0.0;
        
        double score = 0.0;
    };

    std::vector<Candidate> get_candidates(const WalkState& state);
    double score_candidate(const WalkState& state, const Candidate& c, const WalkParameters& params);
    size_t select_index(const std::vector<double>& probs);
    Eigen::Vector4d get_position(const BLAKE3Pipeline::Hash& comp_id);

    PostgresConnection& db_;

    // In-memory position cache for microsecond lookups
    std::unordered_map<BLAKE3Pipeline::Hash, Eigen::Vector4d, HashHasher> position_cache_;
};

} // namespace Hartonomous
