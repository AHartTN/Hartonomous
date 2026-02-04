#include <interop_api.h>
#include <cognitive/godel_engine.hpp>
#include <cognitive/walk_engine.hpp>
#include <ingestion/text_ingester.hpp>
#include <database/postgres_connection.hpp>
#include <stdexcept>
#include <cstring>
#include <memory>

// Thread-local error storage
thread_local std::string g_last_error;

const char* hartonomous_get_last_error() {
    return g_last_error.c_str();
}

void set_error(const std::exception& e) {
    g_last_error = e.what();
}

void set_error(const char* msg) {
    g_last_error = msg;
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
    try {
        auto* db = new Hartonomous::PostgresConnection(connection_string);
        return static_cast<h_db_connection_t>(db);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_db_destroy(h_db_connection_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::PostgresConnection*>(handle);
    }
}

bool hartonomous_db_is_connected(h_db_connection_t handle) {
    if (!handle) return false;
    // Assuming PostgresConnection has an is_connected() or similar check, 
    // or just valid instance implies connection attempted.
    // For now we assume if the object exists, it's "valid" enough.
    // Ideally PostgresConnection should expose connection state.
    return true; 
}

#include <ingestion/universal_ingester.hpp>

// ... in hartonomous_ingester_create ...
h_ingester_t hartonomous_ingester_create(h_db_connection_t db_handle) {
    try {
        if (!db_handle) throw std::runtime_error("Invalid database handle");
        auto* db = static_cast<Hartonomous::PostgresConnection*>(db_handle);
        auto* ingester = new Hartonomous::UniversalIngester(*db);
        return static_cast<h_ingester_t>(ingester);
    } catch (const std::exception& e) {
        set_error(e);
        return nullptr;
    }
}

void hartonomous_ingester_destroy(h_ingester_t handle) {
    if (handle) {
        delete static_cast<Hartonomous::UniversalIngester*>(handle);
    }
}

bool hartonomous_ingest_text(h_ingester_t handle, const char* text, HIngestionStats* out_stats) {
    try {
        if (!handle || !text || !out_stats) return false;
        auto* ingester = static_cast<Hartonomous::UniversalIngester*>(handle);
        auto stats = ingester->ingest_text(text);
        
        // ... mapping stats ...
        out_stats->atoms_total = stats.atoms_total;
        out_stats->atoms_new = stats.atoms_new;
        out_stats->compositions_total = stats.compositions_total;
        out_stats->compositions_new = stats.compositions_new;
        out_stats->relations_total = stats.relations_total;
        out_stats->relations_new = stats.relations_new;
        out_stats->evidence_count = stats.evidence_count;
        out_stats->original_bytes = stats.original_bytes;
        out_stats->ngrams_extracted = stats.ngrams_extracted;
        out_stats->ngrams_significant = stats.ngrams_significant;
        out_stats->cooccurrences_found = stats.cooccurrences_found;
        out_stats->cooccurrences_significant = stats.cooccurrences_significant;

        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
}

bool hartonomous_ingest_file(h_ingester_t handle, const char* file_path, HIngestionStats* out_stats) {
    try {
        if (!handle || !file_path || !out_stats) return false;
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
        out_stats->ngrams_extracted = stats.ngrams_extracted;
        out_stats->ngrams_significant = stats.ngrams_significant;
        out_stats->cooccurrences_found = stats.cooccurrences_found;
        out_stats->cooccurrences_significant = stats.cooccurrences_significant;
        
        return true;
    } catch (const std::exception& e) {
        set_error(e);
        return false;
    }
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
    
    // We don't free the plan pointer itself as it's likely a stack object or managed by caller
    // but we clear its members
    plan->original_problem = nullptr;
    plan->sub_problems = nullptr;
    plan->knowledge_gaps = nullptr;
}
