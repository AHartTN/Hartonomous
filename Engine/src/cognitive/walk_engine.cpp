/**
 * @file walk_engine.cpp
 * @brief Generative walking = forward pass through ELO-weighted relation graph
 *
 * The walk IS inference. Relations are weights. ELO is activation strength.
 * Each step selects the next composition by scoring relation-graph neighbors
 * with observation-weighted ELO, filtering noise, and beam-searching for coherence.
 */

#include <cognitive/walk_engine.hpp>
#include <random>
#include <cmath>
#include <iostream>
#include <algorithm>
#include <iomanip>
#include <sstream>
#include <numeric>

namespace Hartonomous {

// Tokens that are model artifacts, not semantic content
static bool is_model_artifact(const std::string& text) {
    if (text.empty()) return true;
    if (text.size() >= 8 && text.substr(0, 7) == "[unused") return true;
    if (text == "[PAD]" || text == "[CLS]" || text == "[SEP]" || text == "[MASK]") return true;
    if (text == "[UNK]") return true;
    if (text.size() >= 2 && text[0] == '#' && text[1] == '#') return true; // wordpiece subword
    if (text.size() >= 2 && text[0] == '#' && !std::isalpha(text[1])) return true; // #17, #», etc.
    return false;
}

// Function words — carry grammatical structure but low semantic content
// Used for scoring deprioritization, NOT filtering from output
static bool is_function_word(const std::string& text) {
    if (text.empty()) return true;
    // Punctuation is always structural
    if (text.size() == 1 && !std::isalnum(static_cast<unsigned char>(text[0]))) return true;
    static const std::unordered_set<std::string> funcs = {
        "the", "a", "an", "of", "in", "to", "is", "and", "or", "but", "not",
        "for", "on", "with", "at", "by", "from", "as", "that", "this", "it",
        "was", "were", "be", "been", "being", "have", "has", "had", "do", "does",
        "did", "will", "would", "could", "should", "may", "might", "shall", "can",
        "are", "am", "its", "his", "her", "he", "she", "they", "them", "their",
        "we", "our", "you", "your", "my", "me", "him", "us", "who", "whom",
        "which", "what", "where", "when", "how", "why", "if", "so", "no",
        "than", "then", "there", "here", "these", "those", "itself", "himself",
        "herself", "themselves", "myself", "yourself"
    };
    std::string lower = text;
    std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);
    return funcs.count(lower) > 0;
}

WalkEngine::WalkEngine(PostgresConnection& db) : db_(db) {
    // Pre-cache composition text for fast lookup during walks
    preload_composition_text();
}

void WalkEngine::preload_composition_text() {
    db_.query(
        "SELECT v.composition_id, v.reconstructed_text "
        "FROM hartonomous.v_composition_text v",
        {},
        [&](const std::vector<std::string>& row) {
            auto hash = BLAKE3Pipeline::from_hex(row[0]);
            comp_text_cache_[hash] = row[1];
        }
    );
}

std::string WalkEngine::lookup_text(const BLAKE3Pipeline::Hash& id) const {
    auto it = comp_text_cache_.find(id);
    if (it != comp_text_cache_.end()) return it->second;
    return "";
}

BLAKE3Pipeline::Hash WalkEngine::find_composition(const std::string& text) {
    BLAKE3Pipeline::Hash result = {};
    db_.query(
        "SELECT v.composition_id FROM hartonomous.v_composition_text v "
        "WHERE v.reconstructed_text = $1 LIMIT 1",
        {text},
        [&](const std::vector<std::string>& row) {
            result = BLAKE3Pipeline::from_hex(row[0]);
        }
    );
    return result;
}

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

WalkState WalkEngine::init_walk_from_prompt(const std::string& prompt, double initial_energy) {
    // Extract content words from prompt
    std::istringstream iss(prompt);
    std::string word;
    std::vector<BLAKE3Pipeline::Hash> seeds;

    while (iss >> word) {
        // Strip punctuation
        word.erase(std::remove_if(word.begin(), word.end(), ::ispunct), word.end());
        if (word.empty() || is_function_word(word)) continue;

        // Try exact match, then lowercase
        auto id = find_composition(word);
        if (id == BLAKE3Pipeline::Hash{}) {
            std::string lower = word;
            std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);
            id = find_composition(lower);
        }
        if (id != BLAKE3Pipeline::Hash{}) {
            seeds.push_back(id);
        }
    }

    if (seeds.empty()) {
        // Fallback: use highest-rated composition
        BLAKE3Pipeline::Hash fallback = {};
        db_.query(
            "SELECT rs.compositionid FROM hartonomous.relationsequence rs "
            "JOIN hartonomous.relationrating rr ON rs.relationid = rr.relationid "
            "ORDER BY rr.ratingvalue DESC LIMIT 1", {},
            [&](const std::vector<std::string>& row) {
                fallback = BLAKE3Pipeline::from_hex(row[0]);
            }
        );
        return init_walk(fallback, initial_energy);
    }

    // Multi-seed: Find the seed with the most relations to other seeds
    // This is the "center of the prompt" — the composition most connected to the query
    BLAKE3Pipeline::Hash best_seed = seeds[0];
    if (seeds.size() > 1) {
        size_t best_connections = 0;
        for (const auto& seed : seeds) {
            size_t connections = 0;
            std::string hex = BLAKE3Pipeline::to_hex(seed);
            for (const auto& other : seeds) {
                if (other == seed) continue;
                std::string other_hex = BLAKE3Pipeline::to_hex(other);
                auto result = db_.query_single(
                    "SELECT 1 FROM hartonomous.relationsequence rs1 "
                    "JOIN hartonomous.relationsequence rs2 ON rs2.relationid = rs1.relationid "
                    "WHERE rs1.compositionid = $1 AND rs2.compositionid = $2 LIMIT 1",
                    {hex, other_hex}
                );
                if (result.has_value()) connections++;
            }
            if (connections > best_connections) {
                best_connections = connections;
                best_seed = seed;
            }
        }
        // Store all seeds as context — boost candidates related to ANY seed
        context_seeds_ = seeds;
    }

    return init_walk(best_seed, initial_energy);
}

void WalkEngine::set_goal(WalkState& state, const BLAKE3Pipeline::Hash& goal_id) {
    state.goal_composition = goal_id;
}

std::vector<WalkEngine::Candidate> WalkEngine::get_candidates(const WalkState& state) {
    std::vector<Candidate> candidates;
    if (state.trajectory.empty()) return candidates;

    std::string current_hex = BLAKE3Pipeline::to_hex(state.current_composition);

    // Query ALL relations for this composition — we aggregate duplicates in C++
    std::string sql = R"(
        SELECT
            rs2.compositionid,
            uint64_to_double(rr.observations),
            rr.ratingvalue
        FROM hartonomous.relationsequence rs1
        JOIN hartonomous.relationsequence rs2 
            ON rs2.relationid = rs1.relationid 
            AND rs2.compositionid != rs1.compositionid
        JOIN hartonomous.relationrating rr 
            ON rr.relationid = rs1.relationid
        WHERE rs1.compositionid = $1
    )";

    // Aggregate: same composition may appear via multiple relations
    // Merge them: sum observations, max ELO
    struct AggCandidate {
        double total_obs = 0.0;
        double max_rating = 0.0;
        int relation_count = 0;
    };
    std::unordered_map<BLAKE3Pipeline::Hash, AggCandidate, HashHasher> agg;

    db_.query(sql, {current_hex}, [&](const std::vector<std::string>& row) {
        auto id = BLAKE3Pipeline::from_hex(row[0]);
        double obs = std::stod(row[1]);
        double rating = std::stod(row[2]);
        auto& ac = agg[id];
        ac.total_obs += obs;
        ac.max_rating = std::max(ac.max_rating, rating);
        ac.relation_count++;
    });

    // Find max observations for normalization (across aggregated candidates)
    double max_obs = 1.0;
    double max_elo = 0.0, min_elo = 1e9;
    for (const auto& [id, ac] : agg) {
        if (ac.total_obs > max_obs) max_obs = ac.total_obs;
        if (ac.max_rating > max_elo) max_elo = ac.max_rating;
        if (ac.max_rating < min_elo) min_elo = ac.max_rating;
    }
    double elo_range = std::max(1.0, max_elo - min_elo);

    for (const auto& [id, ac] : agg) {
        std::string text = lookup_text(id);

        // Filter model artifacts
        if (is_model_artifact(text)) continue;

        // Require minimum observations — single-obs model edges are noise
        // Lowered to 1.0 for testing/sparse graphs
        if (ac.total_obs < 1.0) continue;

        Candidate c;
        c.id = id;
        c.text = text;

        // ELO normalized against THIS candidate set (local, not hardcoded)
        c.elo_score = (ac.max_rating - min_elo) / elo_range;

        // Observation ratio: how observed is this vs the most-observed neighbor?
        c.obs_score = ac.total_obs / max_obs;

        // Raw for sigmoid gating
        c.rel_strength = ac.total_obs;

        // Stop word flag
        c.is_stop_word = is_function_word(text);

        candidates.push_back(c);
    }

    if (candidates.empty()) {
        std::cerr << "WARNING: No viable candidates for " << current_hex << std::endl;
    }

    return candidates;
}

double WalkEngine::score_candidate(const WalkState& state, const Candidate& c, const WalkParameters& params) {
    double score = 0.0;

    // Core signals from relation graph
    score += params.w_model * c.elo_score;
    score += params.w_text  * c.obs_score;
    score += params.w_rel   * (1.0 / (1.0 + std::exp(-c.rel_strength / 50.0)));

    // Stop word: hard cap — guarantees they never enter top-K
    if (c.is_stop_word) {
        score = std::min(score, 0.02);
    } else {
        score += 0.05; // Small consistent push for content words
    }

    // Context seed bonus — if this candidate relates to a prompt keyword, boost it
    if (!context_seeds_.empty()) {
        for (const auto& seed : context_seeds_) {
            if (c.id == seed) {
                score += 0.3;
                break;
            }
        }
    }

    // Repetition penalty
    auto it = state.visit_counts.find(c.id);
    if (it != state.visit_counts.end()) {
        score -= params.w_repeat * static_cast<double>(it->second);
    }

    // Novelty penalty (recent window)
    if (std::find(state.recent.begin(), state.recent.end(), c.id) != state.recent.end()) {
        score -= params.w_novelty;
    }

    // Energy-based exploration bonus
    score += params.w_energy * state.current_energy;

    // Pre-softmax sharpening: widen gaps between content words and noise
    score = std::pow(std::max(0.0, score), 0.75);

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

    // Score all candidates
    for (auto& c : candidates) {
        c.score = score_candidate(state, c, params);
    }

    // Top-K filtering: keep only the best candidates to sharpen the distribution
    constexpr size_t TOP_K = 32;
    if (candidates.size() > TOP_K) {
        std::partial_sort(candidates.begin(), candidates.begin() + TOP_K, candidates.end(),
            [](const Candidate& a, const Candidate& b) { return a.score > b.score; });
        candidates.resize(TOP_K);
    }

    // Energy-modulated temperature: high energy = exploratory, low energy = greedy
    double temperature = params.base_temp - params.energy_alpha * state.current_energy;
    temperature = std::clamp(temperature, params.min_temp, params.base_temp);

    // Softmax sampling over top-K
    std::vector<double> scores;
    scores.reserve(candidates.size());
    for (const auto& c : candidates) {
        scores.push_back(c.score);
    }

    double max_s = *std::max_element(scores.begin(), scores.end());
    double sum = 0.0;
    std::vector<double> probs(scores.size());
    for (size_t i = 0; i < scores.size(); ++i) {
        double logit = (scores[i] - max_s) / temperature;
        probs[i] = std::exp(logit);
        sum += probs[i];
    }
    for (auto& p : probs) p /= sum;

    size_t chosen = select_index(probs);
    auto& selected = candidates[chosen];

    // Update State
    state.current_composition = selected.id;
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

std::string WalkEngine::generate(const std::string& prompt, const WalkParameters& params, size_t max_steps) {
    auto state = init_walk_from_prompt(prompt, 1.0);
    
    std::string seed_text = lookup_text(state.current_composition);
    std::vector<std::string> words;
    if (!seed_text.empty()) {
        words.push_back(seed_text);
    }

    for (size_t i = 0; i < max_steps; ++i) {
        auto result = step(state, params);
        if (result.terminated) break;

        std::string text = lookup_text(result.next_composition);
        if (text.empty()) continue;

        // Avoid consecutive duplicates
        if (!words.empty() && words.back() == text) continue;

        words.push_back(text);
    }

    // Assemble into readable text
    std::string output;
    for (size_t i = 0; i < words.size(); ++i) {
        const auto& w = words[i];
        if (i == 0) {
            std::string cap = w;
            if (!cap.empty()) cap[0] = std::toupper(cap[0]);
            output += cap;
        } else if (w.size() == 1 && std::ispunct(static_cast<unsigned char>(w[0]))) {
            output += w; // No space before punctuation
        } else {
            output += " " + w;
        }
    }
    if (!output.empty() && output.back() != '.' && output.back() != '!' && output.back() != '?') {
        output += ".";
    }

    return output;
}

} // namespace Hartonomous
