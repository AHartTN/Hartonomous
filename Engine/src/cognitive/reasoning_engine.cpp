/**
 * @file reasoning_engine.cpp
 * @brief Full cognitive pipeline: OODA + BDI + Tree of Thought + Reflexion
 *
 * This orchestrates walk_engine, astar_search, godel_engine, and semantic_query
 * into a coherent reasoning loop that transforms prompts into responses.
 */

#include <cognitive/reasoning_engine.hpp>
#include <algorithm>
#include <numeric>
#include <sstream>
#include <cctype>
#include <iostream>
#include <cmath>

namespace Hartonomous {

ReasoningEngine::ReasoningEngine(PostgresConnection& db)
    : db_(db), walk_(db), astar_(db), godel_(db), query_(db) {}

// =============================================================================
// OBSERVE: Parse prompt → extract seeds
// =============================================================================

ReasoningEngine::Observation ReasoningEngine::observe(
    const std::string& prompt, const ReasoningConfig& config)
{
    Observation obs;
    obs.prompt = prompt;
    obs.system_context = config.system_prompt;
    obs.is_question = detect_question(prompt);
    obs.is_creative = detect_creative(prompt);

    // Incorporate conversation history into context
    if (!config.history.empty()) {
        // Use the last few turns for context
        size_t start = config.history.size() > 3 ? config.history.size() - 3 : 0;
        for (size_t i = start; i < config.history.size(); ++i) {
            auto hist_keywords = query_.extract_keywords(config.history[i].second);
            for (const auto& kw : hist_keywords) {
                obs.keywords.push_back(kw);
            }
        }
    }

    // Extract keywords from prompt
    auto prompt_keywords = query_.extract_keywords(prompt);
    obs.keywords.insert(obs.keywords.end(), prompt_keywords.begin(), prompt_keywords.end());

    // Deduplicate keywords
    std::sort(obs.keywords.begin(), obs.keywords.end());
    obs.keywords.erase(std::unique(obs.keywords.begin(), obs.keywords.end()), obs.keywords.end());

    // Resolve keywords → composition IDs
    for (const auto& kw : obs.keywords) {
        auto comp = query_.find_composition(kw);
        if (comp) {
            obs.seed_compositions.push_back(BLAKE3Pipeline::from_hex(*comp));
        } else {
            // Try lowercase
            std::string lower = kw;
            std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);
            auto lower_comp = query_.find_composition(lower);
            if (lower_comp) {
                obs.seed_compositions.push_back(BLAKE3Pipeline::from_hex(*lower_comp));
            }
        }
    }

    return obs;
}

// =============================================================================
// ORIENT: Gödel decomposition → sub-goals + knowledge gaps
// =============================================================================

ReasoningEngine::Orientation ReasoningEngine::orient(const Observation& obs) {
    Orientation ort;

    // Check solvability
    ort.solvable = godel_.is_solvable(obs.prompt);

    // Decompose the problem into sub-problems
    ort.sub_problems = godel_.decompose_problem(obs.prompt);

    // Identify knowledge gaps
    BLAKE3Pipeline::Hash problem_hash = BLAKE3Pipeline::hash(obs.prompt);
    std::string problem_uuid = BLAKE3Pipeline::to_hex(problem_hash);
    ort.gaps = godel_.identify_knowledge_gaps(problem_uuid);

    // Query known facts for each keyword (up to first 5)
    for (size_t i = 0; i < std::min(obs.keywords.size(), size_t(5)); ++i) {
        auto facts = godel_.query_known_facts(obs.keywords[i]);
        ort.known_facts.insert(ort.known_facts.end(), facts.begin(), facts.end());
    }

    // Deduplicate known facts
    std::sort(ort.known_facts.begin(), ort.known_facts.end());
    ort.known_facts.erase(
        std::unique(ort.known_facts.begin(), ort.known_facts.end()),
        ort.known_facts.end()
    );

    return ort;
}

// =============================================================================
// DECIDE: BDI intention selection + prioritization
// =============================================================================

std::vector<Intention> ReasoningEngine::decide(
    const Observation& obs, const Orientation& ort)
{
    std::vector<Intention> intentions;

    // Strategy 1: If we have sub-problems from Gödel, create intentions for solvable ones
    for (const auto& sub : ort.sub_problems) {
        if (sub.is_solvable) {
            Intention intent;
            intent.description = sub.description;
            intent.target_id = sub.node_id;
            // Higher priority for easier sub-problems (solve what we can first)
            intent.priority = 1.0 - (sub.difficulty / 10.0);
            intentions.push_back(intent);
        }
    }

    // Strategy 2: If we have seed compositions, create cross-seed intentions
    // "Connect concept A to concept B" — finding semantic bridges
    if (obs.seed_compositions.size() >= 2) {
        for (size_t i = 0; i < obs.seed_compositions.size() && i < 4; ++i) {
            for (size_t j = i + 1; j < obs.seed_compositions.size() && j < 4; ++j) {
                Intention intent;
                intent.description = "Bridge: " +
                    (i < obs.keywords.size() ? obs.keywords[i] : "?") + " → " +
                    (j < obs.keywords.size() ? obs.keywords[j] : "?");
                intent.target_id = obs.seed_compositions[j];
                intent.priority = 0.8; // High priority — direct prompt relevance
                intentions.push_back(intent);
            }
        }
    }

    // Strategy 3: Known facts as lightweight intentions
    for (const auto& fact : ort.known_facts) {
        auto comp = query_.find_composition(fact);
        if (comp) {
            Intention intent;
            intent.description = "Known: " + fact;
            intent.target_id = BLAKE3Pipeline::from_hex(*comp);
            intent.priority = 0.5; // Lower priority — background knowledge
            intentions.push_back(intent);
        }
        if (intentions.size() >= 12) break; // Cap total intentions
    }

    // Sort by priority (highest first)
    std::sort(intentions.begin(), intentions.end(),
        [](const Intention& a, const Intention& b) { return a.priority > b.priority; });

    // Keep top-N (reasonable limit for Tree of Thought)
    if (intentions.size() > 8) intentions.resize(8);

    return intentions;
}

// =============================================================================
// ACT: Tree of Thought — parallel A* searches
// =============================================================================

std::vector<Hypothesis> ReasoningEngine::act(
    const std::vector<Intention>& intentions,
    const Observation& obs,
    const ReasoningConfig& config)
{
    std::vector<Hypothesis> hypotheses;

    if (intentions.empty() || obs.seed_compositions.empty()) {
        // Fallback: walk-only hypothesis
        Hypothesis h;
        h.assembled_text = generate_passage(
            obs.seed_compositions.empty()
                ? BLAKE3Pipeline::hash(obs.prompt)
                : obs.seed_compositions[0],
            config.max_response_words,
            config.walk
        );
        h.quality_score = 0.2; // Low confidence for pure walk
        hypotheses.push_back(h);
        return hypotheses;
    }

    // Create beam_width hypotheses, each trying different intention orderings
    for (size_t beam = 0; beam < config.beam_width && beam < intentions.size(); ++beam) {
        Hypothesis h;

        // Each beam starts from a different seed
        BLAKE3Pipeline::Hash start = obs.seed_compositions[beam % obs.seed_compositions.size()];

        // Try to resolve intentions via A*
        for (size_t i = 0; i < intentions.size(); ++i) {
            // Rotate intention order for each beam
            size_t idx = (i + beam) % intentions.size();
            Intention intent = intentions[idx];

            auto path = astar_.search(start, intent.target_id, config.astar);

            if (path.found) {
                intent.resolved = true;
                intent.path = path;
                h.paths.push_back(path);

                // Chain: next search starts from where this one ended
                if (!path.nodes.empty()) {
                    start = path.nodes.back();
                }
            }

            h.intentions.push_back(intent);
        }

        h.quality_score = score_hypothesis(h);
        hypotheses.push_back(h);
    }

    return hypotheses;
}

// =============================================================================
// REFLECT: Evaluate quality, re-search if needed
// =============================================================================

Hypothesis ReasoningEngine::reflect(
    std::vector<Hypothesis>& hypotheses,
    const Observation& obs,
    const ReasoningConfig& config)
{
    // Sort hypotheses by quality
    std::sort(hypotheses.begin(), hypotheses.end(),
        [](const Hypothesis& a, const Hypothesis& b) {
            return a.quality_score > b.quality_score;
        });

    Hypothesis& best = hypotheses[0];

    // Reflexion loop: if quality is too low, try to improve
    int round = 0;
    while (best.quality_score < config.min_path_quality && round < config.max_reflexion_rounds) {
        round++;

        // Identify unresolved intentions
        std::vector<BLAKE3Pipeline::Hash> unresolved_targets;
        for (const auto& intent : best.intentions) {
            if (!intent.resolved) {
                unresolved_targets.push_back(intent.target_id);
            }
        }

        if (unresolved_targets.empty()) break;

        // Try multi-goal search from each seed
        for (const auto& seed : obs.seed_compositions) {
            AStarConfig relaxed = config.astar;
            relaxed.min_elo = std::max(600.0, relaxed.min_elo - 200.0 * round);
            relaxed.max_expansions = config.astar.max_expansions * 2;

            auto path = astar_.search_multi_goal(seed, unresolved_targets, relaxed);

            if (path.found) {
                best.paths.push_back(path);
                // Mark the reached goal as resolved
                if (!path.nodes.empty()) {
                    auto goal_id = path.nodes.back();
                    for (auto& intent : best.intentions) {
                        if (intent.target_id == goal_id) {
                            intent.resolved = true;
                            intent.path = path;
                            break;
                        }
                    }
                }
            }
        }

        best.quality_score = score_hypothesis(best);
    }

    return best;
}

// =============================================================================
// Score a hypothesis for reflexion evaluation
// =============================================================================

double ReasoningEngine::score_hypothesis(const Hypothesis& h) const {
    if (h.intentions.empty()) return 0.0;

    // Factor 1: Resolution rate (what fraction of intentions were resolved?)
    int resolved = 0;
    for (const auto& i : h.intentions) {
        if (i.resolved) resolved++;
    }
    double resolution_rate = static_cast<double>(resolved) / h.intentions.size();

    // Factor 2: Average path quality (ELO)
    double avg_elo = 0.0;
    if (!h.paths.empty()) {
        double elo_sum = 0.0;
        for (const auto& p : h.paths) {
            elo_sum += p.avg_elo;
        }
        avg_elo = (elo_sum / h.paths.size()) / 2000.0; // Normalize to [0,1]
    }

    // Factor 3: Path coverage (do paths cover multiple intentions?)
    double coverage = h.paths.empty() ? 0.0 :
        std::min(1.0, static_cast<double>(h.paths.size()) / h.intentions.size());

    // Weighted combination
    return 0.5 * resolution_rate + 0.3 * avg_elo + 0.2 * coverage;
}

// =============================================================================
// Response Assembly
// =============================================================================

std::string ReasoningEngine::assemble_response(
    const Hypothesis& best,
    const Observation& obs,
    const ReasoningConfig& config)
{
    std::vector<std::string> segments;

    // Collect unique words from all paths (preserving order of first occurrence)
    std::vector<std::string> path_words;
    std::unordered_set<std::string> seen_words;

    for (const auto& path : best.paths) {
        for (const auto& text : path.texts) {
            if (text.empty()) continue;
            std::string lower = text;
            std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);
            if (seen_words.insert(lower).second) {
                path_words.push_back(text);
            }
        }
    }

    // If we have resolved paths, build response from path words
    if (!path_words.empty()) {
        std::string passage;
        for (size_t i = 0; i < path_words.size() && i < config.max_response_words; ++i) {
            const auto& w = path_words[i];
            if (i == 0) {
                std::string cap = w;
                if (!cap.empty()) cap[0] = std::toupper(cap[0]);
                passage += cap;
            } else if (w.size() == 1 && std::ispunct(static_cast<unsigned char>(w[0]))) {
                passage += w;
            } else {
                passage += " " + w;
            }
        }

        segments.push_back(passage);
    }

    // Fill remaining capacity with walk-generated text from unresolved seeds
    if (segments.empty() || path_words.size() < config.max_response_words / 2) {
        for (const auto& seed : obs.seed_compositions) {
            size_t remaining = config.max_response_words -
                (path_words.empty() ? 0 : path_words.size());
            if (remaining < 10) break;

            std::string walk_text = generate_passage(seed, remaining, config.walk);
            if (!walk_text.empty()) {
                segments.push_back(walk_text);
                break; // One walk passage is enough
            }
        }
    }

    // Join segments
    std::string response;
    for (size_t i = 0; i < segments.size(); ++i) {
        if (i > 0) response += " ";
        response += segments[i];
    }

    // Ensure ends with punctuation
    if (!response.empty()) {
        char last = response.back();
        if (last != '.' && last != '!' && last != '?') {
            response += ".";
        }
    }

    return response;
}

// =============================================================================
// Walk-based passage generation
// =============================================================================

std::string ReasoningEngine::generate_passage(
    const BLAKE3Pipeline::Hash& seed,
    size_t max_words,
    const WalkParameters& params)
{
    return walk_.generate(walk_.lookup_text(seed), params,
                          std::min(max_words, size_t(100)));
}

// =============================================================================
// Intent detection
// =============================================================================

bool ReasoningEngine::detect_question(const std::string& prompt) const {
    if (prompt.empty()) return false;
    if (prompt.back() == '?') return true;

    std::string lower = prompt;
    std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);

    static const std::vector<std::string> q_prefixes = {
        "what ", "who ", "where ", "when ", "why ", "how ",
        "is ", "are ", "was ", "were ", "do ", "does ", "did ",
        "can ", "could ", "would ", "should ", "will ",
        "tell me", "explain", "describe"
    };

    for (const auto& prefix : q_prefixes) {
        if (lower.compare(0, prefix.size(), prefix) == 0) return true;
    }

    return false;
}

bool ReasoningEngine::detect_creative(const std::string& prompt) const {
    std::string lower = prompt;
    std::transform(lower.begin(), lower.end(), lower.begin(), ::tolower);

    static const std::vector<std::string> creative_markers = {
        "write ", "compose ", "create ", "generate ", "imagine ",
        "story ", "poem ", "essay ", "tell me a ", "once upon"
    };

    for (const auto& marker : creative_markers) {
        if (lower.find(marker) != std::string::npos) return true;
    }

    return false;
}

// =============================================================================
// Main Entry Points
// =============================================================================

ReasoningResult ReasoningEngine::reason(const std::string& prompt,
                                        const ReasoningConfig& config)
{
    ReasoningResult result;
    result.reflexion_rounds = 0;
    result.nodes_expanded = 0;

    // 1. OBSERVE
    auto obs = observe(prompt, config);

    if (obs.seed_compositions.empty()) {
        // Cannot even find prompt concepts in substrate
        result.response = walk_.generate(prompt, config.walk, config.walk_max_steps);
        result.confidence = 0.1;
        result.intentions_resolved = 0;
        result.intentions_total = 0;
        return result;
    }

    // 2. ORIENT
    auto ort = orient(obs);

    // 3. DECIDE
    auto intentions = decide(obs, ort);
    result.intentions_total = intentions.size();

    // 4. ACT (Tree of Thought)
    auto hypotheses = act(intentions, obs, config);

    // 5. REFLECT
    auto best = reflect(hypotheses, obs, config);

    // Count results
    result.intentions_resolved = 0;
    for (const auto& i : best.intentions) {
        if (i.resolved) result.intentions_resolved++;
    }

    for (const auto& p : best.paths) {
        result.nodes_expanded += p.nodes_expanded;
    }

    // Reasoning trace
    if (config.include_reasoning_trace) {
        for (const auto& path : best.paths) {
            if (path.found) {
                std::string trace = "[";
                for (size_t i = 0; i < path.texts.size(); ++i) {
                    if (i > 0) trace += " → ";
                    trace += path.texts[i];
                }
                trace += "]";
                result.reasoning_trace.push_back(trace);
            }
        }
    }

    // 6. ASSEMBLE
    result.response = assemble_response(best, obs, config);
    result.confidence = best.quality_score;

    return result;
}

ReasoningResult ReasoningEngine::reason_stream(
    const std::string& prompt,
    ReasoningStreamCallback callback,
    const ReasoningConfig& config)
{
    // For streaming, we run the full pipeline then emit tokens
    auto result = reason(prompt, config);

    // Tokenize response and stream
    std::istringstream iss(result.response);
    std::string token;
    size_t step = 0;

    while (iss >> token) {
        std::string out = (step == 0) ? token : (" " + token);
        if (!callback(out, step)) break;
        step++;
    }

    return result;
}

ReasoningResult ReasoningEngine::quick_answer(const std::string& prompt,
                                               const ReasoningConfig& config)
{
    ReasoningResult result;
    result.reflexion_rounds = 0;
    result.nodes_expanded = 0;
    result.intentions_total = 0;
    result.intentions_resolved = 0;

    // Try semantic query first (fast path)
    auto answer = query_.answer_question(prompt);
    if (answer && answer->confidence > 5.0) {
        result.response = answer->text;
        result.confidence = std::min(1.0, answer->confidence / 100.0);
        return result;
    }

    // Try gravitational truth
    auto keywords = query_.extract_keywords(prompt);
    for (const auto& kw : keywords) {
        auto truths = query_.find_gravitational_truth(kw, 1400.0, 5);
        if (!truths.empty()) {
            std::string response;
            for (size_t i = 0; i < truths.size() && i < 5; ++i) {
                if (i > 0) response += ", ";
                response += truths[i].text;
            }
            result.response = response;
            result.confidence = truths[0].confidence;
            return result;
        }
    }

    // Fall back to full reasoning
    return reason(prompt, config);
}

} // namespace Hartonomous
