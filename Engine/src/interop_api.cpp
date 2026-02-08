#include <interop_api.h>
#include <cognitive/godel_engine.hpp>
#include <cognitive/walk_engine.hpp>
#include <query/semantic_query.hpp>
#include <ingestion/universal_ingester.hpp>
#include <database/postgres_connection.hpp>
#include <hashing/blake3_pipeline.hpp>
#include <unicode/codepoint_projection.hpp>
#include <spatial/hilbert_curve_4d.hpp>
#include <geometry/s3_centroid.hpp>
#include <stdexcept>
#include <cstring>
#include <memory>
#include <endian.h>
#include <sstream>

// Thread-local error storage
thread_local std::string g_last_error;

const char* hartonomous_get_last_error() {
    return g_last_error.c_str();
}

const char* hartonomous_get_version() {
    return "0.1.0";
}

static void set_error(const std::exception& e) {
    g_last_error = e.what();
}

#define INTEROP_TRY_CATCH(code) \
    try { \
        code \
    } catch (const std::exception& e) { \
        set_error(e); \
        return false; \
    }

#define INTEROP_TRY_CATCH_PTR(code) \
    try { \
        code \
    } catch (const std::exception& e) { \
        set_error(e); \
        return nullptr; \
    }

// Helper for string duplication
char* strdup_safe(const std::string& str) {
#ifdef _WIN32
    return _strdup(str.c_str());
#else
    return strdup(str.c_str());
#endif
}

// =============================================================================
//  Database Connection
// =============================================================================

h_db_connection_t hartonomous_db_create(const char* connection_string) {
    INTEROP_TRY_CATCH_PTR({
        auto* db = new Hartonomous::PostgresConnection(connection_string);
        return static_cast<h_db_connection_t>(db);
    })
}

void hartonomous_db_destroy(h_db_connection_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::PostgresConnection*>(handle);
    }
}

bool hartonomous_db_is_connected(h_db_connection_t handle) {
    if (!handle) return false;
    auto* db = static_cast<Hartonomous::PostgresConnection*>(handle);
    return db->is_connected(); 
}

// =============================================================================
//  Core Primitives (Hashing & Projection)
// =============================================================================

void hartonomous_blake3_hash(const char* data, size_t len, uint8_t* out_16b) {
    auto hash = Hartonomous::BLAKE3Pipeline::hash(data, len);
    std::memcpy(out_16b, hash.data(), 16);
}

void hartonomous_blake3_hash_codepoint(uint32_t codepoint, uint8_t* out_16b) {
    auto hash = Hartonomous::BLAKE3Pipeline::hash_codepoint(static_cast<char32_t>(codepoint));
    std::memcpy(out_16b, hash.data(), 16);
}

bool hartonomous_codepoint_to_s3(uint32_t codepoint, double* out_4d) {
    INTEROP_TRY_CATCH({
        auto proj = hartonomous::unicode::CodepointProjection::project(static_cast<char32_t>(codepoint));
        std::memcpy(out_4d, proj.s3_position.data(), 4 * sizeof(double));
        return true;
    })
}

void hartonomous_s3_to_hilbert(const double* in_4d, uint32_t entity_type, uint64_t* out_hi, uint64_t* out_lo) {
    Eigen::Vector4d unit_coords;
    for (int i = 0; i < 4; ++i) {
        unit_coords[i] = (in_4d[i] + 1.0) / 2.0;
    }
    
    auto type = static_cast<hartonomous::spatial::HilbertCurve4D::EntityType>(entity_type);
    auto hilbert = hartonomous::spatial::HilbertCurve4D::encode(unit_coords, type);
    
    // Extract two uint64_t values from 16-byte array
    std::memcpy(out_hi, hilbert.data(), 8);
    std::memcpy(out_lo, hilbert.data() + 8, 8);
}

void hartonomous_s3_compute_centroid(const double* points_4d, size_t count, double* out_4d) {
    auto centroid = Hartonomous::Geometry::compute_s3_centroid(points_4d, count);
    std::memcpy(out_4d, centroid.data(), 4 * sizeof(double));
}

// =============================================================================
//  Ingestion Service
// =============================================================================

h_ingester_t hartonomous_ingester_create(h_db_connection_t db_handle) {
    INTEROP_TRY_CATCH_PTR({
        if (!db_handle) throw std::runtime_error("Invalid database handle");
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        auto* ingester = new Hartonomous::UniversalIngester(*db);
        return static_cast<h_ingester_t>(ingester);
    })
}

void hartonomous_ingester_destroy(h_ingester_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::UniversalIngester*>(handle);
    }
}

bool hartonomous_ingest_text(h_ingester_t handle, const char* text, HIngestionStats* out_stats) {
    INTEROP_TRY_CATCH({
        if (!handle || !text || !out_stats) throw std::runtime_error("Invalid parameters");
        auto* ingester = static_cast<Hartonomous::UniversalIngester*>(handle);
        auto stats = ingester->ingest_text(text);
        
        out_stats->atoms_total = stats.atoms_total;
        out_stats->atoms_new = stats.atoms_new;
        out_stats->compositions_total = stats.compositions_total;
        out_stats->compositions_new = stats.compositions_new;
        out_stats->relations_total = stats.relations_total;
        out_stats->relations_new = stats.relations_new;
        out_stats->evidence_count = stats.evidence_count;
        out_stats->original_bytes = stats.original_bytes;
        out_stats->stored_bytes = stats.stored_bytes;
        out_stats->compression_ratio = stats.compression_ratio;
        out_stats->ngrams_extracted = stats.ngrams_extracted;
        out_stats->ngrams_significant = stats.ngrams_significant;
        out_stats->cooccurrences_found = stats.cooccurrences_found;
        out_stats->cooccurrences_significant = stats.cooccurrences_significant;

        return true;
    })
}

bool hartonomous_ingest_file(h_ingester_t handle, const char* file_path, HIngestionStats* out_stats) {
    INTEROP_TRY_CATCH({
        if (!handle || !file_path || !out_stats) throw std::runtime_error("Invalid parameters");
        auto* ingester = static_cast<Hartonomous::UniversalIngester*>(handle);
        auto stats = ingester->ingest_path(file_path);

        out_stats->atoms_total = stats.atoms_total;
        out_stats->atoms_new = stats.atoms_new;
        out_stats->compositions_total = stats.compositions_total;
        out_stats->compositions_new = stats.compositions_new;
        out_stats->relations_total = stats.relations_total;
        out_stats->relations_new = stats.relations_new;
        out_stats->evidence_count = stats.evidence_count;
        out_stats->original_bytes = stats.original_bytes;
        out_stats->stored_bytes = stats.stored_bytes;
        out_stats->compression_ratio = stats.compression_ratio;
        out_stats->ngrams_extracted = stats.ngrams_extracted;
        out_stats->ngrams_significant = stats.ngrams_significant;
        out_stats->cooccurrences_found = stats.cooccurrences_found;
        out_stats->cooccurrences_significant = stats.cooccurrences_significant;

        return true;
    })
}
// =============================================================================
//  Walk Engine
// =============================================================================

h_walk_engine_t hartonomous_walk_create(h_db_connection_t db_handle) {
    try {
        if (!db_handle) throw std::runtime_error("Invalid database handle");
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        auto* engine = new Hartonomous::WalkEngine(*db);
        return static_cast<h_walk_engine_t>(engine);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_walk_destroy(h_walk_engine_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::WalkEngine*>(handle);
    }
}

bool hartonomous_walk_init(h_walk_engine_t handle, const uint8_t* start_id, double initial_energy, HWalkState* out_state) {
    try {
        if (!handle || !start_id || !out_state) return false;
        auto* engine = static_cast<Hartonomous::WalkEngine*>(handle);
        
        Hartonomous::BLAKE3Pipeline::Hash id;
        std::memcpy(id.data(), start_id, 16);
        
        auto state = engine->init_walk(id, initial_energy);
        
        std::memcpy(out_state->current_composition, state.current_composition.data(), 16);
        out_state->current_position[0] = state.current_position[0];
        out_state->current_position[1] = state.current_position[1];
        out_state->current_position[2] = state.current_position[2];
        out_state->current_position[3] = state.current_position[3];
        out_state->current_energy = state.current_energy;
        
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

bool hartonomous_walk_step(h_walk_engine_t handle, HWalkState* in_out_state, const HWalkParameters* params, HWalkStepResult* out_result) {
    try {
        if (!handle || !in_out_state || !params || !out_result) return false;
        auto* engine = static_cast<Hartonomous::WalkEngine*>(handle);
        
        // Reconstruct C++ state from C struct
        Hartonomous::WalkState state;
        std::memcpy(state.current_composition.data(), in_out_state->current_composition, 16);
        state.current_position = Eigen::Vector4d(
            in_out_state->current_position[0],
            in_out_state->current_position[1],
            in_out_state->current_position[2],
            in_out_state->current_position[3]
        );
        state.current_energy = in_out_state->current_energy;
        
        Hartonomous::WalkParameters p;
        p.w_model = params->w_model;
        p.w_text = params->w_text;
        p.w_rel = params->w_rel;
        p.w_geo = params->w_geo;
        p.w_hilbert = params->w_hilbert;
        p.w_repeat = params->w_repeat;
        p.w_novelty = params->w_novelty;
        p.goal_attraction = params->goal_attraction;
        p.w_energy = params->w_energy;
        p.base_temp = params->base_temp;
        p.energy_alpha = params->energy_alpha;
        p.energy_decay = params->energy_decay;
        p.recent_window = params->context_window;

        auto result = engine->step(state, p);
        
        // Output result
        std::memcpy(out_result->next_composition, result.next_composition.data(), 16);
        out_result->probability = result.probability;
        out_result->energy_remaining = result.energy_remaining;
        out_result->terminated = result.terminated;
        std::strncpy(out_result->reason, result.reason.c_str(), 255);
        out_result->reason[255] = '\0';
        
        // Update input state for next iteration
        std::memcpy(in_out_state->current_composition, state.current_composition.data(), 16);
        in_out_state->current_position[0] = state.current_position[0];
        in_out_state->current_position[1] = state.current_position[1];
        in_out_state->current_position[2] = state.current_position[2];
        in_out_state->current_position[3] = state.current_position[3];
        in_out_state->current_energy = state.current_energy;

        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

bool hartonomous_walk_set_goal(h_walk_engine_t handle, HWalkState* in_out_state, const uint8_t* goal_id) {
    try {
        if (!handle || !in_out_state || !goal_id) return false;
        auto* engine = static_cast<Hartonomous::WalkEngine*>(handle);
        
        // Reconstruct state
        Hartonomous::WalkState state;
        std::memcpy(state.current_composition.data(), in_out_state->current_composition, 16);
        
        Hartonomous::BLAKE3Pipeline::Hash gid;
        std::memcpy(gid.data(), goal_id, 16);
        
        engine->set_goal(state, gid);
        
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

// =============================================================================
//  Godel Engine
// =============================================================================

h_godel_t hartonomous_godel_create(h_db_connection_t db_handle) {
    try {
        if (!db_handle) throw std::runtime_error("Invalid database handle");
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        auto* godel = new Hartonomous::GodelEngine(*db);
        return static_cast<h_godel_t>(godel);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_godel_destroy(h_godel_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::GodelEngine*>(handle);
    }
}

bool hartonomous_godel_analyze(h_godel_t handle, const char* problem, HResearchPlan* out_plan) {
    try {
        if (!handle || !problem || !out_plan) return false;
        auto* godel = static_cast<Hartonomous::GodelEngine*>(handle);
        auto plan = godel->analyze_problem(problem);
        
        out_plan->original_problem = strdup_safe(plan.original_problem);
        out_plan->total_steps = plan.total_steps;
        out_plan->solvable_steps = plan.solvable_steps;
        
        // Convert sub-problems
        out_plan->sub_problems_count = plan.decomposition.size();
        if (out_plan->sub_problems_count > 0) {
            out_plan->sub_problems = new HSubProblem[out_plan->sub_problems_count];
            for (size_t i = 0; i < out_plan->sub_problems_count; ++i) {
                std::memcpy(out_plan->sub_problems[i].node_id, plan.decomposition[i].node_id.data(), 16);
                out_plan->sub_problems[i].description = strdup_safe(plan.decomposition[i].description);
                out_plan->sub_problems[i].difficulty = plan.decomposition[i].difficulty;
                out_plan->sub_problems[i].is_solvable = plan.decomposition[i].is_solvable;
            }
        } else {
            out_plan->sub_problems = nullptr;
        }

        // Convert knowledge gaps
        out_plan->knowledge_gaps_count = plan.knowledge_gaps.size();
        if (out_plan->knowledge_gaps_count > 0) {
            out_plan->knowledge_gaps = new HKnowledgeGap[out_plan->knowledge_gaps_count];
            for (size_t i = 0; i < out_plan->knowledge_gaps_count; ++i) {
                out_plan->knowledge_gaps[i].concept_name = strdup_safe(plan.knowledge_gaps[i].concept_name);
                out_plan->knowledge_gaps[i].references_count = plan.knowledge_gaps[i].references_count;
                out_plan->knowledge_gaps[i].confidence = plan.knowledge_gaps[i].confidence;
            }
        } else {
            out_plan->knowledge_gaps = nullptr;
        }

        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

void hartonomous_godel_free_plan(HResearchPlan* plan) {
    if (!plan) return;
    
    if (plan->original_problem) free(plan->original_problem);
    
    if (plan->sub_problems) {
        for (size_t i = 0; i < plan->sub_problems_count; ++i) {
            if (plan->sub_problems[i].description) free(plan->sub_problems[i].description);
        }
        delete[] plan->sub_problems;
    }
    
    if (plan->knowledge_gaps) {
        for (size_t i = 0; i < plan->knowledge_gaps_count; ++i) {
            if (plan->knowledge_gaps[i].concept_name) free(plan->knowledge_gaps[i].concept_name);
        }
        delete[] plan->knowledge_gaps;
    }
    
    plan->original_problem = nullptr;
    plan->sub_problems = nullptr;
    plan->knowledge_gaps = nullptr;
}

// =============================================================================
//  Composition Lookup
// =============================================================================

// Resolve a composition hash to its reconstructed text via v_composition_text
static std::string resolve_composition_text(Hartonomous::PostgresConnection& db,
                                            const Hartonomous::BLAKE3Pipeline::Hash& hash) {
    std::string hex_id = Hartonomous::BLAKE3Pipeline::to_hex(hash);
    std::string text;
    db.query(
        "SELECT reconstructed_text FROM hartonomous.v_composition_text WHERE composition_id = $1",
        {hex_id},
        [&](const std::vector<std::string>& row) { text = row[0]; }
    );
    return text;
}

char* hartonomous_composition_text(h_db_connection_t db_handle, const uint8_t* hash_16b) {
    try {
        if (!db_handle || !hash_16b) return nullptr;
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        Hartonomous::BLAKE3Pipeline::Hash hash;
        std::memcpy(hash.data(), hash_16b, 16);
        auto text = resolve_composition_text(*db, hash);
        return text.empty() ? nullptr : strdup_safe(text);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

bool hartonomous_composition_position(h_db_connection_t db_handle, const uint8_t* hash_16b, double* out_4d) {
    try {
        if (!db_handle || !hash_16b || !out_4d) return false;
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        Hartonomous::BLAKE3Pipeline::Hash hash;
        std::memcpy(hash.data(), hash_16b, 16);
        std::string hex_id = Hartonomous::BLAKE3Pipeline::to_hex(hash);
        bool found = false;
        db->query(
            "SELECT ST_X(p.centroid), ST_Y(p.centroid), ST_Z(p.centroid), ST_M(p.centroid) "
            "FROM hartonomous.physicality p "
            "JOIN hartonomous.composition c ON c.physicalityid = p.id "
            "WHERE c.id = $1",
            {hex_id},
            [&](const std::vector<std::string>& row) {
                out_4d[0] = std::stod(row[0]);
                out_4d[1] = std::stod(row[1]);
                out_4d[2] = std::stod(row[2]);
                out_4d[3] = std::stod(row[3]);
                found = true;
            }
        );
        return found;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

void hartonomous_free_string(char* str) {
    if (str) free(str);
}

// =============================================================================
//  Text Generation (Walk → Text)
// =============================================================================

// Hash a prompt into a starting composition by extracting its BLAKE3 hash
static Hartonomous::BLAKE3Pipeline::Hash prompt_to_seed(
    Hartonomous::PostgresConnection& db, const std::string& prompt) {
    // Try to find the prompt text (or a keyword from it) as an existing composition.
    // If not found, hash the whole prompt as a seed.
    Hartonomous::SemanticQuery query(db);

    // Try exact match first
    auto comp = query.get_composition_info(prompt);
    if (comp) return Hartonomous::BLAKE3Pipeline::from_hex(comp->hash);

    // Extract keywords from the prompt and find the highest-confidence related composition
    auto keywords = query.extract_keywords(prompt);
    for (const auto& kw : keywords) {
        auto kw_comp = query.get_composition_info(kw);
        if (kw_comp) return Hartonomous::BLAKE3Pipeline::from_hex(kw_comp->hash);
    }

    // Fallback: hash the prompt bytes directly
    return Hartonomous::BLAKE3Pipeline::hash(prompt.data(), prompt.size());
}

static Hartonomous::WalkParameters map_generate_params(const HGenerateParams* params) {
    Hartonomous::WalkParameters wp;
    if (params->temperature > 0.0) wp.base_temp = params->temperature;
    if (params->energy_decay > 0.0) wp.energy_decay = params->energy_decay;
    return wp;
}

bool hartonomous_generate(h_walk_engine_t walk_handle, h_db_connection_t db_handle,
                          const char* prompt, const HGenerateParams* params,
                          HGenerateResult* out_result) {
    try {
        if (!walk_handle || !db_handle || !prompt || !params || !out_result) return false;
        auto* engine = static_cast<Hartonomous::WalkEngine*>(walk_handle);

        auto wp = map_generate_params(params);
        size_t max_steps = (params->max_tokens > 0) ? params->max_tokens : 50;

        auto text = engine->generate(prompt, wp, max_steps);

        out_result->text = strdup_safe(text);
        out_result->steps = max_steps;
        out_result->total_energy_used = 1.0;
        std::strncpy(out_result->finish_reason, "stop", 63);
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

bool hartonomous_generate_stream(h_walk_engine_t walk_handle, h_db_connection_t db_handle,
                                  const char* prompt, const HGenerateParams* params,
                                  HGenerateCallback callback, void* user_data,
                                  HGenerateResult* out_result) {
    try {
        if (!walk_handle || !db_handle || !prompt || !params || !callback || !out_result) return false;
        auto* engine = static_cast<Hartonomous::WalkEngine*>(walk_handle);

        auto wp = map_generate_params(params);
        size_t max_steps = (params->max_tokens > 0) ? params->max_tokens : 50;

        auto state = engine->init_walk_from_prompt(prompt, 1.0);

        std::ostringstream full_output;
        size_t steps = 0;
        std::string prev_text;

        while (steps < max_steps) {
            auto result = engine->step(state, wp);
            if (result.terminated) {
                std::strncpy(out_result->finish_reason, result.reason.c_str(), 63);
                out_result->finish_reason[63] = '\0';
                break;
            }

            auto text = engine->lookup_text(result.next_composition);
            if (!text.empty() && text != prev_text) {
                std::string token = (full_output.tellp() == 0) ? text : (" " + text);
                full_output << token;
                if (!callback(token.c_str(), steps, result.energy_remaining, user_data)) {
                    std::strncpy(out_result->finish_reason, "stop", 63);
                    break;
                }
                prev_text = text;
            }
            ++steps;
        }

        if (out_result->finish_reason[0] == '\0') {
            std::strncpy(out_result->finish_reason, "length", 63);
        }

        out_result->text = strdup_safe(full_output.str());
        out_result->steps = steps;
        out_result->total_energy_used = 1.0 - state.current_energy;
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

// =============================================================================
//  Semantic Query
// =============================================================================

h_query_t hartonomous_query_create(h_db_connection_t db_handle) {
    try {
        if (!db_handle) throw std::runtime_error("Invalid database handle");
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        auto* query = new Hartonomous::SemanticQuery(*db);
        return static_cast<h_query_t>(query);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_query_destroy(h_query_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::SemanticQuery*>(handle);
    }
}

bool hartonomous_query_related(h_query_t handle, const char* text, size_t limit,
                               HQueryResult** out_results, size_t* out_count) {
    try {
        if (!handle || !text || !out_results || !out_count) return false;
        auto* query = static_cast<Hartonomous::SemanticQuery*>(handle);
        auto results = query->find_related(text, limit);

        *out_count = results.size();
        if (results.empty()) { *out_results = nullptr; return true; }

        *out_results = new HQueryResult[results.size()];
        for (size_t i = 0; i < results.size(); ++i) {
            (*out_results)[i].text = strdup_safe(results[i].text);
            (*out_results)[i].confidence = results[i].confidence;
        }
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

bool hartonomous_query_truth(h_query_t handle, const char* text, double min_elo,
                              size_t limit, HQueryResult** out_results, size_t* out_count) {
    try {
        if (!handle || !text || !out_results || !out_count) return false;
        auto* query = static_cast<Hartonomous::SemanticQuery*>(handle);
        auto results = query->find_gravitational_truth(text, min_elo, limit);

        *out_count = results.size();
        if (results.empty()) { *out_results = nullptr; return true; }

        *out_results = new HQueryResult[results.size()];
        for (size_t i = 0; i < results.size(); ++i) {
            (*out_results)[i].text = strdup_safe(results[i].text);
            (*out_results)[i].confidence = results[i].confidence;
        }
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

bool hartonomous_query_answer(h_query_t handle, const char* question, HQueryResult* out_result) {
    try {
        if (!handle || !question || !out_result) return false;
        auto* query = static_cast<Hartonomous::SemanticQuery*>(handle);
        auto result = query->answer_question(question);

        if (!result) {
            out_result->text = nullptr;
            out_result->confidence = 0.0;
            return true; // No answer found is not an error
        }

        out_result->text = strdup_safe(result->text);
        out_result->confidence = result->confidence;
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

void hartonomous_query_free_results(HQueryResult* results, size_t count) {
    if (!results) return;
    for (size_t i = 0; i < count; ++i) {
        if (results[i].text) free(results[i].text);
    }
    delete[] results;
}
