/**
 * @file walk_engine.hpp
 * @brief Core engine for generative walks = forward pass through relation graph
 *
 * The walk IS inference. Relations are weights. ELO is activation strength.
 * Geometry is for indexing/fuzzy search — semantics emerge from relation traversal.
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
#include <string>

namespace Hartonomous {

struct WalkParameters {
    // Relation graph weights (semantics emerge from relations, not proximity)
    double w_model = 0.35;               // ELO quality weight
    double w_text = 0.40;                // Observation frequency weight
    double w_rel = 0.15;                 // Sigmoid-gated relation strength
    double w_geo = 0.05;                 // Reserved (C ABI compat)
    double w_hilbert = 0.05;             // Reserved (C ABI compat)

    // Penalties
    double w_repeat = 0.25;              // Per-visit penalty
    double w_novelty = 0.15;             // Recent visit penalty

    // Goals
    double goal_attraction = 2.0;        // Pull towards goal

    // Energy / Exploration
    double w_energy = 0.10;              // Energy bonus
    double base_temp = 0.55;              // Maximum temperature (start of walk)
    double min_temp = 0.35;               // Minimum temperature (end of walk, greedy)
    double energy_alpha = 0.20;           // Energy-to-temp modulation (small effect)
    double energy_decay = 0.03;          // Energy lost per step (~33 steps at 1.0)
    
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
    WalkState init_walk_from_prompt(const std::string& prompt, double initial_energy = 1.0);
    WalkStepResult step(WalkState& state, const WalkParameters& params);
    void set_goal(WalkState& state, const BLAKE3Pipeline::Hash& goal_id);

    // High-level: prompt → coherent text response
    std::string generate(const std::string& prompt, const WalkParameters& params, size_t max_steps = 50);

    // Utilities
    std::string lookup_text(const BLAKE3Pipeline::Hash& id) const;
    BLAKE3Pipeline::Hash find_composition(const std::string& text);

private:
    struct Candidate {
        BLAKE3Pipeline::Hash id;
        std::string text;
        
        // Relation graph signals
        double elo_score = 0.0;      // Locally normalized ELO rating
        double obs_score = 0.0;      // Observation ratio (obs / max_obs)
        double rel_strength = 0.0;   // Raw observation count for sigmoid gating
        bool is_stop_word = false;
        
        double score = 0.0;
    };

    std::vector<Candidate> get_candidates(const WalkState& state);
    double score_candidate(const WalkState& state, const Candidate& c, const WalkParameters& params);
    size_t select_index(const std::vector<double>& probs);
    void preload_composition_text();

    PostgresConnection& db_;
    std::unordered_map<BLAKE3Pipeline::Hash, std::string, HashHasher> comp_text_cache_;
    std::vector<BLAKE3Pipeline::Hash> context_seeds_; // From multi-seed prompt init
};

} // namespace Hartonomous
